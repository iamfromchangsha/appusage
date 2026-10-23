namespace AppUsage.Core;

/// <summary>列出某天已存的截图缩略图，并算出对应的 GitHub 原图链接。</summary>
internal static class ShotIndex
{
    public static List<ShotRef> ForDay(string dataDir, DateTime day, Config config)
    {
        var result = new List<ShotRef>();
        var dir = Path.Combine(dataDir, "shots", "thumbs", day.ToString("yyyy-MM-dd"));
        if (!Directory.Exists(dir)) return result;

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
                GithubUrl = BlobUrl(config, day, name),
            });
        }
        return result;
    }

    public static string BlobUrl(Config config, DateTime day, string name)
    {
        var s = config.Shots;
        if (!s.HasGithub) return string.Empty;
        return $"https://github.com/{s.GithubOwner}/{s.GithubRepo}/blob/main/shots/{day:yyyy}/{day:MM}/{day:DD}/{name}.jpg";
    }

    public static string RelPath(DateTime day, string name) =>
        $"shots/{day:yyyy}/{day:MM}/{day:DD}/{name}.jpg";

    private static TimeSpan ParseTime(string s)
    {
        if (TimeSpan.TryParseExact(s, @"hh\-mm\-ss", null, out var t)) return t;
        return TimeSpan.Zero;
    }
}
