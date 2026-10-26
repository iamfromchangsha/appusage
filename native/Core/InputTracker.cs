using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace AppUsage.Core;

/// <summary>某一天的键鼠统计。</summary>
internal sealed class InputDayStat
{
    /// <summary>
    /// 按键名 → 次数（按键名是**物理按键**，不记输入内容）。
    /// 注意：要用属性而不是字段 —— System.Text.Json 默认只序列化属性。
    /// </summary>
    public Dictionary<string, int> Keys { get; set; } = new(StringComparer.Ordinal);

    /// <summary>鼠标移动距离（厘米）。</summary>
    public double DistanceCm { get; set; }

    public int Left { get; set; }
    public int Middle { get; set; }
    public int Right { get; set; }
    public int Side { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public int TotalKeys => Keys.Values.Sum();

    public static readonly InputDayStat Empty = new();
}

/// <summary>
/// 键盘 / 鼠标点击统计。
/// <para>
/// 用的是 <b>WH_KEYBOARD_LL / WH_MOUSE_LL</b> 全局低级钩子：只看**按了哪个键**，
/// 不看输入了什么内容，也不看窗口标题 —— 和截图那套「不记内容」的隐私原则一致。
/// </para>
/// <para>
/// 每天一个 JSON 文件（<c>data/input/YYYY-MM-DD.json</c>），
/// 内存里累加、定期落盘，跨零点自动切文件。
/// </para>
/// </summary>
internal sealed class InputTracker : IDisposable
{
    private readonly string _dataDir;
    private readonly int _distanceDpi;
    private readonly object _sync = new();

    private NativeMethods.LowLevelHookProc? _kbProc;
    private NativeMethods.LowLevelHookProc? _mouseProc;
    private IntPtr _kbHook = IntPtr.Zero;
    private IntPtr _mouseHook = IntPtr.Zero;

    private DateTime _day = DateTime.Today;
    private readonly Dictionary<string, int> _keys = new(StringComparer.Ordinal);
    private double _distanceCm;
    private int _left, _middle, _right, _side;

    /// <summary>最近一次落盘时间（秒）。</summary>
    private DateTime _lastSave = DateTime.MinValue;

    /// <summary>组合键前缀：按住不松的修饰键。</summary>
    private readonly HashSet<int> _modifiersDown = new();

    public InputTracker(string dataDir, int distanceDpi = 96)
    {
        _dataDir = dataDir;
        _distanceDpi = distanceDpi > 0 ? distanceDpi : 96;
    }

    public bool IsRunning => _kbHook != IntPtr.Zero || _mouseHook != IntPtr.Zero;

    /// <summary>装钩子。返回 false 表示装不上（调用方要提示用户，不能静默）。</summary>
    public bool Start()
    {
        if (IsRunning) return true;
        try
        {
            // 先接上今天已有的（必须在装钩子之前，否则这几秒的输入会被覆盖掉）
            LoadToday();

            // 委托必须保持引用，否则会被 GC 回收 → 钩子回调进不去，直接崩
            _kbProc = KeyboardHookProc;
            _mouseProc = MouseHookProc;

            // 低级钩子要 hMod 指向包含回调的模块。
            // 单文件发布下 GetModuleHandle(null) 可能拿不到东西，
            // 所以按「进程 exe 路径 → 当前模块 → 0」的顺序都试一遍。
            var mod = ResolveModule();
            _kbHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _kbProc, mod, 0);
            _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseProc, mod, 0);

