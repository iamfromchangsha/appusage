using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Text;

namespace AppUsage.Core;

/// <summary>
/// 随机使用截图的后台服务：
/// 独立线程按间隔抓当前前台窗口 → 存缩略图 → 上传到 GitHub 私有仓 → 成功后不留全图；
/// 上传失败的进 outbox，等后续重试。**绝不阻塞 1 秒一次的追踪轮询。**
/// </summary>
/// <remarks>
/// <b>命名约定（全链路统一，别再各拼一遍格式串）</b>
/// <list type="bullet">
/// <item>原始名 / 缩略图名：<c>{HH-mm-ss}_{app}</c>，例如 <c>12-29-31_msedge</c></item>
/// <item>GitHub 相对路径：<c>shots/{yyyy}/{MM}/{dd}/{原始名}.jpg</c></item>
/// <item>outbox 文件名：<c>{yyyy-MM-dd}_{原始名}.jpg</c>（多带日期前缀，纯粹为了排序和看年龄）</item>
/// <item>上传清单 index.tsv：<c>{原始名}\t{GitHub 相对路径}</c>，**键必须是原始名**</item>
/// </list>
/// 早期版本 outbox 补传时把日期前缀写进了 GitHub 路径和清单键名，
/// 导致日报按缩略图名找不到、显示成「未上传」，路径也和直传的对不上。
/// </remarks>
internal sealed class ShotService : IDisposable
{
    private const long MinFreeBytes = 1024L * 1024 * 1024;   // 剩余不足 1GB 就停拍
    private const long OutboxCapBytes = 200L * 1024 * 1024;  // outbox 上限 200MB
    private const int MaxFlushPerRound = 20;                // 一轮最多补传几张
    private static readonly TimeSpan OutboxRetryInterval = TimeSpan.FromMinutes(10);

    private readonly Config _config;
    private readonly string _dataDir;
    private ShotUploader? _uploader;
    private readonly Thread _thread;
    private volatile bool _running;
    private DateTime _nextDue = DateTime.MinValue;
    private DateTime _nextOutboxRetry = DateTime.MinValue;
    private int _consecutiveFailures;

    // outbox 体积缓存：每次失败都全盘统计太贵（200MB 能有几千个文件）
    private long _outboxBytes = -1;

    // 清单键名缓存：避免每次上传都把整个 index.tsv 读进内存做子串匹配
    private readonly object _manifestLock = new();
    private HashSet<string>? _manifestKeys;

