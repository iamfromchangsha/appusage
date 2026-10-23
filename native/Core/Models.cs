using System.Drawing;

namespace AppUsage.Core;

/// <summary>某个自然日的聚合。</summary>
internal sealed class DayBucket
{
    public double Total;
    public int AppCount => Apps.Count;
    public readonly Dictionary<string, double> Apps = new();
    public readonly Dictionary<int, double> Hours = new();

    /// <summary>清洗后的标题 → 聚合（同一标题重复出现只累加时长）。</summary>
    public readonly Dictionary<string, TopicAgg> Topics = new();

    /// <summary>站点域名 → 时长。</summary>
    public readonly Dictionary<string, double> Sites = new();

    /// <summary>当天的原始使用段（用于算切换次数 / 最长连续）。</summary>
    public readonly List<Seg> Segs = new();
}

/// <summary>一个标题（网页 / 文档 / 应用窗口）的聚合。</summary>
internal sealed class TopicAgg
{
    public string Text = string.Empty;
    public TopicKind Kind = TopicKind.Unknown;
    public string Site = string.Empty;
    public string SiteName = string.Empty;
    public double Seconds;
    public int Count;
}

/// <summary>一段连续使用。</summary>
internal readonly record struct Seg(string AppKey, double Start, double Duration);

/// <summary>单个应用的累计统计。</summary>
internal sealed class AppAggregate
{
    public string Key = string.Empty;
    public string Name = string.Empty;
    public string ExePath = string.Empty;
    public double Total;
    public int Sessions;
    public double LastSeen;
    public double Today;
    public double Week;
    public double Month;
}

/// <summary>热力图上的一个格子。</summary>
internal readonly record struct DayPoint(DateTime Date, double Seconds, int Apps);

/// <summary>排行榜的一行。</summary>
public sealed class RankItem
{
    public string Name = string.Empty;
    public double Seconds;
    public Color Accent = Color.Gray;
}
