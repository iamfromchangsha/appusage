using System.Text.RegularExpressions;

namespace AppUsage.Core;

/// <summary>
/// 凭据清洗：从粘贴内容里只取出 token 本身。
/// 防的是「先粘了 PAT，又把另一个 key 粘进同一个框」→ 拼成一长串 → 鉴权 401。
/// </summary>
internal static class TokenUtil
{
    /// <summary>GitHub PAT：fine-grained 是 github_pat_xxx，classic 是 ghp_/gho_/ghu_/ghs_。</summary>
    public const string GitHubPattern = @"github_pat_[A-Za-z0-9_]+|gh[pousr]_[A-Za-z0-9]+";

    /// <summary>DeepSeek / OpenAI 兼容的 key：sk- 开头的一串。</summary>
    public const string AiPattern = @"sk-[A-Za-z0-9_\-]{16,}";

    /// <summary>
    /// 从一段文本里提取 token。
    /// 关键：**按已知格式的固定长度截取**，而不是靠正则贪婪匹配——
    /// 否则「PAT 后面又粘了另一个 key」时会把多出来的字符一起吞掉（实测踩过：401）。
    /// </summary>
    public static string Extract(string? raw, string pattern)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var text = raw.Trim();

        // 1) GitHub fine-grained PAT：github_pat_ + 82 字符 = 93 字符（长度固定）
        var i = text.IndexOf("github_pat_", StringComparison.Ordinal);
        if (i >= 0 && text.Length - i >= 93) return text.Substring(i, 93);

        // 2) GitHub classic PAT：ghp_ + 36 字符 = 40 字符（长度固定）
        var classic = Regex.Match(text, @"gh[pousr]_[A-Za-z0-9]{36}");
        if (classic.Success) return classic.Value;

        // 3) DeepSeek / OpenAI 兼容 key：sk- + 32 字符
        var sk = Regex.Match(text, @"sk-[A-Za-z0-9]{32}");
        if (sk.Success) return sk.Value;

        // 4) 兜底：用调用方给的宽松模式
        var loose = Regex.Match(text, pattern);
        if (loose.Success) return loose.Value;

        var line = text.Split('\n')[0].Trim().Trim('"', '\'', ' ', '\r');
        return line;
    }
}
