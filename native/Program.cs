using AppUsage.Core;
using AppUsage.Ui;

namespace AppUsage;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        bool trayMode = args.Any(a =>
            a.Equals("--tray", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("/tray", StringComparison.OrdinalIgnoreCase));

        string? dataDir = null;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--data", StringComparison.OrdinalIgnoreCase) ||
                args[i].Equals("/data", StringComparison.OrdinalIgnoreCase))
            {
                dataDir = args[i + 1];
            }
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Tracker? tracker = null;
        ShotUploader? uploader = null;
        ShotService? shots = null;
        try
        {
            var paths = AppPaths.Resolve(dataDir);
            var config = Config.Load(paths.ConfigPath);
            var store = new Store(paths.LogPath, config);
            store.Reload();

            tracker = new Tracker(paths.LogPath, config);
            tracker.Start();

            // 自测：--test-shot 抓一张当前前台窗口的图，验证截图链路
            if (args.Any(a => a.Equals("--test-shot", StringComparison.OrdinalIgnoreCase)))
            {
                var hwnd = NativeMethods.GetForegroundWindow();
                if (Screenshotter.TryCapture(hwnd, 1280, 60, out var jpeg))
                {
                    File.WriteAllBytes(Path.Combine(paths.DataDir, "test-shot.jpg"), jpeg);
                    Console.WriteLine($"capture ok: {jpeg.Length} bytes");
                }
                else
                {
                    Console.WriteLine("capture failed (可能是黑图或窗口不可抓)");
                }
                tracker.Stop();
                return 0;
            }

            // 随机截图服务（独立线程，不阻塞追踪）
            shots = new ShotService(config, paths.DataDir);
            shots.Start();

            // 调试用：--dump <yyyy-MM-dd> 把某天的日报数据导出成文本后退出
            int dumpIdx = Array.FindIndex(args, a => a.Equals("--dump", StringComparison.OrdinalIgnoreCase));            if (dumpIdx >= 0 && dumpIdx + 1 < args.Length)
            {
                if (!DateTime.TryParse(args[dumpIdx + 1], out var dumpDay))
                    dumpDay = DateTime.Today;
                dumpDay = dumpDay.Date;

                store.Reload();
                var report = ReportBuilder.Build(store, config, dumpDay, DateTime.Today);
                var path = Path.Combine(paths.DataDir, "report-dump.txt");
                File.WriteAllText(path, ReportDump.Format(report, store, config), new System.Text.UTF8Encoding(false));
                Console.WriteLine(path);
                tracker.Stop();
                return 0;
            }

            if (trayMode)
            {
                // 托盘常驻：主窗口按需创建，平时完全不构造那套界面，内存占用最低
                var host = new TrayHost(store, tracker, config, paths);
                Application.Run(new TrayContext(host));
            }
            else
            {
                var form = new MainForm(store, tracker, config, paths);
                Application.Run(form);
            }
        }
        catch (Exception ex)
        {
            try
            {
                var dir = dataDir ?? Path.Combine(AppContext.BaseDirectory, "data");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "error.log"),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} 启动失败\r\n{ex}\r\n\r\n");
            }
            catch { /* ignore */ }
            MessageBox.Show(ex.Message, "应用使用时长 · 启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
        finally
        {
            shots?.Dispose();
            tracker?.Dispose();
        }
        return 0;
    }

    internal static void Trace(string message)
    {
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "data");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "trace.log"),
                $"{DateTime.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}");
        }
        catch { /* ignore */ }
    }
}

/// <summary>托盘模式下的宿主：不显示主窗口，只保留消息循环。</summary>
internal sealed class TrayContext : ApplicationContext
{
    private readonly TrayHost _host;

    public TrayContext(TrayHost host)
    {
        _host = host;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _host.ExitApp();
        base.Dispose(disposing);
    }
}
