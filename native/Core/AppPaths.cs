namespace AppUsage.Core;

/// <summary>程序用到的路径。</summary>
internal sealed class AppPaths
{
    public string BaseDir = string.Empty;
    public string DataDir = string.Empty;
    public string ConfigPath = string.Empty;
    public string LogPath = string.Empty;

    public static AppPaths Resolve(string? dataDirOverride)
    {
        var baseDir = AppContext.BaseDirectory;
        var dataDir = dataDirOverride is { Length: > 0 }
            ? Path.GetFullPath(dataDirOverride)
            : Path.Combine(baseDir, "data");

        Directory.CreateDirectory(dataDir);

        // 配置文件优先级：exe 同级 → 上一级 → 仓库里的示例文件 → 数据目录
        // （config.json 含 API Key / PAT，已被 .gitignore 排除，不要提交）
        var candidates = new List<string>
        {
            Path.Combine(baseDir, "config.json"),
            Path.Combine(dataDir, "config.json"),
        };
        var parent = Path.GetDirectoryName(baseDir.TrimEnd(Path.DirectorySeparatorChar));
        if (!string.IsNullOrEmpty(parent))
        {
            candidates.Insert(1, Path.Combine(parent, "config.json"));
            candidates.Add(Path.Combine(parent, "config.example.json"));
        }

        var configPath = candidates.FirstOrDefault(File.Exists) ?? Path.Combine(dataDir, "config.json");

        return new AppPaths
        {
            BaseDir = baseDir,
            DataDir = dataDir,
            ConfigPath = configPath,
            LogPath = Path.Combine(dataDir, "usage.log"),
        };
    }
}
