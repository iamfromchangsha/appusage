using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace AppUsage.Core;

/// <summary>
/// 随机使用截图的后台服务：
/// 独立线程按间隔抓当前前台窗口 → 存缩略图 → 上传到 GitHub 私有仓 → 成功后不留全图；
/// 上传失败的进 outbox，等下次重试。**绝不阻塞 1 秒一次的追踪轮询。**
/// </summary>
internal sealed class ShotService : IDisposable
{
    private const long MinFreeBytes = 1024L * 1024 * 1024;   // 剩余不足 1GB 就停拍

    private readonly Config _config;
    private readonly string _dataDir;
    private readonly ShotUploader? _uploader;
    private readonly Thread _thread;
    private volatile bool _running;
    private DateTime _nextDue = DateTime.MinValue;
    private int _consecutiveFailures;

    public string Status { get; private set; } = "未启用";
    public event Action<string>? StatusChanged;

    /// <summary>当前运行的实例（供 UI 显示状态）。</summary>
    public static ShotService? Current { get; private set; }

    public ShotService(Config config, string dataDir, ShotUploader? uploader)
    {
        _config = config;
        _dataDir = dataDir;
        _uploader = uploader;
        _thread = new Thread(Loop) { IsBackground = true, Name = "AppUsage.Shots", Priority = ThreadPriority.BelowNormal };
    }

    public void Start() { _running = true; Current = this; _thread.Start(); }

    public void Stop() { _running = false; }

    public string ThumbsDirFor(DateTime day) =>
        Path.Combine(_dataDir, "shots", "thumbs", day.ToString("yyyy-MM-dd"));

    public string OutboxDir => Path.Combine(_dataDir, "shots", "outbox");

    private void Loop()
    {
        // 启动时先补传 outbox
        TryFlushOutbox();

        while (_running)
        {
            try { Thread.Sleep(3000); } catch { break; }
            if (!_running) break;
            try { MaybeCapture(); }
            catch { /* 单次失败不影响下一轮 */ }
        }
    }

    private void SetStatus(string s)
    {
        if (Status == s) return;
        Status = s;
        try { StatusChanged?.Invoke(s); } catch { }
    }

