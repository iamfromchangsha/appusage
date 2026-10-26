using System.Text.Json;
using System.Text.Json.Serialization;

namespace AppUsage.Core;

/// <summary>DeepSeek / OpenAI 兼容端点的配置。</summary>
internal sealed class AiConfig
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("base_url")] public string BaseUrl { get; set; } = "https://api.deepseek.com";
    [JsonPropertyName("api_key")] public string ApiKey { get; set; } = string.Empty;
    /// <summary>当前可用：deepseek-v4-pro（最新）/ deepseek-v4-flash（快）。旧的 chat/reasoner 已弃用。</summary>
    [JsonPropertyName("model")] public string Model { get; set; } = "deepseek-v4-pro";
    [JsonPropertyName("thinking")] public bool Thinking { get; set; } = true;
    [JsonPropertyName("include_topics")] public bool IncludeTopics { get; set; } = true;
    [JsonPropertyName("timeout_seconds")] public int TimeoutSeconds { get; set; } = 45;
}

/// <summary>随机截图 + 私有 GitHub 仓库存档的配置。</summary>
internal sealed class ShotsConfig
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("interval_minutes")] public int IntervalMinutes { get; set; } = 20;
    [JsonPropertyName("max_per_day")] public int MaxPerDay { get; set; } = 8;

    [JsonPropertyName("github_token")] public string GithubToken { get; set; } = string.Empty;
    [JsonPropertyName("github_owner")] public string GithubOwner { get; set; } = string.Empty;
    [JsonPropertyName("github_repo")] public string GithubRepo { get; set; } = "appusage-shots";
    [JsonPropertyName("auto_create_repo")] public bool AutoCreateRepo { get; set; } = true;
    /// <summary>不截图的应用（隐私）。</summary>
    [JsonPropertyName("excluded")] public List<string> Excluded { get; set; } = new();

    [JsonPropertyName("full_width")] public int FullWidth { get; set; } = 1280;
    [JsonPropertyName("thumb_width")] public int ThumbWidth { get; set; } = 320;
    [JsonPropertyName("jpeg_quality")] public int JpegQuality { get; set; } = 60;

    /// <summary>GitHub 凭据是否配齐了（计算属性，不写进配置文件）。</summary>
    [JsonIgnore]
    public bool HasGithub =>
        GithubToken.Length > 0 && GithubOwner.Length > 0 && GithubRepo.Length > 0;
}

/// <summary>键盘 / 鼠标点击统计的配置。</summary>
internal sealed class InputConfig
{
    /// <summary>
    /// 是否统计按键与鼠标点击。
    /// 开着就意味着系统里挂了全局低级钩子（能看见你按了哪些键，
    /// 但**不记输入内容**）。默认开着，想关在设置里改。
    /// </summary>
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;

    /// <summary>
    /// 鼠标距离换算用的 DPI。
    /// <para>
    /// 默认 96 = 按**逻辑像素**换算，和老工具（键盘鼠标点击次数统计工具V1.0）的
    /// 历史数据口径一致 —— 不然迁过来的老数据和新记的数据会差 1.5 倍
    /// （实测老工具就是按 96 算的，本机 150% 缩放下两边的比值是 0.68 ≈ 96/144）。
    /// </para>
    /// <para>
    /// 改成 144（本机的真实缩放）就得到**物理上真实**的厘米数，
    /// 但那样就和历史数据不是一个口径了。
    /// </para>
    /// </summary>
    [JsonPropertyName("distance_dpi")] public int DistanceDpi { get; set; } = 96;
}

/// <summary>读取与保存 config.json。</summary>
internal sealed class Config
{
    [JsonPropertyName("poll_interval")] public double PollInterval { get; set; } = 1.0;
    [JsonPropertyName("idle_seconds")] public int IdleSeconds { get; set; } = 90;
    [JsonPropertyName("min_span_seconds")] public double MinSpanSeconds { get; set; } = 2.0;
    [JsonPropertyName("flush_interval")] public double FlushInterval { get; set; } = 20.0;
    [JsonPropertyName("excluded_exes")] public List<string> ExcludedExes { get; set; } = new();
    [JsonPropertyName("app_names")] public Dictionary<string, string> AppNames { get; set; } = new();

    [JsonPropertyName("app_categories")] public Dictionary<string, string> AppCategories { get; set; } = new();
    [JsonPropertyName("site_hints")] public Dictionary<string, string> SiteHints { get; set; } = new();
    [JsonPropertyName("site_names")] public Dictionary<string, string> SiteNames { get; set; } = new();
    [JsonPropertyName("ai")] public AiConfig Ai { get; set; } = new();
    [JsonPropertyName("shots")] public ShotsConfig Shots { get; set; } = new();
    [JsonPropertyName("input")] public InputConfig Input { get; set; } = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    public static Config Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new Config();

