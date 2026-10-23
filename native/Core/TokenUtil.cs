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

    /// <summary>从一段文本里提取 token；提取不到就退回「去首尾空白/引号 + 只取第一行」。</summary>
    public static string Extract(string? raw, string pattern)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var m = Regex.Match(raw, pattern);
        if (m.Success) return m.Value;

        var text = raw.Trim().Trim('"', '\'', ' ');
        var nl = text.IndexOfAny(new[] { '\r', '\n' });
        if (nl >= 0) text = text[..nl].Trim();
        return text;
    }
}
