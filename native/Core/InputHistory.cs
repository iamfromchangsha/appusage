using System.Globalization;

namespace AppUsage.Core;

/// <summary>
/// 键鼠统计的历史读取（热力图、统计卡片用）。
/// <para>
/// 数据是「一天一个 JSON」，放在 <c>data\input\</c> 下。
/// 为了不让主界面每 20 秒刷新一次就把几百个文件重读一遍，
/// 这里按文件修改时间做缓存 —— 实际只有「今天」那个文件会变。
/// </para>
/// </summary>
internal sealed class InputHistory
{
    /// <summary>文件路径 → (最后修改时间, 该天的统计)。</summary>
    private readonly Dictionary<string, (DateTime Write, InputDayStat Stat)> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>读取全部键鼠统计，按日期索引。文件读不出来就当那天没数据。</summary>
    public Dictionary<DateTime, InputDayStat> Load(string dataDir)
    {
        var result = new Dictionary<DateTime, InputDayStat>();
        var dir = InputTracker.DirFor(dataDir);
        if (!Directory.Exists(dir)) return result;

        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (!DateTime.TryParseExact(name, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                        DateTimeStyles.None, out var day))
                continue;

            DateTime write;
            try { write = File.GetLastWriteTimeUtc(file); }
            catch { continue; }

            if (_cache.TryGetValue(file, out var hit) && hit.Write == write)
            {
                result[day] = hit.Stat;
                continue;
            }

            var stat = InputTracker.Load(dataDir, day);
            _cache[file] = (write, stat);
            result[day] = stat;
        }
        return result;
    }

    /// <summary>某天的「输入次数」：按键 + 点击。这是热力图键鼠模式下的着色依据。</summary>
    public static int InputCount(InputDayStat stat) =>
        stat.TotalKeys + stat.Left + stat.Middle + stat.Right + stat.Side;
}
