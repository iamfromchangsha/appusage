using System.Drawing;
using AppUsage.Core;

namespace AppUsage.Ui;

/// <summary>托盘图标：托盘常驻时唯一需要的东西（比整套 UI 轻得多）。</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Func<string> _tooltip;

    public TrayIcon(Func<string> tooltip, Action showMain, Action showToday, Action exit)
    {
        _tooltip = tooltip;

        var icon = ExtractIcon() ?? SystemIcons.Application;
        var menu = new ContextMenuStrip { ShowImageMargin = false, BackColor = Theme.Panel, ForeColor = Theme.Text };
        menu.Items.Add("显示主窗口", null, (_, _) => showMain());
        menu.Items.Add("今天用量", null, (_, _) => showToday());
        menu.Items.Add(new ToolStripSeparator());
        var autoItem = new ToolStripMenuItem("开机自启");
        autoItem.Checked = Autostart.IsEnabled;
        autoItem.Click += (_, _) =>
        {
            if (Autostart.IsEnabled)
            {
                Autostart.Disable();
                autoItem.Checked = false;
            }
            else
            {
                autoItem.Checked = Autostart.Enable();
            }
        };
        menu.Items.Add(autoItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => exit());

        _icon = new NotifyIcon
        {
            Icon = icon,
            Text = tooltip(),
            ContextMenuStrip = menu,
            Visible = false,
        };
        _icon.DoubleClick += (_, _) => showMain();
    }

    public void Show() => _icon.Visible = true;
    public void Hide() => _icon.Visible = false;
    public void RefreshTooltip() => _icon.Text = _tooltip();

    private static Icon? ExtractIcon()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                var icon = Icon.ExtractAssociatedIcon(exe);
                if (icon is not null) return icon;
            }
        }
        catch { /* ignore */ }
        return null;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}

/// <summary>托盘常驻模式：主窗口按需创建，平时完全不构造那套 UI。</summary>
internal sealed class TrayHost
{
    private readonly Store _store;
    private readonly Tracker _tracker;
    private readonly Config _config;
    private readonly string _dataDir;
    private readonly AppPaths _paths;
    private readonly TrayIcon _tray;
    private MainForm? _form;

    public TrayHost(Store store, Tracker tracker, Config config, AppPaths paths)
    {
        _store = store;
        _tracker = tracker;
        _config = config;
        _paths = paths;
        _dataDir = paths.DataDir;

        _tray = new TrayIcon(
            () => $"应用使用时长 · 今日 {Fmt(_store.TotalForDay(DateTime.Today))}",
            ShowMain,
            ShowToday,
            ExitApp);
        _tray.Show();
    }

    public void ShowMain()
    {
        try
        {
            _form ??= new MainForm(_store, _tracker, _config, _paths, _tray);
            _form.ShowMain();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "应用使用时长", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ShowToday()
    {
        MessageBox.Show($"今日累计使用 {Fmt(_store.TotalForDay(DateTime.Today))}",
            "应用使用时长", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    public void ExitApp()
    {
        _tracker.Stop();
        _tray.Dispose();
        _form?.Close();
        Application.Exit();
    }

    private static string Fmt(double seconds)
    {
        var s = (long)Math.Round(Math.Max(0, seconds));
        var h = s / 3600;
        var m = (s % 3600) / 60;
        if (h > 0) return $"{h} 小时 {m} 分";
        if (m > 0) return $"{m} 分";
        return $"{s % 60} 秒";
    }
}
