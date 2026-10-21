using System.Runtime.InteropServices;

namespace AppUsage.Ui;

/// <summary>开机自启：往「启动」文件夹丢一个快捷方式，登录后静默进托盘。</summary>
internal static class Autostart
{
    private const string LinkName = "应用使用时长.lnk";

    public static string StartupFolder =>
        Environment.GetFolderPath(Environment.SpecialFolder.Startup);

    public static string LinkPath => Path.Combine(StartupFolder, LinkName);

    public static bool IsEnabled => File.Exists(LinkPath);

    public static string ExePath => Environment.ProcessPath ?? "AppUsage.exe";

    public static bool Enable()
    {
        try
        {
            Directory.CreateDirectory(StartupFolder);
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null) return false;

            dynamic shell = Activator.CreateInstance(type)!;
            dynamic link = shell.CreateShortcut(LinkPath);
            link.TargetPath = ExePath;
            link.Arguments = "--tray";
            link.WorkingDirectory = Path.GetDirectoryName(ExePath) ?? StartupFolder;
            link.WindowStyle = 7;            // 最小化，不弹窗
            link.Description = "应用使用时长 · 本机使用追踪";
            link.IconLocation = $"{ExePath},0";
            link.Save();
            return true;
        }
        catch (Exception)
        {
            // COM 不可用时退回写一个 .cmd
            try
            {
                var cmd = Path.Combine(StartupFolder, "AppUsageTracker.cmd");
                File.WriteAllText(cmd,
                    "@echo off\r\nstart \"\" \"" + ExePath + "\" --tray\r\n");
                return true;
            }
            catch { return false; }
        }
    }

    public static bool Disable()
    {
        try
        {
            if (File.Exists(LinkPath)) File.Delete(LinkPath);
            var cmd = Path.Combine(StartupFolder, "AppUsageTracker.cmd");
            if (File.Exists(cmd)) File.Delete(cmd);
            return true;
        }
        catch { return false; }
    }
}
