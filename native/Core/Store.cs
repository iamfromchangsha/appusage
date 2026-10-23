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
    private readonly string _logDir;
    private readonly Config _config;
    private readonly TopicParser _topics;
    private readonly object _sync = new();
    private readonly Dictionary<string, long> _fileOffsets = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>单次读取的最大字节数（超过则分多次读，避免一次分配过大）。</summary>
    private const int MaxChunk = 4 * 1024 * 1024;

    public readonly SortedDictionary<DateTime, DayBucket> Days = new();
    public readonly Dictionary<string, AppAggregate> Apps = new();

    public Store(string logDir, Config config)
    {
        _logDir = logDir;
        _config = config;
        _topics = new TopicParser(config);
    }

    public double TotalSeconds { get; private set; }
    public int ActiveDays { get; private set; }

    /// <summary>数据版本号：有新数据进来才 +1，UI 据此决定要不要重建。</summary>
    public int Version { get; private set; }

    /// <summary>上次算窗口指标时是哪一天（跨零点要重算一次）。</summary>
    private DateTime _windowDay = DateTime.MinValue;

    /// <summary>读取日志里新增的行并更新聚合（支持按天分片的多个文件）。</summary>
    public void Reload()
    {
        lock (_sync)
        {
            var changed = false;
            try
            {
                foreach (var file in LogFiles())
                {
                    var info = new FileInfo(file);
                    if (!info.Exists) continue;
                    var offset = _fileOffsets.GetValueOrDefault(file);
                    if (info.Length <= offset) continue;

                    // 每个文件独立处理：只吃「完整行」，文件末尾的半行留在原处等下次补齐。
                    // 早期文件若因崩溃残留半行，绝不能和后面文件的内容粘成一行，
                    // 所以这里不共用跨文件的缓冲区。
                    var budget = (int)Math.Min(info.Length - offset, MaxChunk);
                    var buf = new byte[budget];
                    int read;
                    using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        fs.Seek(offset, SeekOrigin.Begin);
                        read = fs.Read(buf, 0, budget);
                    }
                    if (read <= 0) continue;

                    int lastNl = -1;
                    for (int i = read - 1; i >= 0; i--)
                    {
                        if (buf[i] == (byte)'\n') { lastNl = i; break; }
                    }
                    if (lastNl < 0) continue;   // 全是半行，等下一次再读

                    var text = Encoding.UTF8.GetString(buf, 0, lastNl + 1);
                    _fileOffsets[file] = offset + lastNl + 1;
                    if (text.Length > 0 && text[0] == '\uFEFF') text = text[1..];   // 去 BOM

                    foreach (var raw in text.Split('\n'))
                    {
                        var line = raw.TrimEnd('\r');
                        if (line.Length == 0) continue;
                        Apply(line);
                        changed = true;
                    }
                }
            }
            catch { /* 日志正在被写，读失败就下一轮再读 */ }

            if (changed) Version++;

            // 窗口指标（今日 / 近7天 / 近30天）只在「有新数据」或「跨了零点」时重算。
            // UI 每 3 秒调一次 Reload，无条件重算等于每 3 秒把所有历史天都走一遍，
            // 攒得越久越贵。
            var today = DateTime.Today;
            if (changed || today != _windowDay)
            {
                _windowDay = today;
                RecomputeWindows();
            }
        }
    }

    /// <summary>日志文件列表（按天分片，文件名排序即时间顺序）。</summary>
    private List<string> LogFiles()
    {
        try
        {
            if (!Directory.Exists(_logDir)) return new List<string>();
            var files = new List<string>(Directory.GetFiles(_logDir, "*.log"));
            files.Sort(StringComparer.OrdinalIgnoreCase);
            return files;
        }
        catch { return new List<string>(); }
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
        var title = parts.Length > 2 ? parts[2] : string.Empty;

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
        bucket.Segs.Add(new Seg(key, start, duration));

        // 标题 → 主题 / 站点（标题解析有缓存，同一标题只算一次）
        if (title.Length > 0)
        {
            var info = _topics.Parse(title);
            if (info.Text.Length > 0)
            {
                if (!bucket.Topics.TryGetValue(info.Text, out var agg))
                {
                    agg = new TopicAgg
                    {
                        Text = info.Text,
                        Kind = info.Kind,
                        Site = info.Site,
                        SiteName = info.SiteName,
                    };
                    bucket.Topics[info.Text] = agg;
                }
                agg.Seconds += duration;
                agg.Count++;

                if (info.Site.Length > 0)
                    bucket.Sites[info.Site] = bucket.Sites.GetValueOrDefault(info.Site) + duration;
            }
        }

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

    /// <summary>
    /// 整个重算：清空聚合，从日志重新读一遍。
    /// 改了「站点关键词」这类解析规则后必须重算——
    /// 站点是**读取时**算出来的，不重算的话新规则不生效。
    /// </summary>
    public void Rebuild()
    {
        lock (_sync)
        {
            Days.Clear();
            Apps.Clear();
            TotalSeconds = 0;
            ActiveDays = 0;
            Version = 0;
            _fileOffsets.Clear();
            _windowDay = DateTime.MinValue;
            _topics.RefreshHints();
        }
        Reload();
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

    // ---------------- 日报相关查询 ----------------

    /// <summary>某天的使用段序列（按时间升序）。</summary>
    public List<Seg> SegmentsFor(DateTime day)
    {
        if (!Days.TryGetValue(day, out var b)) return new List<Seg>();
        var list = b.Segs.ToList();
        list.Sort((x, y) => x.Start.CompareTo(y.Start));
        return list;
    }

    /// <summary>某天的主题聚合（按时长降序）。</summary>
    public List<TopicAgg> TopicsFor(DateTime day)
    {
        if (!Days.TryGetValue(day, out var b)) return new List<TopicAgg>();
        return b.Topics.Values.OrderByDescending(t => t.Seconds).ToList();
    }

    /// <summary>某天的站点聚合（按时长降序）。</summary>
    public List<(string Site, string Name, double Seconds)> SitesFor(DateTime day)
    {
        if (!Days.TryGetValue(day, out var b)) return new List<(string, string, double)>();
        return b.Sites
            .Select(kv => (kv.Key, SiteDisplayName(kv.Key), kv.Value))
            .OrderByDescending(x => x.Item3)
            .ToList();
    }

    public string SiteDisplayName(string site) =>
        _config.SiteNames.TryGetValue(site, out var n) ? n
        : CategoryMap.DefaultSiteNames.TryGetValue(site, out var d) ? d
        : site;

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