            if (_kbHook == IntPtr.Zero && _mouseHook == IntPtr.Zero)
            {
                Stop();
                return false;
            }
            Program.Trace($"[input] 钩子已装: 键盘={_kbHook != IntPtr.Zero} 鼠标={_mouseHook != IntPtr.Zero}");
            _timer = new System.Threading.Timer(_ => Tick(), null, 10_000, 10_000);
            return true;
        }
        catch (Exception ex)
        {
            Program.Trace("[input] 装钩子失败: " + ex.Message);
            Stop();
            return false;
        }
    }

    private System.Threading.Timer? _timer;

    /// <summary>
    /// 接着今天已经记的继续记。
    /// 不接上的话，**每次重启应用都会把当天的计数清零覆盖**——
    /// 上午记了几千次按键，中午重启一下全没了。
    /// </summary>
    private void LoadToday()
    {
        try
        {
            var stat = Load(_dataDir, DateTime.Today);
            lock (_sync)
            {
                _day = DateTime.Today;
                _keys.Clear();
                foreach (var (k, v) in stat.Keys) _keys[k] = v;
                _distanceCm = stat.DistanceCm;
                _left = stat.Left;
                _middle = stat.Middle;
                _right = stat.Right;
                _side = stat.Side;
            }
            Program.Trace($"[input] 接上今天已有的: 按键 {stat.TotalKeys} 次, 移动 {stat.DistanceCm:F0} cm");
        }
        catch (Exception ex)
        {
            Program.Trace("[input] 读取今天已有统计失败: " + ex.Message);
        }
    }

    public void Stop()
    {
        try { _timer?.Dispose(); } catch { }
        _timer = null;
        if (_kbHook != IntPtr.Zero) { NativeMethods.UnhookWindowsHookEx(_kbHook); _kbHook = IntPtr.Zero; }
        if (_mouseHook != IntPtr.Zero) { NativeMethods.UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
        _kbProc = null;
        _mouseProc = null;
        Save();
    }

    private static IntPtr ResolveModule()
    {
        try
        {
            // 低级钩子的 hMod 会被系统忽略（回调函数就在本进程里），
            // 但还是给个真实模块句柄更稳妥。
            // GetModuleHandle(null) 取的是当前进程映像基址 —— 单文件发布下也有效。
            // 注意：不能用 Marshal.GetHINSTANCE(Module)，单文件里它返回 -1（IL3002）。
            var h = NativeMethods.GetModuleHandle(null);
            if (h != IntPtr.Zero) return h;
        }
        catch { }
        return IntPtr.Zero;
    }

    // ---------------- 钩子回调 ----------------

    private IntPtr KeyboardHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0)
            {
                var msg = (int)wParam;
                if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
                {
                    var info = Marshal.PtrToStructure<NativeMethods.KbdLlHookStruct>(lParam);
                    OnKeyDown((int)info.VkCode, info.ScanCode);
                }
                else if (msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP)
                {
                    var info = Marshal.PtrToStructure<NativeMethods.KbdLlHookStruct>(lParam);
                    OnKeyUp((int)info.VkCode);
                }
            }
        }
        catch { /* 钩子里绝不能抛 */ }
        return NativeMethods.CallNextHookEx(_kbHook, nCode, wParam, lParam);
    }

    private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0)
            {
                var msg = (int)wParam;
                var info = Marshal.PtrToStructure<NativeMethods.MouseLlHookStruct>(lParam);
                switch (msg)
                {
                    case NativeMethods.WM_MOUSEMOVE:
                        // MSLLHOOKSTRUCT.flags 的 0x01 是 LLMHF_INJECTED（注入事件）。
                        // 宏/远程控制注入的移动不算用户活动，跳过（只计物理移动）。
                        if ((info.Flags & 0x0001) == 0)
                            AddDistance(info.X, info.Y);
                        break;
                    case NativeMethods.WM_LBUTTONDOWN: _left++; break;
                    case NativeMethods.WM_RBUTTONDOWN: _right++; break;
                    case NativeMethods.WM_MBUTTONDOWN: _middle++; break;
                    case NativeMethods.WM_XBUTTONDOWN:
                        // XBUTTON1 = 侧键左，XBUTTON2 = 侧键右
                        if ((info.MouseData >> 16) == 1) _side++;
                        else _side++;
                        break;
                }
            }
        }
        catch { /* 钩子里绝不能抛 */ }
        return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    // ---------------- 计数 ----------------

    private void OnKeyDown(int vk, uint scanCode)
    {
        var shift = IsShiftDown();
        var name = KeyNames.NameOf(vk, scanCode, shift);
        if (name.Length == 0) return;

        lock (_sync)
        {
            RollDayIfNeeded();
            if (IsModifier(vk))
            {
                _modifiersDown.Add(vk);
                return;   // 修饰键本身不单独计数
            }
            string key;
            if (shift && KeyNames.IsPunctuation(vk))
            {
                // Shift + 标点：记成标点本身（和老工具口径一致，
                // 老数据里是 ":"、"?"、"_" 这些单独的名字，不是 "SHIFT+;"）
                key = name;
            }
            else
            {
                var combo = ComboPrefix();
                key = combo.Length > 0 ? combo + "+" + name : name;
            }
            _keys.TryGetValue(key, out var c);
            _keys[key] = c + 1;
        }
    }

    private bool IsShiftDown() =>
        _modifiersDown.Contains(0x10) || _modifiersDown.Contains(0xA0) || _modifiersDown.Contains(0xA1);

    private void OnKeyUp(int vk)
    {
        lock (_sync) { _modifiersDown.Remove(vk); }
    }

    private bool IsModifier(int vk) =>
        vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;

    /// <summary>当前按住的修饰键前缀（按固定的修饰键顺序拼，保证统计口径稳定）。</summary>
    private string ComboPrefix()
    {
        // 顺序固定：CTRL → ALT → SHIFT → WIN，和老工具的命名保持一致
        if (_modifiersDown.Contains(0xA2) || _modifiersDown.Contains(0xA3) || _modifiersDown.Contains(0x11))
            return "CTRL";
        if (_modifiersDown.Contains(0x12) || _modifiersDown.Contains(0xA5))
            return "ALT";
        if (_modifiersDown.Contains(0x10) || _modifiersDown.Contains(0xA0) || _modifiersDown.Contains(0xA1))
            return "SHIFT";
        if (_modifiersDown.Contains(0x5B) || _modifiersDown.Contains(0x5C))
            return "WIN";
        return string.Empty;
    }

    private int _lastX, _lastY;
    private bool _hasLast;

    private void AddDistance(int x, int y)
    {
        if (_hasLast)
        {
            double dx = x - _lastX, dy = y - _lastY;
            var px = Math.Sqrt(dx * dx + dy * dy);
            if (px > 0 && px < 5000)   // 跳变（切屏/锁屏）不算移动
            {
                // 像素 → 厘米。DPI 口径见 InputConfig.DistanceDpi 的说明
                // （默认 96，和老工具的历史数据保持一致）
                _distanceCm += px / _distanceDpi * 2.54;
            }
        }
        _lastX = x;
        _lastY = y;
        _hasLast = true;
    }

    private void RollDayIfNeeded()
    {
        var today = DateTime.Today;
        if (today == _day) return;
        Save();
        lock (_sync)
        {
            _day = today;
            _keys.Clear();
            _distanceCm = 0;
            _left = _middle = _right = _side = 0;
        }
    }

    // ---------------- 落盘 / 读盘 ----------------

    public static string DirFor(string dataDir) => Path.Combine(dataDir, "input");

    public static string FileFor(string dataDir, DateTime day) =>
        Path.Combine(DirFor(dataDir), day.ToString("yyyy-MM-dd") + ".json");

    /// <summary>把内存里的当天统计写盘（可随时调用）。</summary>
    public void Save()
    {
        lock (_sync)
        {
            try
            {
                var stat = new InputDayStat
                {
                    Keys = new Dictionary<string, int>(_keys, StringComparer.Ordinal),
                    DistanceCm = _distanceCm,
                    Left = _left,
                    Middle = _middle,
                    Right = _right,
                    Side = _side,
                };                var dir = DirFor(_dataDir);
                Directory.CreateDirectory(dir);
                var path = FileFor(_dataDir, _day);
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(stat, new JsonSerializerOptions { WriteIndented = false }));
                if (File.Exists(path)) File.Replace(tmp, path, path + ".bak");
                else File.Move(tmp, path);
                _lastSave = DateTime.Now;
            }
            catch (Exception ex)
            {
                Program.Trace("[input] 落盘失败: " + ex.Message);
            }
        }
    }

    /// <summary>定时落盘（由外部的心跳调用）。</summary>
    public void Tick()
    {
        lock (_sync)
        {
            RollDayIfNeeded();
            if ((DateTime.Now - _lastSave).TotalSeconds < 20) return;
        }
        Save();
    }

    /// <summary>读某一天的统计（从磁盘）。</summary>
    public static InputDayStat Load(string dataDir, DateTime day)
    {
        try
        {
            var path = FileFor(dataDir, day);
            if (!File.Exists(path)) return new InputDayStat();
            var json = File.ReadAllText(path);
            var stat = JsonSerializer.Deserialize<InputDayStat>(json);
            return stat ?? new InputDayStat();
        }
        catch { return new InputDayStat(); }
    }

    public void Dispose() => Stop();
}

