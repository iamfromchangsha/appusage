using System.Drawing;

namespace AppUsage.Core;

/// <summary>一天的使用构成。</summary>
internal sealed class CategorySlice
{
    public string Name = string.Empty;
    public double Seconds;
    public Color Color = Color.Gray;
}

/// <summary>一个应用的当日用量。</summary>
internal sealed class AppSlice
{
    public string Name = string.Empty;
    public string Category = CategoryMap.Other;
    public double Seconds;
}

/// <summary>一个主题（网页 / 文档标题）。</summary>
internal sealed class TopicSlice
{
    public string Text = string.Empty;
    public TopicKind Kind = TopicKind.Unknown;
    public string Site = string.Empty;
    public string SiteName = string.Empty;
    public double Seconds;
    public int Count;
}

/// <summary>一个站点。</summary>
internal sealed class SiteSlice
{
    public string Site = string.Empty;
    public string Name = string.Empty;
    public double Seconds;
}

/// <summary>一张截图（本地缩略图 + GitHub 原图链接）。</summary>
internal sealed class ShotRef
{
    public string ThumbPath = string.Empty;
    public string GithubUrl = string.Empty;
    public string App = string.Empty;
    public DateTime At;
    public bool Uploaded;
}

/// <summary>一天日报的全部数据（纯离线算出）。</summary>
internal sealed class ReportData
{
    public DateTime Date;
    public double TotalSeconds;
    public double YesterdaySeconds;
    public double WeekAvgSeconds;
    public int WeekSampleDays;

    public int Sessions;
    public int Switches;

    /// <summary>「人离开了又回来用同一个应用」的次数——这不是切换，是暂停。</summary>
    public int Pauses;

    public double LongestSessionSeconds;
    public double FragmentRate;          // 0~1

    public DateTime? FirstActive;
    public DateTime? LastActive;

    public List<CategorySlice> Categories = new();
    public List<AppSlice> TopApps = new();
    public List<TopicSlice> Topics = new();
    public List<SiteSlice> Sites = new();
    public List<ShotRef> Shots = new();

    /// <summary>当天的键盘 / 鼠标统计。</summary>
    public InputDayStat Input { get; set; } = new();
}
