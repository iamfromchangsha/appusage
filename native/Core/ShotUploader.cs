using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AppUsage.Core;

/// <summary>GitHub 私有仓库的截图存档（REST API，纯手写，不引 NuGet）。</summary>
internal sealed class ShotUploader : IDisposable
{
    private readonly Config _config;
    private readonly HttpClient _http;

    public ShotUploader(Config config)
    {
        _config = config;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public bool Configured => _config.Shots.HasGithub;

    public string Owner => _config.Shots.GithubOwner;
    public string Repo => _config.Shots.GithubRepo;

    private void Prepare(HttpRequestMessage req)
    {
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.Shots.GithubToken);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        req.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        req.Headers.UserAgent.ParseAdd("AppUsage");
    }

    private async Task<string?> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        try
        {
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode) return null;

            string detail = $"HTTP {(int)resp.StatusCode}";
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("message", out var m))
                    detail += " · " + m.GetString();
            }
            catch { }
            return detail;
        }
        catch (TaskCanceledException) { return "请求超时"; }
        catch (Exception ex) { return ex.Message; }
    }

    /// <summary>确保私有仓库存在；返回 null 表示成功。</summary>
    public async Task<string?> EnsureRepoAsync(CancellationToken ct)
    {
        if (!Configured) return "未配置 GitHub token / owner / repo";

        // 先看仓库在不在
        var check = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Owner}/{Repo}");
        Prepare(check);
        using (check)
        {
            using var resp = await _http.SendAsync(check, ct).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode) return null;
            if ((int)resp.StatusCode != 404) return $"仓库检查失败：HTTP {(int)resp.StatusCode}";
        }

        if (!_config.Shots.AutoCreateRepo) return $"仓库 {Owner}/{Repo} 不存在，且未开启自动创建";

        var createBody = new Dictionary<string, object>
        {
            ["name"] = Repo,
            ["private"] = true,      // private 是关键字，这里用字典避开
            ["auto_init"] = true,
            ["description"] = "AppUsage 随机使用截图存档（自动上传，仅本人可见）",
        };
        var create = new HttpRequestMessage(HttpMethod.Post, "https://api.github.com/user/repos")
        {
            Content = JsonContent(createBody),
        };
        Prepare(create);
        using (create)
            return await SendAsync(create, ct).ConfigureAwait(false);
    }

    /// <summary>上传一张图（内容 base64）。返回 null 表示成功。</summary>
    public async Task<string?> UploadAsync(string relPath, byte[] content, string message, CancellationToken ct)
    {
        if (!Configured) return "未配置 GitHub";
        var req = new HttpRequestMessage(HttpMethod.Put, $"https://api.github.com/repos/{Owner}/{Repo}/contents/{relPath}")
        {
            Content = JsonContent(new
            {
                message,
                content = Convert.ToBase64String(content),
                branch = "main",
            }),
        };
        Prepare(req);
        using (req)
            return await SendAsync(req, ct).ConfigureAwait(false);
    }

    /// <summary>从私有仓库取一张图（用于本地缩略图丢失时补拉）。</summary>
    public async Task<byte[]?> DownloadAsync(string relPath, CancellationToken ct)
    {
        if (!Configured) return null;
        var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Owner}/{Repo}/contents/{relPath}");
        Prepare(req);
        try
        {
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("content", out var c)) return null;
            var b64 = c.GetString()?.Replace("\n", string.Empty);
            if (string.IsNullOrEmpty(b64)) return null;
            return Convert.FromBase64String(b64);
        }
        catch { return null; }
    }

    public void Dispose() => _http.Dispose();

    private static StringContent JsonContent(object value) =>
        new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");
}