    private void MaybeCapture()
    {
        var shots = _config.Shots;
        if (!shots.Enabled) { SetStatus("未启用"); return; }

        var now = DateTime.Now;
        if (now < _nextDue) return;
        _nextDue = now.AddMinutes(Math.Max(1, shots.IntervalMinutes));

        // 磁盘保护
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(_dataDir));
            if (!string.IsNullOrEmpty(root))
            {
                var free = new DriveInfo(root).AvailableFreeSpace;
                if (free < MinFreeBytes)
                {
                    SetStatus($"磁盘剩余 {free / 1024 / 1024} MB，已暂停截图");
                    return;
                }
            }
        }
        catch { }

        // 每天上限
        var todayDir = ThumbsDirFor(now);
        int todayCount = Directory.Exists(todayDir) ? Directory.GetFiles(todayDir, "*.jpg").Length : 0;
        if (todayCount >= Math.Max(1, shots.MaxPerDay)) { SetStatus($"今日已达上限 {shots.MaxPerDay} 张"); return; }

        // 用户不在就不拍（拍到的是静止画面，没意义）
        if (NativeMethods.IdleSeconds() > 120) { SetStatus("空闲中，暂不截图"); return; }

        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;

        var (exe, title, _) = NativeMethods.GetForeground();
        var appKey = _config.AppKey(exe, title);
        if (shots.Excluded.Any(e => string.Equals(e, appKey, StringComparison.OrdinalIgnoreCase))) return;
        if (_config.ExcludedExes.Any(e => string.Equals(e, appKey, StringComparison.OrdinalIgnoreCase))) return;

        if (!Screenshotter.TryCapture(hwnd, shots.FullWidth, shots.JpegQuality, out var jpeg)) return;

        var name = $"{now:HH-mm-ss}_{Sanitize(appKey)}";
        SaveThumb(todayDir, name, jpeg, shots.ThumbWidth, shots.JpegQuality);

        var relPath = $"shots/{now:yyyy}/{now:MM}/{now:DD}/{name}.jpg";
        _ = UploadAsync(relPath, jpeg, name, now);
    }

    private async Task UploadAsync(string relPath, byte[] jpeg, string name, DateTime at)
    {
        if (_uploader is null || !_uploader.Configured)
        {
            // 没配 GitHub：全图留在 outbox，等配好再补传
            SaveOutbox(name, jpeg, at);
            SetStatus("未配置 GitHub，图片暂存在本地待上传");
            return;
        }

        try
        {
            var err = await _uploader.UploadAsync(relPath, jpeg, $"appusage shot {at:yyyy-MM-dd HH:mm:ss}", CancellationToken.None)
                                   .ConfigureAwait(false);
            if (err is null)
            {
                _consecutiveFailures = 0;
                SetStatus("截图已存 GitHub 私有仓库");
                return;
            }
            if (err.Contains("401") || err.Contains("Bad credentials"))
            {
                SetStatus("GitHub token 无效，暂停上传");
                return;
            }
            SaveOutbox(name, jpeg, at);
            _consecutiveFailures++;
            SetStatus($"上传失败，已存本地待重试（{err}）");
        }
        catch (Exception ex)
        {
            SaveOutbox(name, jpeg, at);
            SetStatus("上传异常：" + ex.Message);
        }
    }

    /// <summary>把 outbox 里积压的图片补传上去。</summary>
    public void TryFlushOutbox()
    {
        if (_uploader is null || !_uploader.Configured) return;
        var dir = OutboxDir;
        if (!Directory.Exists(dir)) return;

        foreach (var f in Directory.GetFiles(dir, "*.jpg").Take(20))
        {
            try
            {
                var name = Path.GetFileNameWithoutExtension(f);
                var at = ParseAt(name);
                var rel = $"shots/{at:yyyy}/{at:MM}/{at:DD}/{name}.jpg";
                var err = _uploader.UploadAsync(rel, File.ReadAllBytes(f), $"appusage shot {at:yyyy-MM-dd HH:mm:ss}", CancellationToken.None)
                                   .GetAwaiter().GetResult();
                if (err is null)
                {
                    try { File.Delete(f); } catch { }
                    SetStatus("已补传历史截图");
                }
            }
            catch { }
        }
    }

    private void SaveThumb(string dir, string name, byte[] jpeg, int thumbWidth, int quality)
    {
        try
        {
            Directory.CreateDirectory(dir);
            using var ms = new MemoryStream(jpeg);
            using var src = Image.FromStream(ms);
            int nw = Math.Min(thumbWidth, src.Width);
            int nh = (int)Math.Round(src.Height * (double)nw / src.Width);
            using var thumb = new Bitmap(nw, nh, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(thumb))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(src, 0, 0, nw, nh);
            }
            var codec = ImageCodecInfo.GetImageEncoders().FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);
            if (codec is null) thumb.Save(Path.Combine(dir, name + ".jpg"), ImageFormat.Jpeg);
            else
            {
                using var ep = new EncoderParameters(1);
                ep.Param[0] = new EncoderParameter(Encoder.Quality, (long)Math.Clamp(quality, 20, 95));
                thumb.Save(Path.Combine(dir, name + ".jpg"), codec, ep);
            }
        }
        catch { }
    }

    private void SaveOutbox(string name, byte[] jpeg, DateTime at)
    {
        try
        {
            Directory.CreateDirectory(OutboxDir);
            // outbox 上限 200MB：超了丢最旧的
            var files = Directory.GetFiles(OutboxDir, "*.jpg").OrderBy(f => File.GetLastWriteTimeUtc(f)).ToList();
            long total = files.Sum(f => new FileInfo(f).Length);
            const long cap = 200L * 1024 * 1024;
            foreach (var old in files)
            {
                if (total <= cap) break;
                try { total -= new FileInfo(old).Length; File.Delete(old); } catch { }
            }
            File.WriteAllBytes(Path.Combine(OutboxDir, $"{at:yyyy-MM-dd}_{name}.jpg"), jpeg);
        }
        catch { }
    }

    private static DateTime ParseAt(string name)
    {
        // 形如 2026-10-22_15-04-05_app
        try
        {
            var parts = name.Split('_');
            if (parts.Length >= 2)
                return DateTime.ParseExact(parts[0] + "_" + parts[1], "yyyy-MM-dd_HH-mm-ss", null);
        }
        catch { }
        return DateTime.Now;
    }

    private static string Sanitize(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var c in s)
            sb.Append(char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_');
        var r = sb.ToString();
        return r.Length > 24 ? r[..24] : r;
    }

    public void Dispose() => Stop();
}
