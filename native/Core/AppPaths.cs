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

        var configPath = Path.Combine(baseDir, "config.json");
        if (!File.Exists(configPath))
        {
            var parent = Path.GetDirectoryName(baseDir.TrimEnd(Path.DirectorySeparatorChar));
            if (!string.IsNullOrEmpty(parent))
            {
                var alt = Path.Combine(parent, "config.json");
                if (File.Exists(alt)) configPath = alt;
            }
        }
        if (!File.Exists(configPath))
        {
            configPath = Path.Combine(dataDir, "config.json");
        }

        return new AppPaths
        {
            BaseDir = baseDir,
            DataDir = dataDir,
            ConfigPath = configPath,
            LogPath = Path.Combine(dataDir, "usage.log"),
        };
    }
}
