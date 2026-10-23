using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AppUsage.Core;

/// <summary>DeepSeek（OpenAI 兼容）调用。只发送归纳后的统计数字，不发截图 / exe 路径 / 原始标题。</summary>
internal sealed class AiClient : IDisposable
{
    private readonly Config _config;
    private readonly HttpClient _http;

    public AiClient(Config config)
    {
        _config = config;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(15, config.Ai.TimeoutSeconds)) };
    }

    public bool Configured => _config.Ai.Enabled
                              && TokenUtil.Extract(_config.Ai.ApiKey, TokenUtil.AiPattern).Length > 0
                              && _config.Ai.BaseUrl.Length > 0;

    private HttpRequestMessage BuildRequest(object body)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"{_config.Ai.BaseUrl.TrimEnd('/')}/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        // key 也可能被粘进多余内容，用之前洗一次
        var key = TokenUtil.Extract(_config.Ai.ApiKey, TokenUtil.AiPattern);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return req;
    }

    private object BuildBody(string system, string user)
    {
        var messages = new List<object>
        {
            new { role = "system", content = system },
            new { role = "user", content = user },
        };

        var body = new Dictionary<string, object?>
        {
            ["model"] = _config.Ai.Model,
            ["messages"] = messages,
            ["stream"] = false,
            ["max_tokens"] = 900,
        };
        if (_config.Ai.Thinking)
        {
            body["thinking"] = new { type = "enabled" };
            body["reasoning_effort"] = "high";
        }
        return body;
    }

    /// <summary>生成当天总结。Error 为 null 表示成功，Text 是总结正文。</summary>
    public async Task<(string? Text, string? Error)> SummarizeAsync(ReportData report, CancellationToken ct)
    {
        if (!Configured) return (null, "未配置 DeepSeek（需要在设置里填 API Key）");
        var (text, error) = await SendAsync(BuildBody(SystemPrompt, BuildUserContent(report)), ct).ConfigureAwait(false);
        return (text, error);
    }

    /// <summary>测试 key 与模型名是否可用。</summary>
    public async Task<string?> TestAsync(CancellationToken ct)
    {
        if (_config.Ai.ApiKey.Length == 0) return "还没填 API Key";
        var (_, error) = await SendAsync(BuildBody("只回复两个字：可用", "测试"), ct).ConfigureAwait(false);
        return error;
    }

    private async Task<(string? Text, string? Error)> SendAsync(object body, CancellationToken ct)
    {
        using var req = BuildRequest(body);
        try
        {
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                string detail = $"HTTP {(int)resp.StatusCode}";
                try
                {
                    using var doc = JsonDocument.Parse(text);
                    if (doc.RootElement.TryGetProperty("error", out var err) &&
                        err.TryGetProperty("message", out var em))
                        detail = em.GetString() ?? detail;
                    else if (doc.RootElement.TryGetProperty("message", out var m))
                        detail = m.GetString() ?? detail;
                }
                catch { }
                var friendly = detail switch
                {
                    var d when d.Contains("401") => "API Key 无效或已过期",
                    var d when d.Contains("402") || d.Contains("403") => "余额不足或没有权限",
                    var d when d.Contains("429") => "请求太频繁，稍后重试",
                    var d when d.Contains("model") => $"模型名不被支持（当前：{_config.Ai.Model}）",
                    _ => detail,
                };
                return (null, friendly);
            }

            try
            {
                using var doc = JsonDocument.Parse(text);
                if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                    return (null, "返回内容为空");
                var msg = choices[0].GetProperty("message");
                if (msg.TryGetProperty("content", out var content))
                {
                    var s = content.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) return (s.Trim(), null);
                }
                return (null, "返回内容为空");
            }
            catch (Exception ex) { return (null, "解析响应失败：" + ex.Message); }
        }
        catch (TaskCanceledException) { return (null, "请求超时"); }
        catch (Exception ex) { return (null, ex.Message); }
    }

    private const string SystemPrompt =
        "你是一个软件使用时长统计助手。根据用户给出的当天使用统计数据，写一段 3~5 句的中文客观总结。" +
        "要求：先给总量并与前一天对比；再指出主要的应用类别与站点；然后给一句专注度评价。" +
        "只陈述数据里有的事实，不要臆测用户的身份、心情或意图，不要给建议，不要用 Markdown 标题或列表。";

    private string BuildUserContent(ReportData r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"日期：{r.Date:yyyy-MM-dd}");
        sb.AppendLine($"当天总使用：{Fmt(r.TotalSeconds)}");
        if (r.YesterdaySeconds > 0)
        {
            var diff = r.TotalSeconds - r.YesterdaySeconds;
            sb.AppendLine($"前一天：{Fmt(r.YesterdaySeconds)}（今天{(diff >= 0 ? "多" : "少")} {Fmt(Math.Abs(diff))}）");
        }
        if (r.WeekSampleDays >= 2) sb.AppendLine($"近 7 日平均：{Fmt(r.WeekAvgSeconds)}");

        if (r.Categories.Count > 0)
        {
            double total = r.Categories.Sum(c => c.Seconds);
            sb.AppendLine("使用类别构成：" + string.Join("、",
                r.Categories.Select(c => $"{c.Name} {Fmt(c.Seconds)}（{c.Seconds / total:P0}）")));
        }
        if (r.TopApps.Count > 0)
            sb.AppendLine("用得最多的应用：" + string.Join("、",
                r.TopApps.Take(6).Select(a => $"{a.Name} {Fmt(a.Seconds)}")));
        if (r.Sites.Count > 0)
            sb.AppendLine("主要访问站点：" + string.Join("、",
                r.Sites.Take(6).Select(s => $"{s.Name} {Fmt(s.Seconds)}")));

        sb.AppendLine($"专注度：共 {r.Sessions} 段使用，切换 {r.Switches} 次，最长连续 {Fmt(r.LongestSessionSeconds)}，碎片率 {r.FragmentRate:P0}");

        if (_config.Ai.IncludeTopics && r.Topics.Count > 0)
        {
            sb.AppendLine("当天出现过的页面/文档标题（最多 8 条）：");
            foreach (var t in r.Topics.Take(8))
            {
                var title = t.Text.Length > 60 ? t.Text[..60] + "…" : t.Text;
                sb.AppendLine($"- {title}");
            }
        }
        return sb.ToString();
    }

    private static string Fmt(double seconds)
    {
        var s = (long)Math.Round(Math.Max(0, seconds));
        var h = s / 3600;
        var m = (s % 3600) / 60;
        if (h > 0) return $"{h} 小时 {m} 分";
        if (m > 0) return $"{m} 分";
        return $"{s % 60} 秒";
    }

    public void Dispose() => _http.Dispose();
}