    private static readonly Lazy<ImageCodecInfo?> JpegCodec = new(() =>
        ImageCodecInfo.GetImageEncoders().FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid));

    public string Status { get; private set; } = "未启用";
    public event Action<string>? StatusChanged;

    /// <summary>当前运行的实例（供 UI 显示状态）。</summary>
    public static ShotService? Current { get; private set; }

    public ShotService(Config config, string dataDir)
    {
        _config = config;
        _dataDir = dataDir;
        _thread = new Thread(Loop) { IsBackground = true, Name = "AppUsage.Shots", Priority = ThreadPriority.BelowNormal };
    }

    /// <summary>上传器懒加载：不开启截图时就不用把 HttpClient / JSON 序列化器拖进内存。</summary>
    private ShotUploader? Uploader => _uploader ??= _config.Shots.HasGithub ? new ShotUploader(_config) : null;

    public void Start()
    {
        _running = true;
        Current = this;
        Program.Trace($"[shots] 启动：enabled={_config.Shots.Enabled} token={_config.Shots.GithubToken.Length}字符 " +
                      $"owner={_config.Shots.GithubOwner} repo={_config.Shots.GithubRepo} " +
                      $"interval={_config.Shots.IntervalMinutes}分 max={_config.Shots.MaxPerDay} " +
                      $"dataDir={_dataDir}");
        MigrateManifest();
        _thread.Start();
    }

    public void Stop() { _running = false; }

    public string ThumbsDirFor(DateTime day) =>
        Path.Combine(_dataDir, "shots", "thumbs", day.ToString("yyyy-MM-dd"));

    public string OutboxDir => Path.Combine(_dataDir, "shots", "outbox");

    private void Loop()
    {
        // 启动时先补传 outbox
        TryFlushOutbox();
        _nextOutboxRetry = DateTime.Now.Add(OutboxRetryInterval);

        while (_running)
        {
            try { Thread.Sleep(3000); } catch { break; }
            if (!_running) break;
            try { MaybeCapture(); }
            catch (Exception ex) { Program.Trace("[shots] 采集异常: " + ex); }

            // 积压的图要**在运行期间**也补传，不能只靠下次启动
            if (DateTime.Now >= _nextOutboxRetry)
            {
                _nextOutboxRetry = DateTime.Now.Add(OutboxRetryInterval);
                try { TryFlushOutbox(); }
                catch (Exception ex) { Program.Trace("[shots] 补传异常: " + ex); }
            }
        }
    }

    private void SetStatus(string s)
    {
        if (Status == s) return;
        Status = s;
        Program.Trace($"[shots] {s}");
        try { StatusChanged?.Invoke(s); } catch { }
    }

    private void MaybeCapture()
    {
        var shots = _config.Shots;
        if (!shots.Enabled) { SetStatus("未启用"); return; }

        var now = DateTime.Now;
        if (now < _nextDue) return;
        _nextDue = now.AddMinutes(Math.Max(1, shots.IntervalMinutes));
        Program.Trace($"[shots] 尝试截图（间隔 {shots.IntervalMinutes} 分钟）");

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
        if (hwnd == IntPtr.Zero) { SetStatus("没有前台窗口"); return; }

        var (exe, title, pid) = NativeMethods.GetForeground();

        // 别拍自己：追踪器已经排除自身了，截图也得一样
        // （否则主界面开着的时候，拍到的全是自己的窗口，纯浪费额度）
        if (pid != 0 && pid == (uint)Environment.ProcessId)
        {
            SetStatus("前台是本程序，跳过");
            return;
        }

        var appKey = _config.AppKey(exe, title);
        if (shots.Excluded.Any(e => string.Equals(e, appKey, StringComparison.OrdinalIgnoreCase)))
        {
            SetStatus($"已跳过 {appKey}（在排除列表里）");
            return;
        }
        if (_config.ExcludedExes.Any(e => string.Equals(e, appKey, StringComparison.OrdinalIgnoreCase)))
        {
            SetStatus($"已跳过 {appKey}（全局排除）");
            return;
        }

        if (!Screenshotter.TryCapture(hwnd, shots.FullWidth, shots.JpegQuality, out var jpeg))
        {
            SetStatus("抓图失败（黑图或窗口不可抓）");
            return;
        }

        var name = $"{now:HH-mm-ss}_{Sanitize(appKey)}";
        SaveThumb(todayDir, name, jpeg, shots.ThumbWidth, shots.JpegQuality);

        // 注意：.NET 自定义日期格式里必须用小写 dd（大写 DD 会被原样输出成字面量 "DD"）
        var relPath = RelPath(now, name);
        _ = UploadAsync(relPath, jpeg, name, now);
    }

    /// <summary>GitHub 上的相对路径（全链路唯一真源，别在别处再拼一遍）。</summary>
    internal static string RelPath(DateTime at, string name) =>
        $"shots/{at:yyyy}/{at:MM}/{at:dd}/{name}.jpg";

    private async Task UploadAsync(string relPath, byte[] jpeg, string name, DateTime at)
    {
        if (Uploader is null || !Uploader!.Configured)
        {
            // 没配 GitHub：全图留在 outbox，等配好再补传
            SaveOutbox(name, jpeg, at);
            SetStatus("未配置 GitHub，图片暂存在本地待上传");
            return;
        }

        try
        {
            var err = await Uploader!.UploadAsync(relPath, jpeg, $"appusage shot {at:yyyy-MM-dd HH:mm:ss}", CancellationToken.None)
                                   .ConfigureAwait(false);
            if (err is null)
            {
                _consecutiveFailures = 0;
                RecordUploaded(name, relPath);
                SetStatus("截图已存 GitHub 私有仓库");
                return;
            }
            if (err.Contains("401") || err.Contains("Bad credentials"))
            {
                // token 无效也先把图存下来，别白抓一次
                SaveOutbox(name, jpeg, at);
                SetStatus($"GitHub token 无效（{err}），图片暂存本地，暂停上传");
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
        if (Uploader is null || !Uploader!.Configured) return;
        var dir = OutboxDir;
        if (!Directory.Exists(dir)) return;

        var files = Directory.GetFiles(dir, "*.jpg").OrderBy(f => f, StringComparer.Ordinal).ToArray();
        if (files.Length == 0) return;

        foreach (var f in files.Take(MaxFlushPerRound))
        {
            try
            {
                // outbox 文件名带日期前缀，补传时要还原成原始名，
                // 否则 GitHub 上的路径会多一层日期，日报里的链接也对不上
                var (at, name) = ParseOutboxName(f);
                var rel = RelPath(at, name);

                var err = Uploader!.UploadAsync(rel, File.ReadAllBytes(f), $"appusage shot {at:yyyy-MM-dd HH:mm:ss}", CancellationToken.None)
                                   .GetAwaiter().GetResult();
                if (err is null)
                {
                    TryDelete(f);
                    _outboxBytes -= SafeLength(f);
                    RecordUploaded(name, rel);
                    _consecutiveFailures = 0;
                    SetStatus("已补传历史截图");
                }
                else
                {
                    // 这一轮先停，别对着 GitHub 反复重试
                    _consecutiveFailures++;
                    _nextOutboxRetry = DateTime.Now.AddMinutes(BackoffMinutes(_consecutiveFailures));
                    SetStatus($"补传失败，稍后重试（{err}）");
                    return;
                }
            }
            catch (Exception ex)
            {
                SetStatus("补传异常：" + ex.Message);
                return;
            }
        }
    }

    /// <summary>失败越多等得越久（1→2→4→8…，最多 60 分钟）。</summary>
    private static int BackoffMinutes(int failures) =>
        Math.Min(60, 1 << Math.Clamp(failures - 1, 0, 6));

    /// <summary>
    /// outbox 文件名 → (拍摄时间, 原始名)。
    /// 形如 <c>2026-10-23_15-04-05_edge</c> → (<c>2026-10-23 15:04:05</c>, <c>15-04-05_edge</c>)
    /// </summary>
    internal static (DateTime At, string Name) ParseOutboxName(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var cut = stem.IndexOf('_');
        if (cut > 0)
        {
            var datePart = stem[..cut];
            var rest = stem[(cut + 1)..];
            if (datePart.Length == 10 &&
                DateTime.TryParseExact(datePart, "yyyy-MM-dd", null, DateTimeStyles.None, out var d))
            {
                return (d.Date + ParseClock(rest), rest);
            }
        }
        return (DateTime.Now, stem);
    }

    /// <summary>把 <c>2026-10-23_12-29-31_msedge</c> 还原成 <c>12-29-31_msedge</c>；不是这种格式就原样返回。</summary>
    internal static string NormalizeName(string name)
    {
        var cut = name.IndexOf('_');
        if (cut <= 0) return name;
        var head = name[..cut];
        if (head.Length != 10) return name;
        if (!DateTime.TryParseExact(head, "yyyy-MM-dd", null, DateTimeStyles.None, out _)) return name;
        return name[(cut + 1)..];
    }

    /// <summary>从 <c>15-04-05_edge</c> 里取出 15:04:05。</summary>
    private static TimeSpan ParseClock(string s)
    {
        var head = s.Split('_', 2)[0];
        var t = head.Split('-');
        if (t.Length == 3 &&
            int.TryParse(t[0], out var h) && int.TryParse(t[1], out var m) && int.TryParse(t[2], out var sec))
            return new TimeSpan(h, m, sec);
        return TimeSpan.Zero;
    }

    /// <summary>上传成功的截图清单（本地缩略图名 → GitHub 路径），只有真传上去的才给 GitHub 链接。</summary>
    public string ManifestPath => Path.Combine(_dataDir, "shots", "index.tsv");

    private void RecordUploaded(string name, string relPath)
    {
        lock (_manifestLock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ManifestPath)!);
                _manifestKeys ??= LoadManifestKeys();
                if (_manifestKeys.Contains(name)) return;
                File.AppendAllText(ManifestPath, name + "\t" + relPath + "\n");
                _manifestKeys.Add(name);
            }
            catch { }
        }
    }

    private HashSet<string> LoadManifestKeys()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(ManifestPath)) return set;
            foreach (var line in File.ReadAllLines(ManifestPath))
            {
                var parts = line.Split('\t');
                if (parts.Length >= 2) set.Add(parts[0]);
            }
        }
        catch { }
        return set;
    }

    /// <summary>
    /// 修历史遗留的清单：早期 outbox 补传把日期前缀写进了键名和路径，
    /// 日报按缩略图名（原始名）就找不到它，显示成「未上传」。这里统一还原。
    /// </summary>
    private void MigrateManifest()
    {
        try
        {
            if (!File.Exists(ManifestPath)) return;
            var (lines, changed) = NormalizeManifestLines(File.ReadAllLines(ManifestPath));
            if (changed)
                File.WriteAllLines(ManifestPath, lines, new UTF8Encoding(false));

            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines)
            {
                var parts = line.Split('\t');
                if (parts.Length >= 2) keys.Add(parts[0]);
            }
            lock (_manifestLock) { _manifestKeys = keys; }
        }
        catch { }
    }

    /// <summary>
    /// 把清单里的键名 / 路径统一还原成规范形式，并顺手去重。
    /// 返回 (新行集合, 是否发生过改动)。
    /// </summary>
    internal static (List<string> Lines, bool Changed) NormalizeManifestLines(IEnumerable<string> lines)
    {
        var outLines = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var changed = false;

        foreach (var line in lines)
        {
            if (line.Length == 0) continue;
            var parts = line.Split('\t');
            if (parts.Length < 2) { outLines.Add(line); continue; }

            var key = parts[0];
            var path = parts[1];
            var norm = NormalizeName(key);
            var newPath = norm == key
                ? path
                : path.Replace("/" + key + ".jpg", "/" + norm + ".jpg");

            if (norm != key || newPath != path) changed = true;
            if (!seen.Add(norm)) { changed = true; continue; }   // 顺手去重
            outLines.Add(norm + "\t" + newPath);
        }
        return (outLines, changed);
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
            var codec = JpegCodec.Value;
            if (codec is null) thumb.Save(Path.Combine(dir, name + ".jpg"), ImageFormat.Jpeg);
            else
            {
                using var ep = new EncoderParameters(1);
                ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)Math.Clamp(quality, 20, 95));
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
            EnforceOutboxCap();
            var path = Path.Combine(OutboxDir, $"{at:yyyy-MM-dd}_{name}.jpg");
            File.WriteAllBytes(path, jpeg);
            _outboxBytes += jpeg.Length;
        }
        catch { }
    }

    /// <summary>outbox 超过 200MB 就丢最旧的。体积走缓存，别每次失败都全盘统计。</summary>
    private void EnforceOutboxCap()
    {
        try
        {
            if (!Directory.Exists(OutboxDir)) return;
            var files = Directory.GetFiles(OutboxDir, "*.jpg");
            if (_outboxBytes < 0)
                _outboxBytes = files.Sum(f => SafeLength(f));
            if (_outboxBytes <= OutboxCapBytes) return;

            foreach (var old in files.OrderBy(f => f, StringComparer.Ordinal))
            {
                if (_outboxBytes <= OutboxCapBytes) break;
                var len = SafeLength(old);
                if (TryDelete(old)) _outboxBytes -= len;
            }
        }
        catch { }
    }

    private static long SafeLength(string path)
    {
        try { return new FileInfo(path).Length; } catch { return 0; }
    }

    private static bool TryDelete(string path)
    {
        try { File.Delete(path); return !File.Exists(path); }
        catch { return false; }
    }

    private static string Sanitize(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s)
            sb.Append(char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_');
        var r = sb.ToString();
        return r.Length > 24 ? r[..24] : r;
    }

    public void Dispose() => Stop();
}
