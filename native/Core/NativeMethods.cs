using System.Runtime.InteropServices;
using System.Text;

namespace AppUsage.Core;

/// <summary>Win32 前台窗口 / 进程 / 空闲检测。</summary>
internal static class NativeMethods
{
    private const uint ProcessQueryLimitedInformation = 0x1000;

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint flags);

    [DllImport("user32.dll")]
    public static extern bool GetWindowDC(IntPtr hWnd, out IntPtr hdc);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern bool BitBlt(IntPtr hdcDest, int x, int y, int w, int h, IntPtr hdcSrc, int x1, int y1, uint rop);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder exeName, ref uint size);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetLastInputInfo(ref LastInputInfo info);

    [DllImport("kernel32.dll")]
    public static extern ulong GetTickCount64();

    [StructLayout(LayoutKind.Sequential)]
    public struct LastInputInfo
    {
        public uint CbSize;
        public uint DwTime;
    }

    /// <summary>距离用户最后一次键鼠输入过去了多少秒。</summary>
    public static double IdleSeconds()
    {
        var info = new LastInputInfo { CbSize = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info)) return 0.0;
        return (GetTickCount64() - info.DwTime) / 1000.0;
    }

    /// <summary>取当前前台窗口的 (进程 exe 路径, 窗口标题, 进程 id)。</summary>
    public static (string Exe, string Title, uint Pid) GetForeground()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd)) return (string.Empty, string.Empty, 0);

        var len = GetWindowTextLength(hwnd);
        var title = string.Empty;
        if (len > 0)
        {
            var sb = new StringBuilder(len + 1);
            GetWindowText(hwnd, sb, sb.Capacity);
            title = sb.ToString();
        }

        GetWindowThreadProcessId(hwnd, out var pid);
        return (GetExePath(pid), title, pid);
    }

    private static readonly Dictionary<uint, string> ExeCache = new();

    private static string GetExePath(uint pid)
    {
        if (pid == 0) return string.Empty;
        lock (ExeCache)
        {
            if (ExeCache.TryGetValue(pid, out var cached)) return cached;
        }

        var path = string.Empty;
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle != IntPtr.Zero)
        {
            try
            {
                var sb = new StringBuilder(1024);
                uint size = (uint)sb.Capacity;
                if (QueryFullProcessImageName(handle, 0, sb, ref size))
                    path = sb.ToString();
            }
            catch { /* 进程可能已退出 */ }
            finally { CloseHandle(handle); }
        }

        lock (ExeCache)
        {
            if (ExeCache.Count > 512)
            {
                var stale = ExeCache.Keys.Take(256).ToArray();
                foreach (var k in stale) ExeCache.Remove(k);
            }
            ExeCache[pid] = path;
        }
        return path;
    }
}
