namespace AppUsage.Core;

/// <summary>列出某天已存的截图缩略图，并算出对应的 GitHub 原图链接。</summary>
internal static class ShotIndex
{
    public static List<ShotRef> ForDay(string dataDir, DateTime day, Config config)
    {
        var result = new List<ShotRef>();
        var dir = Path.Combine(dataDir, "shots", "thumbs", day.ToString("yyyy-MM-dd"));
        if (!Directory.Exists(dir)) return result;

        var uploaded = ReadManifest(dataDir);

        foreach (var file in Directory.GetFiles(dir, "*.jpg").OrderBy(f => f, StringComparer.Ordinal))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var parts = name.Split('_', 2);
            var time = parts.Length > 0 ? ParseTime(parts[0]) : TimeSpan.Zero;
            var app = parts.Length > 1 ? parts[1] : "";

            result.Add(new ShotRef
            {
                ThumbPath = file,
                App = app,
                At = day.Date + time,
                // 直接用上传清单里记的路径（唯一真源），避免两处各拼一遍格式串
                GithubUrl = uploaded.TryGetValue(name, out var rel) && rel.Length > 0
                    ? BlobUrl(config, rel)
                    : string.Empty,
                Uploaded = uploaded.ContainsKey(name),
            });
        }
        return result;
    }

    /// <summary>读上传清单：本地缩略图名 → GitHub 相对路径。</summary>
    private static Dictionary<string, string> ReadManifest(string dataDir)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var path = Path.Combine(dataDir, "shots", "index.tsv");
        if (!File.Exists(path)) return map;
        try
        {
            foreach (var line in File.ReadAllLines(path))
            {
                var parts = line.Split('\t');
                if (parts.Length >= 2) map[parts[0]] = parts[1];
            }
        }
        catch { }
        return map;
    }

    public static string BlobUrl(Config config, string relPath)
    {
        var s = config.Shots;
        if (!s.HasGithub) return string.Empty;
        return $"https://github.com/{s.GithubOwner}/{s.GithubRepo}/blob/main/{relPath}";
    }

    private static TimeSpan ParseTime(string s)
    {
        if (TimeSpan.TryParseExact(s, @"hh\-mm\-ss", null, out var t)) return t;
        return TimeSpan.Zero;
    }
}
