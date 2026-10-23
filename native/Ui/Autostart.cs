using System.Runtime.InteropServices;

namespace AppUsage.Ui;

/// <summary>开机自启：往「启动」文件夹丢一个快捷方式，登录后静默进托盘。</summary>
internal static class Autostart
{
    private const string LinkName = "应用使用时长.lnk";
    private const string FallbackCmdName = "AppUsageTracker.cmd";

    public static string StartupFolder =>
        Environment.GetFolderPath(Environment.SpecialFolder.Startup);

    public static string LinkPath => Path.Combine(StartupFolder, LinkName);

    public static string FallbackCmdPath => Path.Combine(StartupFolder, FallbackCmdName);

    public static string ExePath => Environment.ProcessPath ?? "AppUsage.exe";

    /// <summary>
    /// 开机自启是否真的有效。
    /// <para>
    /// **光看 .lnk 存不存在是不够的**：程序被挪了位置 / 改名了，快捷方式还在，
    /// 但开机时根本拉不起来——界面却还显示「开机自启 ✓」，属于典型的静默失效。
    /// 所以这里要把快捷方式的目标读出来核对。
    /// </para>
    /// </summary>
    public static bool IsEnabled
    {
        get
        {
            if (File.Exists(LinkPath))
            {
                var target = ReadTarget(LinkPath);
                // 读不出来（COM 不可用）时保守认为有效，不给用户误报
                if (target.Length == 0) return true;
                return File.Exists(target);
            }
            return File.Exists(FallbackCmdPath);
        }
    }

    /// <summary>自启没生效的原因（给界面提示用）；没问题就返回空串。</summary>
    public static string Problem()
    {
        if (IsEnabled) return string.Empty;
        if (!File.Exists(LinkPath) && !File.Exists(FallbackCmdPath))
            return "启动文件夹里没有快捷方式";
        return "快捷方式指向的程序不存在（可能挪过位置），重新开启一次即可";
    }

    private static string ReadTarget(string lnkPath)
    {
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null) return string.Empty;
            dynamic shell = Activator.CreateInstance(type)!;
            dynamic link = shell.CreateShortcut(lnkPath);
            return (string?)link.TargetPath ?? string.Empty;
        }
        catch { return string.Empty; }
    }

    public static bool Enable()
    {
        try
        {
            Directory.CreateDirectory(StartupFolder);
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null) return FallbackEnable();

            dynamic shell = Activator.CreateInstance(type)!;
            dynamic link = shell.CreateShortcut(LinkPath);
            link.TargetPath = ExePath;
            link.Arguments = "--tray";
            link.WorkingDirectory = Path.GetDirectoryName(ExePath) ?? StartupFolder;
            link.WindowStyle = 7;            // 最小化，不弹窗
            link.Description = "应用使用时长 · 本机使用追踪";
            link.IconLocation = $"{ExePath},0";
            link.Save();

            // 之前走过 COM 不可用的兜底路径，.cmd 还在 → 登录时会起两个实例
            try { if (File.Exists(FallbackCmdPath)) File.Delete(FallbackCmdPath); } catch { }
            return true;
        }
        catch (Exception)
        {
            return FallbackEnable();
        }
    }

    /// <summary>COM 不可用时的兜底：写一个 .cmd。</summary>
    private static bool FallbackEnable()
    {
        try
        {
            Directory.CreateDirectory(StartupFolder);
            File.WriteAllText(FallbackCmdPath,
                "@echo off\r\nstart \"\" \"" + ExePath + "\" --tray\r\n");
            // 两条路别同时留着
            try { if (File.Exists(LinkPath)) File.Delete(LinkPath); } catch { }
            return true;
        }
        catch { return false; }
    }

    public static bool Disable()
    {
        try
        {
            if (File.Exists(LinkPath)) File.Delete(LinkPath);
            if (File.Exists(FallbackCmdPath)) File.Delete(FallbackCmdPath);
            return true;
        }
        catch { return false; }
    }
}