/// <summary>虚拟键码 → 按键名。命名口径和老工具保持一致，这样历史数据能直接合并显示。</summary>
internal static class KeyNames
{
    private static readonly Dictionary<int, string> Named = new()
    {
        [0x08] = "BACKSPACE", [0x09] = "TAB", [0x0D] = "ENTER", [0x1B] = "ESC", [0x20] = "SPACE",
        [0x21] = "PAGE_UP", [0x22] = "PAGE_DOWN", [0x23] = "END", [0x24] = "HOME",
        [0x25] = "LEFT", [0x26] = "UP", [0x27] = "RIGHT", [0x28] = "DOWN",
        [0x2D] = "INSERT", [0x2E] = "DELETE",
        [0x10] = "SHIFT", [0x11] = "CTRL_L", [0x12] = "ALT", [0x5B] = "CMD", [0x5C] = "WIN",
        [0xA0] = "SHIFT_L", [0xA1] = "SHIFT_R", [0xA2] = "CTRL_L", [0xA3] = "CTRL_R",
        [0xA4] = "ALT_L", [0xA5] = "ALT_GR",
        [0x14] = "CAPS_LOCK", [0x90] = "NUM_LOCK", [0x91] = "SCROLL_LOCK",
        [0x13] = "PAUSE", [0x2C] = "PRINT_SCREEN", [0x5D] = "MENU",
        [0xAD] = "MEDIA_VOLUME_MUTE", [0xAE] = "MEDIA_VOLUME_DOWN", [0xAF] = "MEDIA_VOLUME_UP",
        [0xB0] = "MEDIA_NEXT", [0xB1] = "MEDIA_PREVIOUS", [0xB2] = "MEDIA_STOP", [0xB3] = "MEDIA_PLAY_PAUSE",
    };

