namespace AppUsage.Core;

/// <summary>应用分类：把 exe 名映射到「编程 / 办公 / 沟通 / 娱乐 / 浏览 / 学习 / 其他」。</summary>
internal static class CategoryMap
{
    public const string Other = "其他";

    /// <summary>内置默认分类（用户 config.json 里的同名 key 会覆盖）。</summary>
    public static readonly Dictionary<string, string> DefaultCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        // 编程 / 开发
        ["code"] = "编程",
        ["devenv"] = "编程",
        ["rider"] = "编程",
        ["pycharm"] = "编程",
        ["idea"] = "编程",
        ["idea64"] = "编程",
        ["eclipse"] = "编程",
        ["androidstudio"] = "编程",
        ["unity"] = "编程",
        ["unrealeditor"] = "编程",
        ["cmake"] = "编程",
        ["git"] = "编程",
        ["node"] = "编程",
        ["python"] = "编程",
        ["pythonw"] = "编程",
        ["java"] = "编程",
        ["javaw"] = "编程",
        ["gcc"] = "编程",
        ["go"] = "编程",
        ["cargo"] = "编程",
        ["dotnet"] = "编程",
        ["msbuild"] = "编程",
        ["cl"] = "编程",
        ["devenv"] = "编程",
        ["wechatdevtools"] = "编程",
        ["minimaxcode"] = "编程",
        ["claudebiu"] = "编程",

        // 办公 / 文档
        ["winword"] = "办公",
        ["excel"] = "办公",
        ["powerpnt"] = "办公",
        ["outlook"] = "办公",
        ["onenote"] = "办公",
        ["onenoteim"] = "办公",
        ["notepad"] = "办公",
        ["notepad++"] = "办公",
        ["typora"] = "办公",
        ["wps"] = "办公",
        ["wpp"] = "办公",
        ["et"] = "办公",
        ["wpspdf"] = "办公",
        ["acrobat"] = "办公",
        ["foxitreader"] = "办公",

        // 沟通
        ["wechat"] = "沟通",
        ["weixin"] = "沟通",
        ["wechatapp"] = "沟通",
        ["qq"] = "沟通",
        ["tim"] = "沟通",
        ["dingtalk"] = "沟通",
        ["feishu"] = "沟通",
        ["lark"] = "沟通",
        ["slack"] = "沟通",
        ["discord"] = "沟通",
        ["telegram"] = "沟通",
        ["zoom"] = "沟通",
        ["teams"] = "沟通",

        // 娱乐
        ["neteasemusic"] = "娱乐",
        ["qqmusic"] = "娱乐",
        ["kugou"] = "娱乐",
        ["ximalaya"] = "娱乐",
        ["douyin"] = "娱乐",
        ["tiktok"] = "娱乐",
        ["bilibili"] = "娱乐",
        ["bilibiliapp"] = "娱乐",
        ["steam"] = "娱乐",
        ["steamwebhelper"] = "娱乐",
        ["kaka"] = "娱乐",
        ["potplayer"] = "娱乐",
        ["vlc"] = "娱乐",
        ["spotify"] = "娱乐",
        ["neteasecloudmusic"] = "娱乐",
        ["qqlive"] = "娱乐",
        ["iqiyi"] = "娱乐",
        ["youku"] = "娱乐",

        // 浏览
        ["chrome"] = "浏览",
        ["msedge"] = "浏览",
        ["firefox"] = "浏览",
        ["360chrome"] = "浏览",
        ["360se"] = "浏览",
        ["quark"] = "浏览",
        ["browser"] = "浏览",
        ["maxthon"] = "浏览",
        ["opera"] = "浏览",
        ["brave"] = "浏览",
        ["arc"] = "浏览",
        ["safari"] = "浏览",

        // 学习
        ["fenbi"] = "学习",
        ["bilibili_learn"] = "学习",
        ["youdaoworkspace"] = "学习",
        ["youdao"] = "学习",
        ["xmind"] = "学习",
        ["mendeley"] = "学习",
        ["zotero"] = "学习",
        ["endnote"] = "学习",
    };

    /// <summary>内置默认站点关键词表（标题关键词 → 站点域名）。</summary>
    public static readonly Dictionary<string, string> DefaultSiteHints = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bilibili"] = "bilibili.com",
        ["哔哩哔哩"] = "bilibili.com",
        ["知乎"] = "zhihu.com",
        ["zhihu"] = "zhihu.com",
        ["掘金"] = "juejin.cn",
        ["juejin"] = "juejin.cn",
        ["csdn"] = "csdn.net",
        ["github"] = "github.com",
        ["youtube"] = "youtube.com",
        ["douyin"] = "douyin.com",
        ["抖音"] = "douyin.com",
        ["微博"] = "weibo.com",
        ["weibo"] = "weibo.com",
        ["淘宝"] = "taobao.com",
        ["taobao"] = "taobao.com",
        ["京东"] = "jd.com",
        ["百度"] = "baidu.com",
        ["baidu"] = "baidu.com",
        ["粉笔"] = "fenbi.com",
        ["fenbi"] = "fenbi.com",
        // 粉笔系的特征词：标题里通常没有「粉笔」两个字，靠这张表兜住
        ["专项智能练习"] = "fenbi.com",
        ["单题批改"] = "fenbi.com",
        ["考生管理系统"] = "fenbi.com",
        ["模块检测"] = "fenbi.com",
        ["行测"] = "fenbi.com",
        ["申论"] = "fenbi.com",
        ["面试"] = "fenbi.com",
    };

    /// <summary>内置的站点域名 → 中文显示名。</summary>
    public static readonly Dictionary<string, string> DefaultSiteNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["fenbi.com"] = "粉笔",
        ["bilibili.com"] = "哔哩哔哩",
        ["zhihu.com"] = "知乎",
        ["juejin.cn"] = "掘金",
        ["csdn.net"] = "CSDN",
        ["github.com"] = "GitHub",
        ["youtube.com"] = "YouTube",
        ["douyin.com"] = "抖音",
        ["weibo.com"] = "微博",
        ["taobao.com"] = "淘宝",
        ["jd.com"] = "京东",
        ["baidu.com"] = "百度",
    };

    /// <summary>合并后的分类表：用户配置优先。</summary>
    public static Dictionary<string, string> Merged(Config? config)
    {
        var merged = new Dictionary<string, string>(DefaultCategories, StringComparer.OrdinalIgnoreCase);
        if (config?.AppCategories is null) return merged;
        foreach (var (k, v) in config.AppCategories)
        {
            if (!string.IsNullOrWhiteSpace(v)) merged[k] = v;
        }
        return merged;
    }

    public static string CategoryOf(Config config, Dictionary<string, string> merged, string appKey)
    {
        if (merged.TryGetValue(appKey, out var c) && !string.IsNullOrWhiteSpace(c)) return c;
        return Other;
    }

    /// <summary>报告里用的分类配色（按分类名固定）。</summary>
    public static string ColorOf(string category) => category switch
    {
        "编程" => "#4C8DFF",
        "办公" => "#00D4FF",
        "沟通" => "#FF3D7F",
        "娱乐" => "#FFB020",
        "浏览" => "#7B5CFF",
        "学习" => "#2FE39A",
        _ => "#8296C8",
    };
}
