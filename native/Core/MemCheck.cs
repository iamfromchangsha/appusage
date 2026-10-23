using System.Diagnostics;
using System.Text;

namespace AppUsage.Core;

/// <summary>
/// 内存体检：造 N 天的合成日志，量一下 Store 到底吃多少内存。
/// 用来回答「这台机器天天开着、攒一年会不会把内存撑爆」这个问题。
/// <para>
/// 用法：<c>AppUsage.exe --memcheck [天数]</c>（默认 30 天）
/// </para>
/// </summary>
internal static class MemCheck
{
    // 一天 1800 段 ≈ 10 小时活跃（每段 20 秒），40 个应用，400 个不同页面标题
    private const int SegsPerDay = 1800;
    private const int AppCount = 40;
    private const int TopicCount = 400;

    public static int Run(int days)
    {
        if (days < 1) days = 1;
        if (days > 400) days = 400;

        var dir = Path.Combine(Path.GetTempPath(), "appusage_memcheck_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var sw = Stopwatch.StartNew();
            long total = 0;
            for (int d = 0; d < days; d++)
            {
                var day = DateTime.Today.AddDays(-d);
                var path = Path.Combine(dir, day.ToString("yyyy-MM-dd") + ".log");
                var sb = new StringBuilder();
                double t = new DateTimeOffset(day.Date).ToUnixTimeMilliseconds() / 1000.0;
                for (int s = 0; s < SegsPerDay; s++)
                {
                    var app = "app" + (s % AppCount);
                    var title = "页面标题 " + (s % TopicCount) + " - Microsoft Edge";
                    sb.Append(app).Append('\t')
                      .Append("C:\\x\\").Append(app).Append(".exe\t")
                      .Append(title).Append('\t')
                      .Append((t + s * 20).ToString("F3", System.Globalization.CultureInfo.InvariantCulture)).Append('\t')
                      .Append((t + s * 20 + 20).ToString("F3", System.Globalization.CultureInfo.InvariantCulture)).Append('\t')
                      .Append("20.000").Append('\n');
                    total++;
                }
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            }
            sw.Stop();

            var logBytes = Directory.GetFiles(dir, "*.log").Sum(f => new FileInfo(f).Length);
            var logMb = logBytes / 1024.0 / 1024.0;

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var before = GC.GetTotalMemory(true);

            var store = new Store(dir, new Config());
            var readSw = Stopwatch.StartNew();
            store.Reload();
            readSw.Stop();

            var after = GC.GetTotalMemory(true);
            var usedMb = (after - before) / 1024.0 / 1024.0;

            Console.WriteLine("──── 内存体检 ────");
            Console.WriteLine($"  模拟天数      : {days} 天（{total:N0} 段）");
            Console.WriteLine($"  生成日志      : {logMb:F1} MB（耗时 {sw.Elapsed.TotalSeconds:F1} 秒）");
            Console.WriteLine($"  读入+聚合     : {readSw.Elapsed.TotalSeconds:F2} 秒（= 启动时要花的）");
            Console.WriteLine($"  聚合天数      : {store.Days.Count}");
            Console.WriteLine($"  聚合应用      : {store.Apps.Count}");
            Console.WriteLine($"  托管堆占用    : {usedMb:F1} MB");
            Console.WriteLine($"  每 MB 日志    : {(logMb > 0 ? usedMb / logMb : 0):F2} MB 内存");

            var yearLog = logMb / days * 365;
            var yearMem = usedMb / days * 365;
            Console.WriteLine($"  线性外推 365 天: 日志 {yearLog:F0} MB / 内存 {yearMem:F0} MB");
            Console.WriteLine("─────────────────");
            return 0;
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
