using AppUsage.Core;
using AppUsage.Ui;

namespace AppUsage;

internal static class Program
{
    /// <summary>实际使用的数据目录（崩溃日志写到这儿，便于事后找）。</summary>
    private static string _dataDir = Path.Combine(AppContext.BaseDirectory, "data");

    [STAThread]
    private static int Main(string[] args)
    {
        // 命令行用法（自测 / 导出 / 抓图）需要看得见输出：
        // 本程序是 WinExe，默认没有控制台，先挂到父进程的控制台上。
        bool cliMode = args.Any(a =>
            a.Equals("--selftest", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--memcheck", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--test-ai", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--ai-summary", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--backup", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--dump", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--test-shot", StringComparison.OrdinalIgnoreCase));
        if (cliMode) ConsoleHost.Attach();

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

        // 未捕获异常：写进 error.log，别弹系统那个丑对话框
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => LogCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash(e.ExceptionObject as Exception);

        Tracker? tracker = null;
        ShotService? shots = null;
        InputTracker? input = null;
        SingleInstance? single = null;
        try
        {
            var paths = AppPaths.Resolve(dataDir);
            var config = Config.Load(paths.ConfigPath);
            _dataDir = paths.DataDir;
            paths.MigrateLegacyLog();
            var store = new Store(paths.LogDir, config);
            store.Reload();

            // 自测：--selftest 跑一组纯逻辑回归测试后退出
            if (args.Any(a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase)))
            {
                return SelfTest.Run(Path.Combine(paths.DataDir, "selftest.txt"));
            }

            // 内存体检：--memcheck [天数] 造合成日志，量 Store 的内存占用
            int memIdx = Array.FindIndex(args, a => a.Equals("--memcheck", StringComparison.OrdinalIgnoreCase));
            if (memIdx >= 0)
            {
                int memDays = 30;
                if (memIdx + 1 < args.Length && int.TryParse(args[memIdx + 1], out var parsed))
                    memDays = parsed;
                return MemCheck.Run(memDays);
            }

            // AI 自检：--test-ai 用真实 AiClient 跑一次，验证 key / 模型 / 编码
            if (args.Any(a => a.Equals("--test-ai", StringComparison.OrdinalIgnoreCase)))
            {
                using var ai = new AiClient(config);
                // 同步等，不用 await —— Main 要是 async，await 之后的代码会跑到线程池线程上，
                // 而 WinForms 的消息循环必须在 STA 线程
                var (aiText, aiErr) = ai.TestAsync(CancellationToken.None).GetAwaiter().GetResult();
                if (aiErr is not null)
                {
                    Console.WriteLine("✗ " + aiErr);
                    return 1;
                }
                Console.WriteLine("✓ 连接正常");
                Console.WriteLine("模型回复：" + aiText);
                return 0;
            }

            // AI 总结：--ai-summary [yyyy-MM-dd] 生成某天的总结（默认今天）
            int sumIdx = Array.FindIndex(args, a => a.Equals("--ai-summary", StringComparison.OrdinalIgnoreCase));
            if (sumIdx >= 0)
            {
                var sumDay = DateTime.Today;
                if (sumIdx + 1 < args.Length && DateTime.TryParse(args[sumIdx + 1], out var parsedDay))
                    sumDay = parsedDay.Date;

                store.Reload();
                var report = ReportBuilder.Build(store, config, sumDay, DateTime.Today);
                using var ai = new AiClient(config);
                var (sumText, sumErr) = ai.SummarizeAsync(report, CancellationToken.None).GetAwaiter().GetResult();
                if (sumErr is not null)
                {
                    Console.WriteLine("✗ " + sumErr);
                    return 1;
                }
                Console.WriteLine($"===== {sumDay:yyyy-MM-dd} AI 总结 =====");
                Console.WriteLine(sumText);
                return 0;
            }

            // 调试用：--test-shot 抓一张当前前台窗口的图，验证截图链路
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
                return 0;
            }

            // 调试用：--dump <yyyy-MM-dd> 把某天的日报数据导出成文本后退出
            int dumpIdx = Array.FindIndex(args, a => a.Equals("--dump", StringComparison.OrdinalIgnoreCase));
            if (dumpIdx >= 0 && dumpIdx + 1 < args.Length)
            {
                if (!DateTime.TryParse(args[dumpIdx + 1], out var dumpDay))
                    dumpDay = DateTime.Today;
                dumpDay = dumpDay.Date;

                store.Reload();
                var report = ReportBuilder.Build(store, config, dumpDay, DateTime.Today);
                var path = Path.Combine(paths.DataDir, "report-dump.txt");
                File.WriteAllText(path, ReportDump.Format(report, store, config), new System.Text.UTF8Encoding(false));
                Console.WriteLine(path);
                return 0;
            }

