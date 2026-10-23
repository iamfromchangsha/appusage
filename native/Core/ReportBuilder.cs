using System.Drawing;

namespace AppUsage.Core;

/// <summary>把 Store 的聚合成一份日报数据。纯函数、全程离线。</summary>
internal static class ReportBuilder
{
    /// <summary>相邻两段间隔超过这个秒数，也算一次「切换」（否则 20 秒落盘会被误算成切换）。</summary>
    private const double SwitchGapSeconds = 60.0;

    /// <summary>
    /// 把「采样块」合并成「逻辑连续段」。
    /// 日志每 20 秒落盘一行，所以同一段长时间使用会被切成很多个 20 秒的块；
    /// 直接用行的 duration 算「最长连续」会永远等于 20 秒。
    /// 合并规则：同一个应用 且 与上一段的时间间隔 ≤ 60 秒。
    /// </summary>
    public static List<(string AppKey, double Start, double Duration)> MergeSegments(
        IReadOnlyList<Seg> segs, double gapLimit = SwitchGapSeconds)
    {
        var merged = new List<(string AppKey, double Start, double Duration)>();
        foreach (var s in segs)
        {
            if (merged.Count > 0)
            {
                var last = merged[^1];
                double lastEnd = last.Start + last.Duration;
                bool sameApp = string.Equals(last.AppKey, s.AppKey, StringComparison.Ordinal);
                bool contiguous = s.Start - lastEnd <= gapLimit;
                if (sameApp && contiguous)
                {
                    merged[^1] = (last.AppKey, last.Start, (s.Start + s.Duration) - last.Start);
                    continue;
                }
            }
            merged.Add((s.AppKey, s.Start, s.Duration));
        }
        return merged;
    }

    public static ReportData Build(Store store, Config config, DateTime day, DateTime today)
    {
        var data = new ReportData { Date = day };

        var bucket = store.Days.TryGetValue(day, out var b) ? b : null;
        data.TotalSeconds = bucket?.Total ?? 0.0;
        data.YesterdaySeconds = store.TotalForDay(day.AddDays(-1));

        // 周均：截至该日的最近 7 个「有记录」的自然日
        double weekSum = 0;
        int weekDays = 0;
        for (int i = 0; i < 7; i++)
        {
            var d = day.AddDays(-i);
            var t = store.TotalForDay(d);
            if (t > 0) { weekSum += t; weekDays++; }
        }
        data.WeekSampleDays = weekDays;
        data.WeekAvgSeconds = weekDays >= 2 ? weekSum / weekDays : 0;

        // 专注度：先把 20 秒采样块合并成逻辑连续段
        var segs = store.SegmentsFor(day);
        var runs = MergeSegments(segs);
        data.Sessions = runs.Count;

        // 「应用切换次数」只算**应用真的变了**的次数。
        // 「人离开了（空闲 90 秒以上）又回来用同一个应用」不是切换，是暂停——
        // 之前把这种也算进去，切换次数会虚高（实测某天多算了 17 次）。
        data.Switches = 0;
        data.Pauses = 0;
        for (int i = 1; i < runs.Count; i++)
        {
            if (string.Equals(runs[i - 1].AppKey, runs[i].AppKey, StringComparison.Ordinal))
                data.Pauses++;
            else
                data.Switches++;
        }

        double longest = 0;
        DateTime? first = null, last = null;
        foreach (var s in segs)
        {
            var start = DateTimeOffset.FromUnixTimeMilliseconds((long)(s.Start * 1000)).LocalDateTime;
            var end = start.AddSeconds(s.Duration);
            if (first is null || start < first) first = start;
            if (last is null || end > last) last = end;
        }
        foreach (var r in runs)
            if (r.Duration > longest) longest = r.Duration;
        data.LongestSessionSeconds = longest;
        data.FragmentRate = data.TotalSeconds > 0
            ? Math.Clamp(1.0 - longest / data.TotalSeconds, 0, 1)
            : 0;
        data.FirstActive = first;
        data.LastActive = last;

        // 分类构成
        var cats = CategoryMap.Merged(config);
        var catAgg = new Dictionary<string, double>(StringComparer.Ordinal);
        if (bucket is not null)
        {
            foreach (var (appKey, seconds) in bucket.Apps)
            {
                var name = CategoryMap.CategoryOf(config, cats, appKey);
                catAgg[name] = catAgg.GetValueOrDefault(name) + seconds;
            }
        }
        data.Categories = catAgg
            .OrderByDescending(kv => kv.Value)
            .Select(kv => new CategorySlice
            {
                Name = kv.Key,
                Seconds = kv.Value,
                Color = ColorTranslator.FromHtml(CategoryMap.ColorOf(kv.Key)),
            })
            .ToList();

        // TOP 应用
        if (bucket is not null)
        {
            data.TopApps = bucket.Apps
                .OrderByDescending(kv => kv.Value)
                .Take(12)
                .Select(kv => new AppSlice
                {
                    Name = config.DisplayName(kv.Key),
                    Category = CategoryMap.CategoryOf(config, cats, kv.Key),
                    Seconds = kv.Value,
                })
                .ToList();
        }

        // 今天看了什么：网页 / 文档标题（应用窗口标题归到分类里，不重复占版面）
        var topics = store.TopicsFor(day);
        var webTopics = topics.Where(t => t.Kind is TopicKind.Web or TopicKind.Document).ToList();
        if (webTopics.Count == 0) webTopics = topics.Take(8).ToList();
        data.Topics = webTopics.Take(20)
            .Select(t => new TopicSlice
            {
                Text = t.Text,
                Kind = t.Kind,
                Site = t.Site,
                SiteName = t.SiteName,
                Seconds = t.Seconds,
                Count = t.Count,
            })
            .ToList();

        // 站点
        data.Sites = store.SitesFor(day)
            .Select(x => new SiteSlice { Site = x.Site, Name = x.Name, Seconds = x.Item3 })
            .Take(12)
            .ToList();

        return data;
    }
}
