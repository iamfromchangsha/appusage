using System.Drawing;
using System.Globalization;
using System.Text;

namespace AppUsage.Core;

/// <summary>
/// 日志存储与增量聚合。
/// 日志是纯文本追加文件，读取时只吃「上次位置之后的新增行」，
/// 并把数据压成 (天 → 天/应用/小时) 和 (应用 → 累计) 两层聚合，
/// 这样即使攒了一年，内存也只占几 MB。
/// </summary>
internal sealed class Store
{
    private readonly string _logPath;
    private readonly Config _config;
    private readonly object _sync = new();
    private readonly List<byte> _pending = new();
    private long _offset;

    public readonly SortedDictionary<DateTime, DayBucket> Days = new();
    public readonly Dictionary<string, AppAggregate> Apps = new();

    public Store(string logPath, Config config)
    {
        _logPath = logPath;
        _config = config;
    }

    public double TotalSeconds { get; private set; }
    public int ActiveDays { get; private set; }

    /// <summary>数据版本号：有新数据进来才 +1，UI 据此决定要不要重建。</summary>
    public int Version { get; private set; }

    /// <summary>读取日志里新增的行并更新聚合。</summary>
    public void Reload()
    {
        lock (_sync)
        {
            try
            {
                var info = new FileInfo(_logPath);
                if (!info.Exists) return;
                if (info.Length <= _offset) { RecomputeWindows(); return; }

                using var fs = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                fs.Seek(_offset, SeekOrigin.Begin);
                var buf = new byte[info.Length - _offset];
                var read = fs.Read(buf, 0, buf.Length);
                if (read > 0) _pending.AddRange(buf.AsSpan(0, read).ToArray());

                // 只处理完整的行，残留的半行留到下次
                int lastNl = -1;
                for (int i = 0; i < _pending.Count; i++)
                    if (_pending[i] == (byte)'\n') lastNl = i;
                if (lastNl < 0) { RecomputeWindows(); return; }

                var text = Encoding.UTF8.GetString(_pending.ToArray(), 0, lastNl + 1);
                _pending.RemoveRange(0, lastNl + 1);
                _offset += lastNl + 1;

                foreach (var raw in text.Split('\n'))
                {
                    var line = raw.TrimEnd('\r');
                    if (line.Length == 0) continue;
                    Apply(line);
                }
                Version++;
            }
            catch { /* 日志正在被写，读失败就下一轮再读 */ }

            RecomputeWindows();
        }
    }

