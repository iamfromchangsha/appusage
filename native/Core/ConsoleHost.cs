using System.Runtime.InteropServices;
using System.Text;

namespace AppUsage.Core;

/// <summary>
/// 让 WinExe 程序也能往控制台输出。
/// <para>
/// OutputType=WinExe 的进程默认没有控制台，<c>Console.WriteLine</c> 会直接丢掉——
/// 自测（--selftest）、--dump、--test-shot 这些命令行用法就完全看不见输出了。
/// 这里把进程挂到父进程的控制台上，再把 Console 的三个流重新接到标准句柄。
/// </para>
/// </summary>
internal static class ConsoleHost
{
    private const uint AttachParentProcess = 0xFFFFFFFF;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeConsole();

    /// <summary>挂到父进程的控制台（没有就自己开一个）。成功返回 true。</summary>
    public static bool Attach()
    {
        try
        {
            if (!AttachConsole(AttachParentProcess))
            {
                // 没有父控制台（例如直接双击运行）：自己开一个
                if (!AllocConsole()) return false;
            }

            var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
            var stderr = new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true };
            Console.SetOut(stdout);
            Console.SetError(stderr);
            try
            {
                Console.SetIn(new StreamReader(Console.OpenStandardInput(), Encoding.UTF8));
            }
            catch { /* 没有标准输入就算了 */ }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>退出前把控制台还回去（自己开的话）。</summary>
    public static void Detach()
    {
        try { FreeConsole(); } catch { /* ignore */ }
    }
}