            // 备份：--backup [目标目录] 把使用记录打成 ZIP
            int bakIdx = Array.FindIndex(args, a => a.Equals("--backup", StringComparison.OrdinalIgnoreCase));
            if (bakIdx >= 0)
            {
                string? bakDest = bakIdx + 1 < args.Length ? args[bakIdx + 1] : null;
                try
                {
                    var zip = DataBackup.Create(paths.DataDir, paths.ConfigPath, bakDest);
                    Console.WriteLine(zip);
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("备份失败: " + ex);
                    return 1;
                }
            }

            // 到这里才是真正的常驻运行：才允许开追踪和随机截图
            // （上面的命令行用法刻意不启动它们，免得一条调试命令就顺手截了屏）

            // 单实例守卫：两个实例同时跑会把同一段时间记两遍，数据直接被污染。
            // 第二个实例不报错退出，而是通知已有实例把主窗口显示出来
            // （本机开机自启走托盘，双击 EXE 时托盘实例一定已经在跑）
            single = new SingleInstance(paths.DataDir);
            if (!single.IsPrimary)
            {
                var notified = single.NotifyShow();
                single.Dispose();
                single = null;
                if (!notified)
                {
                    MessageBox.Show(
                        "「应用使用时长」已经在运行了。\n看右下角托盘图标，双击它可以打开主界面。",
                        "应用使用时长", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return 0;
            }
            single.Listen();

            tracker = new Tracker(paths.LogDir, config);
            tracker.Start();

            // 键盘 / 鼠标点击统计（全局低级钩子）
            if (config.Input.Enabled)
            {
                input = new InputTracker(paths.DataDir, config.Input.DistanceDpi);
                if (!input.Start())
                    Program.Trace("[input] 钩子装不上（可能被安全软件拦了），键鼠统计不可用");
            }

            // --test-shot 调试时会留下**全尺寸**截图，跟「本地不长期留全图」的原则冲突。
            // 下次正常启动就把它清掉，最多只存在一份临时全图。
            try
            {
                var staleShot = Path.Combine(paths.DataDir, "test-shot.jpg");
                if (File.Exists(staleShot)) File.Delete(staleShot);
            }
            catch { /* ignore */ }

            // 随机截图服务（独立线程，不阻塞追踪）
            shots = new ShotService(config, paths.DataDir);
            shots.Start();

            if (trayMode)
            {
                // 托盘常驻：主窗口按需创建，平时完全不构造那套界面，内存占用最低
                var host = new TrayHost(store, tracker, config, paths);
                using var pump = StartIpcPump(single, host.ShowMain);
                Application.Run(new TrayContext(host));
            }
            else
            {
                var form = new MainForm(store, tracker, config, paths);
                using var pump = StartIpcPump(single, form.ShowMain);
                Application.Run(form);
            }
        }
        catch (Exception ex)
        {
            try
            {
                var dir = _dataDir;
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
            input?.Dispose();
            single?.Dispose();
        }
        return 0;
    }

    /// <summary>
    /// 把 IPC 请求搬到 UI 线程处理。
    /// 命名管道的回调跑在后台线程上，直接动 WinForms 控件会炸。
    /// </summary>
    private static System.Windows.Forms.Timer StartIpcPump(SingleInstance single, Action showMain)
    {
        var timer = new System.Windows.Forms.Timer { Interval = 300 };
        timer.Tick += (_, _) =>
        {
            while (single.TryTakeRequest(out var cmd))
            {
                if (cmd != "show") continue;
                try { showMain(); }
                catch (Exception ex) { Program.LogCrash(ex); }
            }
        };
        timer.Start();
        return timer;
    }

    private static readonly object _logLock = new();
    private const long MaxLogBytes = 2L * 1024 * 1024;

    internal static void Trace(string message)
    {
        try
        {
            lock (_logLock)
            {
                Directory.CreateDirectory(_dataDir);
                var path = Path.Combine(_dataDir, "trace.log");
                RotateIfBig(path);
                File.AppendAllText(path,
                    $"{DateTime.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}");
            }
        }
        catch { /* ignore */ }
    }

    /// <summary>把未捕获异常写进 error.log（含堆栈），便于事后排查。</summary>
    internal static void LogCrash(Exception? ex)
    {
        if (ex is null) return;
        try
        {
            lock (_logLock)
            {
                Directory.CreateDirectory(_dataDir);
                var path = Path.Combine(_dataDir, "error.log");
                RotateIfBig(path);
                File.AppendAllText(path,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} 未捕获异常{Environment.NewLine}{ex}{Environment.NewLine}{new string('-', 60)}{Environment.NewLine}");
            }
        }
        catch { /* ignore */ }
        Trace("CRASH: " + ex.Message);
    }

    /// <summary>
    /// 日志按大小轮转：超过 2 MB 就把当前文件挪成 .1（只留一份旧的）。
    /// 这两个文件是单文件追加、**不会**自己变小，常年开着就会一直涨。
    /// </summary>
    private static void RotateIfBig(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < MaxLogBytes) return;
            var old = path + ".1";
            try { if (File.Exists(old)) File.Delete(old); } catch { }
            File.Move(path, old);
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
