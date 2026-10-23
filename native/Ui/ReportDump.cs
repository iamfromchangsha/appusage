using System.Text;
using AppUsage.Core;

namespace AppUsage.Ui;

/// <summary>把日报数据导出成纯文本（调试 / 核对用）。</summary>
internal static class ReportDump
{
    private static string Fmt(double seconds)
    {
        var s = (long)Math.Round(Math.Max(0, seconds));
        var h = s / 3600;
        var m = (s % 3600) / 60;
        if (h > 0) return $"{h} 小时 {m} 分";
        if (m > 0) return $"{m} 分";
        return $"{s % 60} 秒";
    }

    public static string Format(ReportData r, Store store, Config config)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"===== {r.Date:yyyy-MM-dd} 日报 =====");
        sb.AppendLine($"总时长     : {Fmt(r.TotalSeconds)}");
        sb.AppendLine($"昨日       : {Fmt(r.YesterdaySeconds)}");
        sb.AppendLine($"周均       : {(r.WeekSampleDays >= 2 ? Fmt(r.WeekAvgSeconds) : "样本不足")}（{r.WeekSampleDays} 个有记录日）");
        sb.AppendLine($"首次/末次  : {r.FirstActive:HH:mm} / {r.LastActive:HH:mm}");
        sb.AppendLine($"专注度     : {r.Sessions} 段 / {r.Switches} 次切换 / 最长连续 {Fmt(r.LongestSessionSeconds)} / 碎片率 {r.FragmentRate:P0}" +
                    (r.Pauses > 0 ? $"（另有 {r.Pauses} 段是离开后回来，不算切换）" : ""));
        sb.AppendLine();

        sb.AppendLine("--- 使用构成 ---");
        var total = r.Categories.Sum(c => c.Seconds);
        foreach (var c in r.Categories)
        {
            var pct = total > 0 ? c.Seconds / total : 0;
            sb.AppendLine($"  {c.Name,-6} {Fmt(c.Seconds),-12} {pct,6:P0}");
        }
        if (r.Categories.Count == 0) sb.AppendLine("  （无）");
        sb.AppendLine();

        sb.AppendLine("--- TOP 应用 ---");
        foreach (var a in r.TopApps) sb.AppendLine($"  {a.Name,-22} {a.Category,-4} {Fmt(a.Seconds)}");
        sb.AppendLine();

        sb.AppendLine("--- 站点 ---");
        foreach (var s in r.Sites) sb.AppendLine($"  {s.Name,-16} {s.Site,-24} {Fmt(s.Seconds)}");
        if (r.Sites.Count == 0) sb.AppendLine("  （无）");
        sb.AppendLine();

        sb.AppendLine("--- 今天看了什么 ---");
        foreach (var t in r.Topics)
        {
            var site = t.SiteName.Length > 0 ? $"[{t.SiteName}] " : "";
            sb.AppendLine($"  {site}{t.Text}    {Fmt(t.Seconds)}  ({t.Count} 次)");
        }
        if (r.Topics.Count == 0) sb.AppendLine("  （无）");
        sb.AppendLine();

        sb.AppendLine("--- 原始标题样本（核对清洗效果）---");
        var segs = store.SegmentsFor(r.Date);
        foreach (var g in segs.GroupBy(s => s.AppKey).Take(8))
        {
            sb.AppendLine($"  [{g.Key}] {g.Count()} 段");
        }
        return sb.ToString();
    }
}