    /// <summary>取按键名。取不到就退回「&lt;扫描码&gt;」的写法（和老工具一致）。</summary>
    public static string NameOf(int vk, uint scanCode, bool shift = false)
    {
        if (Named.TryGetValue(vk, out var n)) return n;

        // A-Z
        if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
        // 0-9
        if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
        // F1-F24
        if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F);
        // 小键盘
        if (vk >= 0x60 && vk <= 0x69) return "NUM" + (vk - 0x60);

        // 标点：按 US 布局的 OEM 键给出可读名字（按住 Shift 取上面的那个字符）
        var punct = Punctuation(vk, shift);
        if (punct.Length > 0) return punct;

        // 兜底：扫描码
        var sc = scanCode != 0 ? scanCode : NativeMethods.MapVirtualKey((uint)vk, 0);
        return sc != 0 ? "<" + sc + ">" : "VK" + vk.ToString("X2");
    }

    /// <summary>是不是标点键（标点键按 Shift 时记字符本身，不进组合键）。</summary>
    public static bool IsPunctuation(int vk) => Punctuation(vk, false).Length > 0;

    private static string Punctuation(int vk, bool shift) => vk switch
    {
        0xBA => shift ? ":" : ";",
        0xBB => shift ? "+" : "=",
        0xBC => shift ? "<" : ",",
        0xBD => shift ? "_" : "-",
        0xBE => shift ? ">" : ".",
        0xBF => shift ? "?" : "/",
        0xC0 => shift ? "~" : "`",
        0xDB => shift ? "{" : "[",
        0xDC => shift ? "|" : "\\",
        0xDD => shift ? "}" : "]",
        0xDE => shift ? "\"" : "'",
        0xE2 => shift ? "|" : "\\",
        _ => string.Empty,
    };
}