            var cfg = TryRead(path);
            if (cfg is not null) return cfg;

            // 主配置读不出来（写坏了 / 被别的程序改坏了）→ 试上一份备份。
            // 配置里存着 API Key 和 GitHub PAT，静默退回默认值等于把用户的设置全弄丢。
            var bak = path + ".bak";
            var backup = TryRead(bak);
            if (backup is not null)
            {
                Program.Trace($"config.json 解析失败，已从备份 {bak} 恢复");
                RestoreFrom(path, bak);
                return backup;
            }
            Program.Trace("config.json 解析失败，改用默认配置启动（原文件保留，没有覆盖）");
        }
        catch { /* 配置坏了不该让程序起不来 */ }
        return new Config();
    }

    /// <summary>
    /// 把备份内容原子写回主文件。
    /// 备份本身要保住（不然下一次保存会拿坏文件把好备份覆盖掉），
    /// 坏掉的主文件留成 .corrupt 备查。
    /// </summary>
    private static void RestoreFrom(string path, string bakPath)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var tmp = full + ".restore";
            File.WriteAllText(tmp, File.ReadAllText(bakPath));
            if (File.Exists(full)) File.Replace(tmp, full, full + ".corrupt");
            else File.Move(tmp, full);
        }
        catch { /* 恢复不了就算了，至少数据还在 .bak 里 */ }
    }

    private static Config? TryRead(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Config>(json, Options);
        }
        catch { return null; }
    }

    public void Save(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(this, Options);

            // **原子写**：先写临时文件，再整体替换。
            // 直接 File.WriteAllText 的话，写到一半崩了 / 断电就会留下半截 JSON，
            // 下次启动配置读不出来 → API Key / PAT / 站点词表全丢，而且悄无声息。
            // File.Replace 是原子的，顺带把上一版存成 .bak。
            var tmp = full + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(full))
                File.Replace(tmp, full, full + ".bak");
            else
                File.Move(tmp, full);
        }
        catch { /* 存不了就算了 */ }
    }

    /// <summary>内置的友好显示名，用户配置可以覆盖。</summary>
    public static readonly Dictionary<string, string> DefaultNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chrome"] = "Chrome",
        ["msedge"] = "Microsoft Edge",
        ["firefox"] = "Firefox",
        ["code"] = "VS Code",
        ["explorer"] = "文件资源管理器",
        ["wechat"] = "微信",
        ["weixin"] = "微信",
        ["wechatapp"] = "微信",
        ["qq"] = "QQ",
        ["qqmusic"] = "QQ音乐",
        ["neteasemusic"] = "网易云音乐",
        ["steam"] = "Steam",
        ["discord"] = "Discord",
        ["terminal"] = "终端",
        ["windowsterminal"] = "Windows 终端",
        ["powershell"] = "PowerShell",
        ["cmd"] = "命令提示符",
        ["obs64"] = "OBS Studio",
        ["douyin"] = "抖音",
        ["tiktok"] = "TikTok",
        ["notion"] = "Notion",
        ["slack"] = "Slack",
        ["photoshop"] = "Photoshop",
        ["premiere"] = "Premiere Pro",
        ["blender"] = "Blender",
        ["unity"] = "Unity",
        ["zhihu"] = "知乎",
        ["bilibili"] = "哔哩哔哩",
        ["dingtalk"] = "钉钉",
        ["figma"] = "Figma",
        ["claude"] = "Claude",
        ["chatgpt"] = "ChatGPT",
        ["minimaxcode"] = "MiniMax Code",
        ["winword"] = "Word",
        ["excel"] = "Excel",
        ["uwp"] = "UWP 应用",
    };

    public string DisplayName(string key) =>
        AppNames.TryGetValue(key, out var custom) && !string.IsNullOrWhiteSpace(custom)
            ? custom
            : DefaultNames.TryGetValue(key, out var known) ? known : key;

    /// <summary>把 (exe 路径, 窗口标题) 归一化成稳定的应用标识。</summary>
    public string AppKey(string exePath, string title)
    {
        var baseName = Path.GetFileName(exePath).ToLowerInvariant();
        if (baseName.EndsWith(".exe", StringComparison.Ordinal)) baseName = baseName[..^4];
        if (baseName.Length == 0)
        {
            var head = title.Split(" - ")[0].Trim().ToLowerInvariant();
            return head.Length > 0 ? head[..Math.Min(40, head.Length)] : "unknown";
        }
        if (baseName is "applicationframehost" or "runtimebroker")
        {
            var head = title.Trim();
            if (head.Length > 0 && head.Length <= 40 && !head.Contains(" - ")) return head.ToLowerInvariant();
            return "uwp";
        }
        return baseName;
    }
}
