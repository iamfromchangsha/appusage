using System.Drawing;

namespace AppUsage.Core;

/// <summary>某个自然日的聚合。</summary>
internal sealed class DayBucket
{
    public double Total;
    public int AppCount;
    public readonly Dictionary<string, double> Apps = new();
    public readonly Dictionary<int, double> Hours = new();
}

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
