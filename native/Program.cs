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
        try
        {
            var paths = AppPaths.Resolve(dataDir);
            var config = Config.Load(paths.ConfigPath);
            var store = new Store(paths.LogPath, config);
            store.Reload();

            tracker = new Tracker(paths.LogPath, config);
            tracker.Start();

            if (trayMode)
            {
                // 托盘常驻：主窗口按需创建，平时完全不构造那套界面，内存占用最低
                var host = new TrayHost(store, tracker, config, paths.DataDir);
                Application.Run(new TrayContext(host));
            }
            else
            {
                var form = new MainForm(store, tracker, config, paths.DataDir);
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
