using System.IO.Compression;
using System.Text;

namespace AppUsage.Core;

/// <summary>
/// 备份使用数据。
/// <para>
/// 使用记录是这个程序最值钱的东西，却只躺在应用目录的 <c>data\logs\</c> 里 ——
/// 换应用、删文件夹、磁盘出点事，几年的记录就没了。
/// 这里把日志打成一个 ZIP 放到桌面，方便用户自己存到网盘 / 移动硬盘。
/// </para>
/// <para>
/// **配置里的密钥会被抹掉**（DeepSeek API Key、GitHub PAT）——
/// 备份这种东西很容易被转发出去，密钥不该跟着走。
/// </para>
/// </summary>
internal static class DataBackup
{
    /// <summary>生成备份 ZIP，返回文件路径；失败抛异常（由调用方提示）。</summary>
    /// <param name="dataDir">数据目录（logs 在它下面）。</param>
    /// <param name="configPath">配置文件路径（可能不在 dataDir 里，调用方最清楚）。</param>
    /// <param name="destDir">ZIP 放哪儿，默认桌面。</param>
    public static string Create(string dataDir, string? configPath = null, string? destDir = null)
    {
        destDir ??= Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        if (string.IsNullOrWhiteSpace(destDir)) destDir = dataDir;
        Directory.CreateDirectory(destDir);

        var stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmm");
        var zipPath = Path.Combine(destDir, $"appusage-备份_{stamp}.zip");
        // 同名了就加个序号，别把上一次的覆盖掉
        for (int i = 2; File.Exists(zipPath); i++)
            zipPath = Path.Combine(destDir, $"appusage-备份_{stamp}_{i}.zip");

        using (var fs = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            // 1) 原始使用日志（一天一个文件）
            var logDir = Path.Combine(dataDir, "logs");
            if (Directory.Exists(logDir))
            {
                foreach (var f in Directory.GetFiles(logDir, "*.log"))
                {
                    try
                    {
                        var entry = zip.CreateEntry($"logs/{Path.GetFileName(f)}", CompressionLevel.Optimal);
                        // ⚠️ 必须 FileShare.ReadWrite：追踪器正开着当天这个文件在写，
                        // 用 File.OpenRead（只共享读）会直接抛「另一个程序正在使用此文件」，
                        // 整个备份就废了。这里追日志的读法（Store.Reload）保持一致。
                        using var src = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        using var dst = entry.Open();
                        src.CopyTo(dst);
                    }
                    catch (IOException)
                    {
                        // 读不到就跳过这个文件，别让整个备份泡汤
                        Program.Trace($"[backup] 跳过读不了的文件: {Path.GetFileName(f)}");
                    }
                }
            }

            // 2) 配置（密钥抹掉）。配置不一定在 dataDir 里，所以由调用方给路径。
            if (!string.IsNullOrWhiteSpace(configPath) && File.Exists(configPath))
            {
                var cfg = Config.Load(Path.GetFullPath(configPath));
                cfg.Ai.ApiKey = string.Empty;
                cfg.Shots.GithubToken = string.Empty;
                var entry = zip.CreateEntry("config.json", CompressionLevel.Optimal);
                using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                w.Write(System.Text.Json.JsonSerializer.Serialize(cfg, new System.Text.Json.JsonSerializerOptions
                { WriteIndented = true }));
            }

            // 3) 说明
            var readme = zip.CreateEntry("README.txt", CompressionLevel.Optimal);
            using var rw = new StreamWriter(readme.Open(), new UTF8Encoding(false));
            rw.Write(ReadmeText());
        }
        return zipPath;
    }

    private static string ReadmeText() =>
        "应用使用时长 · 数据备份\n" +
        "========================\n\n" +
        $"导出时间：{DateTime.Now:yyyy-MM-dd HH:mm}\n\n" +
        "logs/        原始使用记录（纯文本，一天一个文件，制表符分隔）\n" +
        "config.json  界面与统计配置\n\n" +
        "恢复方法：把 logs 目录里的 .log 文件放回应用的 data\\logs\\ 目录即可。\n\n" +
        "注意：config.json 里的 DeepSeek API Key 和 GitHub PAT **已经被抹掉**，\n" +
        "      恢复后需要自己在「设置」里重新填（这是故意的：备份容易被转发出去，\n" +
        "      密钥不该跟着走）。\n\n" +
        "随机截图不在这里 —— 它们本来就传到了你自己的 GitHub 私有仓库。\n";
}
