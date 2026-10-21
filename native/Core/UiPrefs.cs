using System.Text.Json;
using System.Text.Json.Serialization;

namespace AppUsage.Core;

/// <summary>界面偏好（目前只有主题），存到数据目录下的 ui.json。</summary>
internal sealed class UiPrefs
{
    [JsonPropertyName("dark_theme")] public bool DarkTheme { get; set; } = false;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string PathIn(string dataDir) => System.IO.Path.Combine(dataDir, "ui.json");

    public static UiPrefs Load(string dataDir)
    {
        try
        {
            var path = PathIn(dataDir);
            if (File.Exists(path))
            {
                var prefs = JsonSerializer.Deserialize<UiPrefs>(File.ReadAllText(path), Options);
                if (prefs is not null) return prefs;
            }
        }
        catch { /* 读不到就用默认 */ }
        return new UiPrefs();
    }

    public void Save(string dataDir)
    {
        try
        {
            Directory.CreateDirectory(dataDir);
            File.WriteAllText(PathIn(dataDir), JsonSerializer.Serialize(this, Options));
        }
        catch { /* 存不了就算了 */ }
    }
}
