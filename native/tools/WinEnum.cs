using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public class WinEnum
{
    private delegate bool EnumProc(IntPtr h, IntPtr l);

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);

    private static uint _target;
    private static List<string> _found = new List<string>();

    private static bool Callback(IntPtr h, IntPtr l)
    {
        uint pid;
        GetWindowThreadProcessId(h, out pid);
        if (pid == _target)
        {
            var t = new StringBuilder(256); GetWindowText(h, t, 256);
            var c = new StringBuilder(256); GetClassName(h, c, 256);
            _found.Add(string.Format("hwnd={0} vis={1} cls={2} title={3}",
                h, IsWindowVisible(h), c, t));
        }
        return true;
    }

    public static List<string> ForPid(int pid)
    {
        _target = (uint)pid;
        _found = new List<string>();
        EnumWindows(Callback, IntPtr.Zero);
        return _found;
    }

    private static IntPtr _childRoot;
    private static string _childNeedle;
    private static List<string> _childFound = new List<string>();

    private static bool ChildCallback(IntPtr h, IntPtr l)
    {
        var t = new StringBuilder(256); GetWindowText(h, t, 256);
        if (t.Length > 0 && t.ToString().IndexOf(_childNeedle, StringComparison.Ordinal) >= 0)
        {
            var c = new StringBuilder(256); GetClassName(h, c, 256);
            var r = new RECT(); GetWindowRect(h, out r);
            _childFound.Add(string.Format("hwnd={0} cls={1} text={2} rect={3},{4},{5},{6}",
                h, c, t, r.Left, r.Top, r.Right, r.Bottom));
        }
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);

    /// <summary>在指定窗口的子控件里找标题包含 needle 的那个。</summary>
    public static List<string> FindChild(IntPtr root, string needle)
    {
        _childRoot = root;
        _childNeedle = needle;
        _childFound = new List<string>();
        EnumChildWindows(root, ChildCallback, IntPtr.Zero);
        return _childFound;
    }
}