    private void Apply(string line)
    {
        var parts = line.Split('\t');
        if (parts.Length < 6) return;
        if (!double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var start)) return;
        if (!double.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var duration)) return;

        var key = parts[0];
        if (key.Length == 0) return;
        var exe = parts[1];

        var local = DateTimeOffset.FromUnixTimeMilliseconds((long)(start * 1000)).LocalDateTime;
        var day = local.Date;
        var hour = local.Hour;

        if (!Days.TryGetValue(day, out var bucket))
        {
            bucket = new DayBucket();
            Days[day] = bucket;
        }
        bucket.Total += duration;
        bucket.Apps[key] = bucket.Apps.GetValueOrDefault(key) + duration;
        bucket.Hours[hour] = bucket.Hours.GetValueOrDefault(hour) + duration;

        if (!Apps.TryGetValue(key, out var app))
        {
            app = new AppAggregate { Key = key };
            Apps[key] = app;
        }
        app.Name = _config.DisplayName(key);
        if (exe.Length > 0) app.ExePath = exe;
        app.Total += duration;
        app.Sessions++;
        if (start + duration > app.LastSeen) app.LastSeen = start + duration;

        TotalSeconds += duration;
    }

    /// <summary>重算「今日 / 近7天 / 近30天」这类窗口指标。</summary>
    private void RecomputeWindows()
    {
        if (Apps.Count == 0) return;
        var today = DateTime.Today;
        var week = today.AddDays(-6);
        var month = today.AddDays(-29);

        foreach (var app in Apps.Values) { app.Today = 0; app.Week = 0; app.Month = 0; }
        ActiveDays = 0;

        foreach (var (day, bucket) in Days)
        {
            if (bucket.Total <= 0) continue;
            ActiveDays++;
            bool isToday = day == today;
            bool inWeek = day >= week;
            bool inMonth = day >= month;
            if (!isToday && !inWeek && !inMonth) continue;

            foreach (var (key, seconds) in bucket.Apps)
            {
                if (!Apps.TryGetValue(key, out var app)) continue;
                if (isToday) app.Today += seconds;
                if (inWeek) app.Week += seconds;
                if (inMonth) app.Month += seconds;
            }
        }
    }

    // ---------------- 查询 ----------------

    public IReadOnlyList<DayPoint> Heatmap(int weeks)
    {
        var today = DateTime.Today;
        var endDow = (int)today.DayOfWeek;           // 0 = 周日
        var weekStart = today.AddDays(-endDow);
        var gridStart = weekStart.AddDays(-(weeks - 1) * 7);

        var result = new List<DayPoint>(weeks * 7);
        for (int w = 0; w < weeks; w++)
        {
            for (int d = 0; d < 7; d++)
            {
                var date = gridStart.AddDays(w * 7 + d);
                double seconds = 0;
                int apps = 0;
                if (date <= today && Days.TryGetValue(date, out var bucket))
                {
                    seconds = bucket.Total;
                    apps = bucket.Apps.Count;
                }
                result.Add(new DayPoint(date, seconds, apps));
            }
        }
        return result;
    }

    public double TotalForDay(DateTime day) => Days.TryGetValue(day, out var b) ? b.Total : 0.0;

    /// <summary>最近 n 天（含今天）的每日总量。</summary>
    public List<DayPoint> LastDays(int n)
    {
        var today = DateTime.Today;
        var result = new List<DayPoint>(n);
        for (int i = n - 1; i >= 0; i--)
        {
            var d = today.AddDays(-i);
            double seconds = 0;
            int apps = 0;
            if (Days.TryGetValue(d, out var b)) { seconds = b.Total; apps = b.Apps.Count; }
            result.Add(new DayPoint(d, seconds, apps));
        }
        return result;
    }

    public List<RankItem> TopForDay(DateTime day, int count)
    {
        var items = new List<RankItem>();
        if (Days.TryGetValue(day, out var bucket))
        {
            foreach (var (key, seconds) in bucket.Apps)
                items.Add(new RankItem { Name = _config.DisplayName(key), Seconds = seconds, Accent = ColorFor(key) });
        }
        items.Sort((a, b) => b.Seconds.CompareTo(a.Seconds));
        if (items.Count > count) items.RemoveRange(count, items.Count - count);
        return items;
    }

    public List<RankItem> TopOverall(int count)
    {
        var items = new List<RankItem>();
        foreach (var (key, app) in Apps)
            items.Add(new RankItem { Name = app.Name, Seconds = app.Total, Accent = ColorFor(key) });
        items.Sort((a, b) => b.Seconds.CompareTo(a.Seconds));
        if (items.Count > count) items.RemoveRange(count, items.Count - count);
        return items;
    }

    public List<(int Hour, double Seconds)> HourlyForDay(DateTime day)
    {
        var list = new List<(int, double)>(24);
        Days.TryGetValue(day, out var bucket);
        for (int h = 0; h < 24; h++)
        {
            double seconds = 0;
            bucket?.Hours.TryGetValue(h, out seconds);
            list.Add((h, seconds));
        }
        return list;
    }

    public List<AppAggregate> AppTable() =>
        Apps.Values.OrderByDescending(a => a.Total).ToList();

    /// <summary>由应用名散列出一个稳定颜色（SMT 风格的电光色系）。</summary>
    /// <summary>应用色标盘（随皮肤切换）。</summary>
    private static string[] _accentPalette =
    {
        "#00D4FF", "#2E6FE0", "#4C8DFF", "#7B5CFF", "#FF3D7F", "#FFB020",
    };

    public static void SetAccentPalette(string[] palette)
    {
        if (palette is { Length: > 0 }) _accentPalette = palette;
    }

    public static Color ColorFor(string key)
    {
        var hash = 0;
        foreach (var c in key) hash = (hash * 31 + c) % 100000;
        var hex = _accentPalette[Math.Abs(hash) % _accentPalette.Length];
        return ColorTranslator.FromHtml(hex);
    }
}
