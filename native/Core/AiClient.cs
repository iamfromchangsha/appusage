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

    /// <summary>
    /// 接口地址。用户很可能只填 "api.deepseek.com" 而忘了写协议，
    /// 那样拼出来的 URL 不是绝对地址，会直接抛异常。缺协议就补 https。
    /// </summary>
    private string Endpoint
    {
        get
        {
            var b = _config.Ai.BaseUrl.Trim().TrimEnd('/');
            if (b.Length == 0) return b;
            if (!b.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !b.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                b = "https://" + b;
            return b;
        }
    }

    private HttpRequestMessage BuildRequest(object body)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"{Endpoint}/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        // key 也可能被粘进多余内容，用之前洗一次
        var key = TokenUtil.Extract(_config.Ai.ApiKey, TokenUtil.AiPattern);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return req;
    }

    private object BuildBody(string system, string user, int maxTokens)
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
            ["max_tokens"] = maxTokens,
        };
        if (_config.Ai.Thinking)
        {
            body["thinking"] = new { type = "enabled" };
            body["reasoning_effort"] = "high";
        }
        return body;
    }

    /// <summary>
    /// 每次请求的 token 预算。
    /// <para>
    /// ⚠️ **开思考模式时推理 token 也算进 max_tokens**。原来固定 900，
    /// 结果推理就把预算吃光，模型来不及输出正文——
    /// 实测 <c>finish_reason=length</c>、正文长度 0，AI 总结**一个字都出不来**。
    /// </para>
    /// </summary>
    private static int MaxTokensFor(bool thinking) => thinking ? 8000 : 1200;

    /// <summary>生成当天总结。Error 为 null 表示成功，Text 是总结正文。</summary>
    public async Task<(string? Text, string? Error)> SummarizeAsync(ReportData report, CancellationToken ct)
    {
        if (!Configured) return (null, "未配置 DeepSeek（需要在设置里填 API Key）");

        var system = SystemPrompt;
        var user = BuildUserContent(report);
        var budget = MaxTokensFor(_config.Ai.Thinking);

        var (text, error) = await SendAsync(BuildBody(system, user, budget), ct).ConfigureAwait(false);
        if (error is not null && error.Contains("截断"))
        {
            // 预算还是不够，自动加倍再试一次
            var (t2, e2) = await SendAsync(BuildBody(system, user, budget * 2), ct).ConfigureAwait(false);
            if (e2 is null) return (t2, null);
        }
        return (text, error);
    }

    /// <summary>测试 key 与模型名是否可用。返回 (模型的回复, 错误)，错误为 null 表示成功。</summary>
    public async Task<(string? Text, string? Error)> TestAsync(CancellationToken ct)
    {
        if (TokenUtil.Extract(_config.Ai.ApiKey, TokenUtil.AiPattern).Length == 0)
            return (null, "还没填 API Key");
        if (_config.Ai.BaseUrl.Trim().Length == 0)
            return (null, "还没填接口地址");
        // 注意：这里不查 Ai.Enabled —— 用户可能想先测通了再打开开关
        return await SendAsync(BuildBody("只回复两个字：可用", "测试", MaxTokensFor(_config.Ai.Thinking)), ct)
            .ConfigureAwait(false);
    }

    private async Task<(string? Text, string? Error)> SendAsync(object body, CancellationToken ct)
    {
        using var req = BuildRequest(body);
        try
        {
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var text = await ReadBodyAsync(resp, ct).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                var status = (int)resp.StatusCode;
                string serverMsg = string.Empty;
                try
                {
                    using var doc = JsonDocument.Parse(text);
                    if (doc.RootElement.TryGetProperty("error", out var err) &&
                        err.TryGetProperty("message", out var em))
                        serverMsg = em.GetString() ?? string.Empty;
                    else if (doc.RootElement.TryGetProperty("message", out var m))
                        serverMsg = m.GetString() ?? string.Empty;
                }
                catch { }

                // 友好提示必须按 **状态码** 判断。
                // 之前这里把状态码覆盖成了服务端正文，那套中文提示几乎永远匹配不上，
                // 用户只会看到一串英文原始报错。
                var friendly = status switch
                {
                    401 => "API Key 无效或已过期",
                    402 or 403 => "余额不足或没有权限",
                    429 => "请求太频繁，稍后重试",
                    400 when serverMsg.Contains("model", StringComparison.OrdinalIgnoreCase)
                        => $"模型名不被支持（当前：{_config.Ai.Model}）",
                    _ => serverMsg.Length > 0 ? $"HTTP {status} · {serverMsg}" : $"HTTP {status}",
                };
                return (null, friendly);
            }

            try
            {
                return ParseChatResponse(text);
            }
            catch (Exception ex) { return (null, "解析响应失败：" + ex.Message); }
        }
        catch (TaskCanceledException) { return (null, "请求超时"); }
        catch (Exception ex) { return (null, ex.Message); }
    }

    /// <summary>
    /// 解析 chat/completions 的响应。抽成静态方法是为了能单测。
    /// <para>
    /// 关键是要分清「正文为空」和「正文被 token 预算截断」：
    /// 两者在响应里长得几乎一样（content 都是空串），
    /// 但前者无解、后者加大预算就能救，所以报错文案必须不一样。
    /// </para>
    /// </summary>
    internal static (string? Text, string? Error) ParseChatResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            return (null, "返回内容为空");
        var choice = choices[0];
        var msg = choice.GetProperty("message");

        // finish_reason=length 表示 max_tokens 用完了、正文被截断
        var truncated = choice.TryGetProperty("finish_reason", out var fr) &&
                        string.Equals(fr.GetString(), "length", StringComparison.OrdinalIgnoreCase);
        var hasReasoning = msg.TryGetProperty("reasoning_content", out var rc) &&
                           !string.IsNullOrWhiteSpace(rc.GetString());

        if (msg.TryGetProperty("content", out var content))
        {
            var s = content.GetString();
            if (!string.IsNullOrWhiteSpace(s)) return (s.Trim(), null);
        }

        if (truncated)
            return (null, hasReasoning
                ? "回答被 token 预算截断了（思考模式把预算用完了），正文没输出"
                : "回答被 token 预算截断了");
        if (hasReasoning)
            return (null, "模型只返回了思考过程、没有正文");
        return (null, "返回内容为空");
    }

    /// <summary>
    /// 读响应正文，**按 UTF-8 显式解码**。
    /// <para>
    /// DeepSeek 回的 Content-Type 只有 "application/json"，**不带 charset**。
    /// JSON 的默认编码按 RFC 8259 就是 UTF-8，所以这里自己解，不赌运行时的默认值。
    /// </para>
    /// </summary>
    private static async Task<string> ReadBodyAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        return DecodeBody(bytes, resp.Content.Headers.ContentType?.CharSet);
    }

    /// <summary>按 charset（可空）解码响应字节；没给 charset 就按 UTF-8。</summary>
    internal static string DecodeBody(byte[] bytes, string? charset)
    {
        if (!string.IsNullOrEmpty(charset))
        {
            try { return Encoding.GetEncoding(charset.Trim('"')).GetString(bytes); }
            catch { /* 编码名不认识就按 UTF-8 */ }
        }
        return Encoding.UTF8.GetString(bytes);
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

        sb.AppendLine($"专注度：共 {r.Sessions} 段使用，切换 {r.Switches} 次，最长连续 {Fmt(r.LongestSessionSeconds)}，碎片率 {r.FragmentRate:P0}" +
                    (r.Pauses > 0 ? $"（其中 {r.Pauses} 段是中途离开后又回到同一个应用，不算切换）" : ""));

        // 键鼠统计也一起给（用户选的「一起发」）：
        // 只发**汇总数字**和最常用的几个键名，不发完整的按键序列。
        if (r.Input.TotalKeys > 0 || r.Input.Left + r.Input.Right > 0)
        {
            var combo = r.Input.Keys.Count(kv => kv.Key.Contains('+'));
            var top = r.Input.Keys.OrderByDescending(kv => kv.Value).Take(5);
            sb.AppendLine($"输入活动：按键 {r.Input.TotalKeys} 次（其中组合键 {combo} 次），" +
                          $"最常用的是 {string.Join("、", top.Select(kv => $"{kv.Key} {kv.Value} 次"))}；" +
                          $"鼠标移动 {r.Input.DistanceCm / 100:F1} 米，点击 {r.Input.Left + r.Input.Middle + r.Input.Right + r.Input.Side} 次" +
                          $"（左 {r.Input.Left} / 右 {r.Input.Right} / 中 {r.Input.Middle}）");
        }

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
