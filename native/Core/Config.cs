using System.Text.Json;
using System.Text.Json.Serialization;

namespace AppUsage.Core;

/// <summary>读取与 Python 版共用的 config.json。</summary>
internal sealed class Config
{
    [JsonPropertyName("poll_interval")] public double PollInterval { get; set; } = 1.0;
    [JsonPropertyName("idle_seconds")] public int IdleSeconds { get; set; } = 90;
    [JsonPropertyName("min_span_seconds")] public double MinSpanSeconds { get; set; } = 2.0;
    [JsonPropertyName("flush_interval")] public double FlushInterval { get; set; } = 20.0;
    [JsonPropertyName("excluded_exes")] public List<string> ExcludedExes { get; set; } = new();
    [JsonPropertyName("app_names")] public Dictionary<string, string> AppNames { get; set; } = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static Config Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var cfg = JsonSerializer.Deserialize<Config>(json, Options);
                if (cfg is not null) return cfg;
            }
        }
        catch { /* 配置坏了不该让程序起不来 */ }
        return new Config();
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
