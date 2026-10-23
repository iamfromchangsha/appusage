using System.Text.RegularExpressions;

namespace AppUsage.Core;

/// <summary>标题属于哪一类。</summary>
internal enum TopicKind
{
    /// <summary>普通桌面应用窗口（QQ、微信、MiniMax Code…）。</summary>
    App,

    /// <summary>浏览器里的网页。</summary>
    Web,

    /// <summary>文档 / 表格（Word、Excel…）。</summary>
    Document,

    /// <summary>认不出来。</summary>
    Unknown,
}

/// <summary>解析结果：清洗后的标题 + 类型 + 推断出的站点。</summary>
internal readonly record struct TopicInfo(string Text, TopicKind Kind, string Site, string SiteName);

/// <summary>
/// 窗口标题解析：把浏览器的「页面标题 和另外 N 个页面 - 个人 2 - Microsoft Edge」
/// 这类脏标题还原成**活动标签页的真实标题**，再靠关键词表推断站点。
/// 纯字符串处理，零开销、不做任何 UI 自动化。
/// </summary>
internal sealed class TopicParser
{
    // 不可见 / 零宽字符
    private static readonly Regex Invisible = new(@"[\u200B-\u200D\u2060\uFEFF\u00A0\u200E\u200F]", RegexOptions.Compiled);
    private static readonly Regex EdgeTabs = new(@"^(.*?)\s*和另外\s*\d+\s*个页面", RegexOptions.Compiled);
    private static readonly Regex ProfilePersonal = new(@"\s*-\s*个人\s*\d+\s*$", RegexOptions.Compiled);
    private static readonly Regex ProfileNamed = new(@"\s*-\s*(?:默认|Default|Profile\s*\d+)\s*$", RegexOptions.Compiled);
    private static readonly Regex OfficeCompat = new(@"\s*-\s*兼容性模式\s*-\s*(Word|Excel|PowerPoint)\s*$", RegexOptions.Compiled);
    private static readonly Regex DocExt = new(@"\.(docx?|xlsx?|pptx?)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly (string Name, Regex Pattern)[] Browsers =
    {
        ("Microsoft Edge", new(@"\s*-\s*Microsoft\s*Edge\s*$", RegexOptions.Compiled)),
        ("Google Chrome", new(@"\s*-\s*Google\s*Chrome\s*$", RegexOptions.Compiled)),
        ("Mozilla Firefox", new(@"\s*-\s*Mozilla\s*Firefox\s*$", RegexOptions.Compiled)),
        ("Chromium", new(@"\s*-\s*Chromium\s*$", RegexOptions.Compiled)),
    };

    private readonly Config _config;
    private readonly Dictionary<string, TopicInfo> _cache = new(StringComparer.Ordinal);
    private List<KeyValuePair<string, string>> _siteHints;
    public TopicParser(Config config)
    {
        _config = config;
        _siteHints = BuildHints(config);
    }

    private static List<KeyValuePair<string, string>> BuildHints(Config config) =>
        CategoryMap.DefaultSiteHints
            .Concat(config.SiteHints ?? new Dictionary<string, string>())
            .GroupBy(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            .Select(g => new KeyValuePair<string, string>(g.Key, g.First()))
            .OrderByDescending(kv => kv.Key.Length)
            .ToList();

    /// <summary>
    /// 词表改了之后要重新构建。
    /// 站点是在**读取日志时**算出来的，不重建的话新加的词条不会生效。
    /// </summary>
    public void RefreshHints()
    {
        _siteHints = BuildHints(_config);
        _cache.Clear();
    }

    /// <summary>解析一个原始窗口标题（带缓存，同一标题只算一次）。</summary>
    public TopicInfo Parse(string rawTitle)
    {
        if (string.IsNullOrWhiteSpace(rawTitle))
            return new TopicInfo(string.Empty, TopicKind.Unknown, string.Empty, string.Empty);

        if (_cache.TryGetValue(rawTitle, out var cached)) return cached;

        var info = Compute(rawTitle);
        if (_cache.Count > 4096) _cache.Clear();
        _cache[rawTitle] = info;
        return info;
    }

    private TopicInfo Compute(string raw)
    {
        var s = Clean(raw);
        if (s.Length == 0) return new TopicInfo(string.Empty, TopicKind.Unknown, string.Empty, string.Empty);

        var kind = TopicKind.App;
        var isBrowser = false;

        // Edge 多标签：「页面 和另外 N 个页面 - 个人 2 - Microsoft Edge」
        var m = EdgeTabs.Match(s);
        if (m.Success)
        {
            s = m.Groups[1].Value.Trim();
            isBrowser = true;
        }

        // Office 兼容性模式
        var oc = OfficeCompat.Match(s);
        if (oc.Success)
        {
            s = s[..oc.Index].Trim();
            kind = TopicKind.Document;
        }

        // 浏览器名后缀
        foreach (var (name, pattern) in Browsers)
        {
            if (!pattern.IsMatch(s)) continue;
            s = pattern.Replace(s, string.Empty).Trim();
            isBrowser = true;
            break;
        }

        // 剥尾部之前留两份原文：站点名常常就在被剥掉的那段里
        // （Edge 的「页面标题 - 站点名」「xxx - 搜索」「xxx - 知乎」）
        var siteSourceFull = s;

        // 浏览器配置文件名
        if (isBrowser)
        {
            s = ProfilePersonal.Replace(s, string.Empty).Trim();
            s = ProfileNamed.Replace(s, string.Empty).Trim();
            var siteSourceAfterProfile = s;
            // 剩下的「 - xxx」按配置文件处理（Chrome 的个人资料名可能是任意文字）
            var dash = s.LastIndexOf(" - ", StringComparison.Ordinal);
            if (dash > 0 && s.Length - dash <= 26) s = s[..dash].Trim();

            // 站点识别：先用**剥之前**的标题，剥干净的当兜底。
            // 反过来做的话，「xxx - 知乎」会先被剥成「xxx」，站点就再也认不出来了。
            var site = SiteOf(siteSourceFull);
            if (site.Length == 0) site = SiteOf(siteSourceAfterProfile);
            if (site.Length == 0) site = SiteOf(s);
            var siteName = site.Length > 0 ? SiteNameOf(site) : string.Empty;
            return new TopicInfo(s, TopicKind.Web, site, siteName);
        }

        if (kind != TopicKind.Document && DocExt.IsMatch(s)) kind = TopicKind.Document;
        if (kind == TopicKind.App && s.Length == 0) kind = TopicKind.Unknown;
        return new TopicInfo(s, kind, string.Empty, string.Empty);
    }

    /// <summary>去零宽 / 不可见字符、折叠空白。</summary>
    public static string Clean(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var s = Invisible.Replace(value, " ");
        s = Regex.Replace(s, @"\s+", " ");
        return s.Trim();
    }

    /// <summary>用关键词表从页面标题推断站点域名（只记域名，不记完整 URL）。</summary>
    public string SiteOf(string pageTitle)
    {
        if (string.IsNullOrEmpty(pageTitle)) return string.Empty;
        foreach (var (keyword, site) in _siteHints)
        {
            if (pageTitle.Contains(keyword, StringComparison.OrdinalIgnoreCase)) return site;
        }
        return string.Empty;
    }

    private string SiteNameOf(string site)
    {
        if (_config.SiteNames.TryGetValue(site, out var n) && !string.IsNullOrWhiteSpace(n)) return n;
        if (CategoryMap.DefaultSiteNames.TryGetValue(site, out var d) && !string.IsNullOrWhiteSpace(d)) return d;
        return site;
    }
}
