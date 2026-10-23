namespace AppUsage.Core;

/// <summary>
/// 站点关键词表的文本编解码。
/// <para>
/// 格式：每行一条 <c>关键词=域名[=中文显示名]</c>，<c>#</c> 开头是注释，空行忽略。
/// 例：<c>小红书=xiaohongshu.com=小红书</c>
/// </para>
/// <para>
/// 抽成静态类是为了能单测——用户手写词表，格式解析错了会静默丢数据。
/// </para>
/// </summary>
internal static class SiteHintsCodec
{
    /// <summary>解析词表文本。返回 (关键词→域名, 域名→显示名)。格式不对的行直接跳过。</summary>
    public static (Dictionary<string, string> Hints, Dictionary<string, string> Names) Parse(IEnumerable<string> lines)
    {
        var hints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;

            var parts = line.Split('=');
            if (parts.Length < 2) continue;

            var keyword = parts[0].Trim();
            var domain = parts[1].Trim().ToLowerInvariant();
            if (keyword.Length == 0 || domain.Length == 0) continue;
            // 域名不合法就别收，省得写出一堆「未识别」的垃圾站点
            if (!domain.Contains('.') || domain.Any(char.IsWhiteSpace)) continue;

            hints[keyword] = domain;

            if (parts.Length >= 3)
            {
                var display = parts[2].Trim();
                if (display.Length > 0) names[domain] = display;
            }
        }
        return (hints, names);
    }

    /// <summary>把词表序列化回文本行（供界面显示和编辑）。</summary>
    public static List<string> Format(Dictionary<string, string> hints, Dictionary<string, string> names)
    {
        var lines = new List<string>();
        if (hints is null) return lines;
        foreach (var (keyword, domain) in hints.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            var line = $"{keyword}={domain}";
            if (names is not null && names.TryGetValue(domain, out var display) && !string.IsNullOrWhiteSpace(display))
                line += "=" + display.Trim();
            lines.Add(line);
        }
        return lines;
    }
}
