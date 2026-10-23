using System.Drawing;
using AppUsage.Core;

namespace AppUsage.Ui;

internal sealed class MainForm : Form
{
    private readonly Store _store;
    private readonly Tracker _tracker;
    private readonly Config _config;

    private readonly HeaderBar _header = new();
    private readonly StatCard[] _cards = new StatCard[5];
    private readonly HeatmapControl _heatmap = new();
    private readonly RankPanel _todayRank = new();
    private readonly RankPanel _totalRank = new();
    private readonly TimelineControl _timeline = new();
    private readonly BarChartControl _recent = new();
    private readonly DataGridView _grid = new();
    private readonly Label _heatSub = new();
    private readonly Label _recentSub = new();
    private Card _tableCard = null!;
    private readonly TextBox _search = new();
    private readonly SlantButton _refresh = new();
    private readonly SlantButton[] _rangeButtons = new SlantButton[3];
    private readonly SlantButton[] _paletteButtons = new SlantButton[5];
    private readonly TrayIcon _tray;
    private readonly TrayIcon? _sharedTray;
    private readonly string _dataDir;
    private readonly AppPaths _paths;
    private TableLayoutPanel? _root;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 3000 };
    private ReportForm? _reportForm;
    private int _lastVersion = -1;

    private int _weeks = 53;
    private int _palette;
    private string _sortKey = "total";
    private bool _sortDesc = true;
    private bool _reallyExit;

    public MainForm(Store store, Tracker tracker, Config config, AppPaths paths, TrayIcon? sharedTray = null)
    {
        _store = store;
        _tracker = tracker;
        _config = config;
        _paths = paths;
        _dataDir = paths.DataDir;
        _sharedTray = sharedTray;

        Text = "应用使用时长";
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9f);
        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;

        // 先拿到窗口句柄，才能读到这个显示器真实的 DPI
        _ = Handle;
        Dpi.Scale = Math.Max(1f, DeviceDpi / 96f);

        // 窗口尺寸用物理像素，并保证不超出工作区
        var work = Screen.FromControl(this).WorkingArea;
        int w = Math.Min(Dpi.Px(this, 1400), work.Width - Dpi.Px(this, 16));
        int h = Math.Min(Dpi.Px(this, 880), work.Height - Dpi.Px(this, 16));
        ClientSize = new Size(w, h);
        MinimumSize = new Size(Dpi.Px(this, 900), Dpi.Px(this, 620));
        Location = new Point(work.X + Math.Max(0, (work.Width - w) / 2),
                              work.Y + Math.Max(0, (work.Height - h) / 2));

        BuildUi();

        _header.ThemeToggle.Pressed += () => SetTheme(!Theme.IsDark);
        _header.ReportButton.Pressed += () => OpenReport(DateTime.Today);

        // 按上次的偏好上色（控件已在默认深色下建好，这里统一重刷一遍）
        SetTheme(UiPrefs.Load(paths.DataDir).DarkTheme);

        _tray = BuildTray();

        _timer.Tick += (_, _) => SafeRefresh();
        _timer.Start();

        FormClosing += OnFormClosing;
        _lastVersion = _store.Version;
        RefreshWidgets();
        UpdateStatus();
    }

    // ---------------------------------------------------------------- UI

    private int P(float v) => Dpi.Px(this, v);

    private void BuildUi()
    {
        // 背景用「预生成位图 + 瓦片」而不是透明层实时重绘，否则 CPU 会翻十几倍
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = Theme.Bg,
            BackgroundImage = null,
            Padding = new Padding(0),
        };        root.RowStyles.Add(new RowStyle(SizeType.Absolute, P(56)));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, P(98)));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, P(128)));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 92));

        _root = root;

        _header.Dock = DockStyle.Fill;
        root.Controls.Add(_header, 0, 0);
        root.Controls.Add(BuildStats(), 0, 1);
        root.Controls.Add(BuildMiddle(), 0, 2);
        root.Controls.Add(BuildTotalRank(), 0, 3);
        root.Controls.Add(BuildTable(), 0, 4);

        Controls.Add(root);
    }

    private Control BuildStats()
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1,
            BackColor = Color.Transparent,
            Padding = new Padding(P(18), P(2), P(18), P(10)),
        };
        for (int i = 0; i < 5; i++)
        {
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
            var card = new StatCard { Dock = DockStyle.Fill, Margin = new Padding(0, 0, P(12), 0) };
            _cards[i] = card;
            row.Controls.Add(card, i, 0);
        }
        _cards[0].Accent = Theme.Accent2;
        _cards[1].Accent = Theme.Muted;
        _cards[2].Accent = Theme.Accent;
        _cards[3].Accent = Theme.Accent3;
        _cards[4].Accent = Theme.Danger;
        return row;
    }

    private Control BuildMiddle()
    {
        var mid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Padding = new Padding(P(18), 0, P(18), P(8)),
        };
        mid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        mid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, P(322)));
        mid.Controls.Add(BuildHeatCard(), 0, 0);
        mid.Controls.Add(BuildSide(), 1, 0);
        return mid;
    }

    private Control BuildHeatCard()
    {
        var card = new Card("每日活跃热力图");
        card.Dock = DockStyle.Fill;
        card.Margin = new Padding(0, 0, P(12), 0);

        var inner = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
        };
        inner.RowStyles.Add(new RowStyle(SizeType.Absolute, P(28)));
        inner.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        inner.RowStyles.Add(new RowStyle(SizeType.Absolute, P(22)));
        inner.RowStyles.Add(new RowStyle(SizeType.Absolute, P(20)));
        inner.RowStyles.Add(new RowStyle(SizeType.Absolute, P(78)));

        var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _heatSub.ForeColor = Theme.Muted2;
        _heatSub.Font = new Font("Segoe UI", 8f);
        _heatSub.AutoSize = false;
        _heatSub.TextAlign = ContentAlignment.MiddleLeft;
        _heatSub.Dock = DockStyle.Fill;

        var tools = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
        };

        string[] rangeLabels = { "3 个月", "半年", "一年" };
        int[] rangeWeeks = { 17, 26, 53 };
        for (int i = 0; i < 3; i++)
        {
            int idx = i;
            var b = MakeSegButton(rangeLabels[i]);
            b.Click += (_, _) =>
            {
                _weeks = rangeWeeks[idx];
                _heatmap.Weeks = _weeks;
                for (int k = 0; k < 3; k++) StyleSeg(_rangeButtons[k], k == idx);
                RefreshWidgets();
            };
            _rangeButtons[i] = b;
            StyleSeg(b, i == 2);
            tools.Controls.Add(b);
        }
        tools.Controls.Add(new Label { Text = "　", Width = P(10), AutoSize = false });

        for (int i = 0; i < 5; i++)
        {
            int idx = i;
            var b = MakeSegButton(Theme.RampNames[i]);
            b.Click += (_, _) =>
            {
                _palette = idx;
                _heatmap.Palette = idx;
                for (int k = 0; k < 5; k++) StyleSeg(_paletteButtons[k], k == idx);
                _heatmap.Invalidate();
                foreach (var c in _cards) c.Invalidate();
            };
            _paletteButtons[i] = b;
            StyleSeg(b, i == 0);
            tools.Controls.Add(b);
        }

        top.Controls.Add(_heatSub, 0, 0);
        top.Controls.Add(tools, 1, 0);
        inner.Controls.Add(top, 0, 0);

        _heatmap.Dock = DockStyle.Fill;
        _heatmap.MinimumSize = new Size(0, P(170));   // 至少放得下 7 行，别被挤扁
        _heatmap.CellClicked += OnCellClicked;
        inner.Controls.Add(_heatmap, 0, 1);

        var legend = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        legend.Paint += (_, e) =>
        {
            var levels = Theme.Levels(_palette);
            using var font = new Font("Segoe UI", 7.6f);
            using var dim = new SolidBrush(Theme.Muted2);
            e.Graphics.DrawString("少", font, dim, P(2), P(4));
            for (int i = 0; i < 5; i++)
            {
                var sw = new Rectangle(P(26 + i * 15), P(5), P(12), P(12));
                using (var b = new SolidBrush(levels[i])) e.Graphics.FillRectangle(b, sw);
                using (var pen = new Pen(Color.FromArgb(70, 79, 227, 255))) e.Graphics.DrawRectangle(pen, sw);
            }
            e.Graphics.DrawString("多", font, dim, P(26 + 5 * 15 + 4), P(4));
            e.Graphics.DrawString("点击任意格子查看当天明细", font, dim, P(170), P(4));
        };
        inner.Controls.Add(legend, 0, 2);

        var recentTop = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        recentTop.Paint += (_, e) =>
        {
            using var font = new Font("Segoe UI", 9f, FontStyle.Bold);
            using var b = new SolidBrush(Theme.Text);
            e.Graphics.DrawString("最近 30 天", font, b, P(2), P(2));
            using var pen = new Pen(Theme.Accent, P(1.6f));
            e.Graphics.DrawLine(pen, P(2), P(18), P(74), P(18));
        };
        _recentSub.ForeColor = Theme.Muted2;
        _recentSub.Font = new Font("Segoe UI", 7.8f);
        _recentSub.AutoSize = false;
        _recentSub.TextAlign = ContentAlignment.MiddleRight;
        _recentSub.Dock = DockStyle.Fill;
        recentTop.Controls.Add(_recentSub);
        inner.Controls.Add(recentTop, 0, 3);

        _recent.Dock = DockStyle.Fill;
        inner.Controls.Add(_recent, 0, 4);

        card.Content.Controls.Add(inner);
        return card;
    }

    private Control BuildSide()
    {
        var side = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        side.RowStyles.Add(new RowStyle(SizeType.Percent, 70));
        side.RowStyles.Add(new RowStyle(SizeType.Percent, 30));

        var rankCard = new Card("今日排行");
        rankCard.Dock = DockStyle.Fill;
        rankCard.Margin = new Padding(0, 0, 0, P(6));
        _todayRank.Dock = DockStyle.Fill;
        rankCard.Content.Controls.Add(_todayRank);
        side.Controls.Add(rankCard, 0, 0);

        var timeCard = new Card("今日时间线", "按小时");
        timeCard.Dock = DockStyle.Fill;
        timeCard.Margin = new Padding(0, P(6), 0, 0);
        _timeline.Dock = DockStyle.Fill;
        timeCard.Content.Controls.Add(_timeline);
        side.Controls.Add(timeCard, 0, 1);

        return side;
    }

    private Control BuildTotalRank()
    {
        var card = new Card("累计排行", "按累计使用时长排序");
        card.Dock = DockStyle.Fill;
        card.Margin = new Padding(P(18), 0, P(18), P(8));
        _totalRank.Dock = DockStyle.Fill;
        card.Content.Controls.Add(_totalRank);
        return card;
    }

    private Control BuildTable()
    {
        var card = new Card("应用明细");
        _tableCard = card;
        card.Dock = DockStyle.Fill;
        card.Margin = new Padding(P(18), 0, P(18), P(14));

        _search.Text = "";
        _search.PlaceholderText = "搜索应用…";
        _search.Font = new Font("Segoe UI", 8.6f);
        _search.BackColor = Theme.Bg;
        _search.ForeColor = Theme.Text;
        _search.BorderStyle = BorderStyle.FixedSingle;
        _search.Size = new Size(P(150), P(26));
        _search.TextChanged += (_, _) => FillGrid();

        _refresh.Text = "刷新";
        _refresh.Font = new Font("Segoe UI", 8.4f);
        _refresh.Size = new Size(P(56), P(26));
        _refresh.Margin = new Padding(P(4), 0, 0, 0);
        _refresh.Click += (_, _) => SafeRefresh();

        var tools = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = new Padding(0, P(3), 0, 0),
        };
        tools.Controls.Add(_refresh);
        tools.Controls.Add(_search);
        card.HeaderExtras.Controls.Add(tools);

        ConfigureGrid();
        card.Content.Controls.Add(_grid);
        return card;
    }

    private void ConfigureGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.EnableHeadersVisualStyles = false;
        _grid.BorderStyle = BorderStyle.None;
        _grid.CellBorderStyle = DataGridViewCellBorderStyle.None;
        _grid.RowHeadersVisible = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.ReadOnly = true;
        _grid.AutoGenerateColumns = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.RowTemplate.Height = P(27);
        _grid.ColumnHeadersHeight = P(30);
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 8.4f, FontStyle.Bold);
        _grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(0, 0, P(12), 0);
        _grid.DefaultCellStyle.Font = new Font("Segoe UI", 8.6f);
        _grid.DefaultCellStyle.Padding = new Padding(0, 0, P(12), 0);

        void AddCol(string name, string title, int fill, bool left = false)
        {
            var col = new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = title,
                FillWeight = fill,
                SortMode = DataGridViewColumnSortMode.NotSortable,
            };
            col.DefaultCellStyle.Alignment = left ? DataGridViewContentAlignment.MiddleLeft : DataGridViewContentAlignment.MiddleRight;
            if (left) col.DefaultCellStyle.Padding = new Padding(P(4), 0, P(8), 0);
            _grid.Columns.Add(col);
        }

        AddCol("app", "应用", 260, true);
        AddCol("today", "今日", 70);
        AddCol("d7", "近 7 天", 70);
        AddCol("d30", "近 30 天", 70);
        AddCol("total", "累计", 80);
        AddCol("last", "最近使用", 90);

        ApplyGridTheme();
    }

    /// <summary>按当前皮肤重刷表格配色。</summary>
    private void ApplyGridTheme()
    {
        bool dark = Theme.IsDark;
        _grid.BackgroundColor = Theme.Panel;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Panel2;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = dark ? Theme.Accent2 : Theme.Accent;
        _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.Panel2;
        _grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = dark ? Theme.Accent2 : Theme.Accent;
        _grid.DefaultCellStyle.BackColor = Theme.Panel;
        _grid.DefaultCellStyle.ForeColor = Theme.TextDim;
        _grid.DefaultCellStyle.SelectionBackColor = dark ? Theme.Accent : Theme.Panel3;
        _grid.DefaultCellStyle.SelectionForeColor = dark ? ColorTranslator.FromHtml("#05060F") : Theme.Text;
        _grid.AlternatingRowsDefaultCellStyle.BackColor = dark ? ColorTranslator.FromHtml("#0C1230") : Theme.Panel2;
        _grid.AlternatingRowsDefaultCellStyle.ForeColor = Theme.TextDim;
        _grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = dark ? Theme.Accent : Theme.Panel3;
        _grid.AlternatingRowsDefaultCellStyle.SelectionForeColor = dark ? ColorTranslator.FromHtml("#05060F") : Theme.Text;
        _grid.RowsDefaultCellStyle.SelectionBackColor = dark ? Theme.Accent : Theme.Panel3;
        _grid.RowsDefaultCellStyle.SelectionForeColor = dark ? ColorTranslator.FromHtml("#05060F") : Theme.Text;
        _grid.GridColor = Theme.BorderDim;
        _grid.Invalidate();
    }

    private static SlantButton MakeSegButton(string text)
    {
        return new SlantButton
        {
            Text = text,
            Font = new Font("Segoe UI", 8.4f),
            Size = new Size(Dpi.Px(54), Dpi.Px(26)),
            Margin = new Padding(Dpi.Px(3), 0, Dpi.Px(3), 0),
        };
    }

    private static void StyleSeg(SlantButton b, bool on)
    {
        b.Selected = on;
        b.Invalidate();
    }

    // ------------------------------------------------------------ 数据刷新

    private void SafeRefresh()
    {
        try
        {
            _store.Reload();
            bool changed = _store.Version != _lastVersion;
            _lastVersion = _store.Version;
            if (changed) RefreshWidgets();
            UpdateStatus();
        }
        catch (Exception ex) { _header.Status.Text = "刷新出错：" + ex.Message; }
    }

    private void UpdateStatus()
    {
        _header.Status.Text = _tracker.IsRunning ? "● 追踪器运行中" : "○ 追踪器未运行";
        _header.Status.ForeColor = _tracker.IsRunning ? Theme.Accent2 : Theme.Muted;
        _header.Today.Text = $"今日 {Fmt(_store.TotalForDay(DateTime.Today))}";
        _tray.RefreshTooltip();
    }

    private void RefreshWidgets()
    {
        var today = DateTime.Today;

        double todaySec = _store.TotalForDay(today);
        double yestSec = _store.TotalForDay(today.AddDays(-1));
        double weekSec = 0, monthSec = 0;
        foreach (var (day, bucket) in _store.Days)
        {
            if (day >= today.AddDays(-6)) weekSec += bucket.Total;
            if (day >= today.AddDays(-29)) monthSec += bucket.Total;
        }

        var todayItems = _store.TopForDay(today, 6);
        _cards[0].Value = Fmt(todaySec);
        _cards[0].Sub = todayItems.Count > 0
            ? $"{todayItems.Count} 个应用 · 最多 {todayItems[0].Name} {Fmt(todayItems[0].Seconds)}"
            : "今天还没有记录";

        var diff = todaySec - yestSec;
        _cards[1].Value = Fmt(yestSec);
        _cards[1].Sub = $"较昨日 {(diff > 0 ? "↑" : diff < 0 ? "↓" : "—")} {Fmt(Math.Abs(diff))}";

        _cards[2].Value = Fmt(weekSec);
        _cards[2].Sub = $"日均 {Fmt(weekSec / 7)}";
        _cards[3].Value = Fmt(monthSec);
        _cards[3].Sub = $"日均 {Fmt(monthSec / 30)}";
        _cards[4].Value = $"{_store.TotalSeconds / 3600:0.#} 小时";
        _cards[4].Sub = $"{_store.ActiveDays} 天活跃 · {_store.Apps.Count} 个应用";

        var heat = _store.Heatmap(_weeks);
        _heatmap.SetData(heat);
        int filled = heat.Count(d => d.Seconds > 0);
        var busiest = heat.OrderByDescending(d => d.Seconds).FirstOrDefault();
        _heatSub.Text = busiest is { Seconds: > 0 }
            ? $"{_weeks} 周 · {filled} 天有记录 · 最忙 {busiest.Date:MM-dd}（{Fmt(busiest.Seconds)}）"
            : $"{_weeks} 周 · 暂无记录";

        var recent = _store.LastDays(30);
        _recent.SetData(recent);
        int rActive = recent.Count(d => d.Seconds > 0);
        _recentSub.Text = $"{rActive} 天有记录 · 日均 {Fmt(recent.Sum(d => d.Seconds) / 30)}";

        _todayRank.SetItems(todayItems);
        _totalRank.SetItems(_store.TopOverall(8), 4);
        _timeline.SetData(_store.HourlyForDay(today));

        FillGrid();

        foreach (var c in _cards) c.Invalidate();
    }

    private void FillGrid()
    {
        var list = _store.AppTable();
        var q = _search.Text.Trim();
        if (q.Length > 0)
            list = list.Where(a => a.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();

        list = _sortKey switch
        {
            "name" => list.OrderBy(a => a.Name, StringComparer.CurrentCulture).ToList(),
            "today" => list.OrderByDescending(a => a.Today).ToList(),
            "d7" => list.OrderByDescending(a => a.Week).ToList(),
            "d30" => list.OrderByDescending(a => a.Month).ToList(),
            "last" => list.OrderByDescending(a => a.LastSeen).ToList(),
            _ => list.OrderByDescending(a => a.Total).ToList(),
        };
        if (_sortKey == "name" && !_sortDesc) list.Reverse();

        _grid.SuspendLayout();
        _grid.Rows.Clear();
        foreach (var a in list)
        {
            var rowIndex = _grid.Rows.Add(
                a.Name,
                a.Today > 0 ? Fmt(a.Today) : "—",
                a.Week > 0 ? Fmt(a.Week) : "—",
                a.Month > 0 ? Fmt(a.Month) : "—",
                Fmt(a.Total),
                RelTime(a.LastSeen));
            _grid.Rows[rowIndex].Cells[0].ToolTipText = a.ExePath;
        }
        _grid.ResumeLayout();
        ClearGridSelection();

        _tableCard.SubLabel.Text = $"{_store.Apps.Count} 个应用 · 累计 {_store.TotalSeconds / 3600:0.#} 小时";
        _tableCard.SubLabel.Invalidate();
    }

    /// <summary>清掉表格的默认选中行（否则一打开就是一条亮蓝色高亮）。</summary>
    private void ClearGridSelection()
    {
        _grid.ClearSelection();
        foreach (DataGridViewRow row in _grid.Rows) row.Selected = false;
        _grid.CurrentCell = null;
    }

    /// <summary>切换皮肤（深色 SMT / 浅色 Claude）。</summary>
    public void SetTheme(bool dark)
    {
        try { SetThemeCore(dark); }
        catch (Exception ex) { Program.Trace("SetTheme failed: " + ex.Message); }
    }

    private void SetThemeCore(bool dark)
    {
        Program.Trace($"SetTheme(dark={dark})");
        Theme.Apply(dark);
        Store.SetAccentPalette(Theme.AppPalette);

        _header.ThemeToggle.Text = dark ? "☀ 浅色" : "☾ 深色";
        _header.ThemeToggle.ForeColor = Theme.Muted;
        _header.Title.ForeColor = Theme.Text;
        _header.Title.Font = new Font("Segoe UI", dark ? 15f : 14f, FontStyle.Bold);
        _header.Title.Glow = dark;
        _header.Title.Shear = dark ? -0.18f : 0f;
        _header.Subtitle.ForeColor = Theme.Muted2;
        _header.Status.ForeColor = Theme.Muted;
        _header.Today.ForeColor = dark ? Theme.Accent2 : Theme.Accent;
        _header.BackColor = Theme.BgDeep;

        BackColor = Theme.Bg;
        if (_root is not null) _root.BackColor = Theme.Bg;

        _search.BackColor = Theme.Bg;
        _search.ForeColor = Theme.Text;

        ApplyGridTheme();
        ApplyThemeToTree(this);

        foreach (var b in _rangeButtons) b.Invalidate();
        foreach (var b in _paletteButtons) b.Invalidate();
        _refresh.Invalidate();

        _reportForm?.ApplyTheme();
        Theme.ApplyTitleBar(this, dark);
        Invalidate(true);

        new UiPrefs { DarkTheme = dark }.Save(_dataDir);
        Program.Trace($"SetTheme done, saved dark_theme={dark}");
    }

    /// <summary>递归把树里所有自绘控件的配色换成当前皮肤。</summary>
    private static void ApplyThemeToTree(Control root)
    {
        foreach (Control c in root.Controls)
        {
            switch (c)
            {
                case Card card:
                    card.Content.BackColor = Theme.Panel;
                    card.TitleLabel.ForeColor = Theme.Text;
                    card.SubLabel.ForeColor = Theme.Muted2;
                    break;
                case StatCard stat:
                    stat.BackColor = Theme.Panel;
                    break;
                case RankPanel rank:
                    rank.BackColor = Theme.Panel;
                    break;
                case TimelineControl tl:
                    tl.BackColor = Theme.Panel;
                    break;
                case BarChartControl bc:
                    bc.BackColor = Theme.Panel;
                    break;
                case HeatmapControl heat:
                    heat.BackColor = Theme.Panel;
                    break;
            }
            c.Invalidate();
            ApplyThemeToTree(c);
        }
    }

    private void OnCellClicked(DateTime day)
    {
        Program.Trace($"cell clicked {day:yyyy-MM-dd}");
        OpenReport(day);
    }

    /// <summary>打开日报页（点热力图格子或顶栏按钮都会走到这里）。</summary>
    private void OpenReport(DateTime day)
    {
        if (_reportForm is null || _reportForm.IsDisposed)
        {
            _reportForm = new ReportForm(_store, _config, _paths, _tracker);
        }
        _reportForm.ShowReport(day);
        _reportForm.Show();
        _reportForm.BringToFront();
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

    private static string RelTime(double ts)
    {
        if (ts <= 0) return "—";
        var diff = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 - ts;
        if (diff < 60) return "刚刚";
        if (diff < 3600) return $"{(int)(diff / 60)} 分钟前";
        if (diff < 86400) return $"{(int)(diff / 3600)} 小时前";
        var days = (int)(diff / 86400);
        if (days < 30) return $"{days} 天前";
        return DateTimeOffset.FromUnixTimeMilliseconds((long)(ts * 1000)).LocalDateTime.ToString("yyyy-MM-dd");
    }

    // ------------------------------------------------------------ 托盘

    private TrayIcon BuildTray()
    {
        // 托盘常驻模式下复用宿主创建的图标，避免出现两个托盘图标
        if (_sharedTray is not null) return _sharedTray;

        return new TrayIcon(
            () => $"应用使用时长 · 今日 {Fmt(_store.TotalForDay(DateTime.Today))}",
            ShowMain,
            () => MessageBox.Show(this, $"今日累计使用 {Fmt(_store.TotalForDay(DateTime.Today))}",
                "应用使用时长", MessageBoxButtons.OK, MessageBoxIcon.Information),
            ExitApp);
    }

    private static Icon? TryExtractIcon()
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

    public void ShowMain()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    /// <summary>托盘模式：只显示托盘图标，不弹主窗口。</summary>
    public void StartInTray()
    {
        _tray.Show();
        _tray.RefreshTooltip();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_reallyExit)
        {
            e.Cancel = true;
            Hide();
            _tray.Show();
            _tray.RefreshTooltip();
            return;
        }
        _timer.Stop();
        _tray.Hide();
        if (_sharedTray is null) _tray.Dispose();
    }

    private void ExitApp()
    {
        _reallyExit = true;
        _timer.Stop();
        _tray.Hide();
        _tracker.Stop();
        if (IsHandleCreated && Visible) Close();
        else Application.Exit();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyTitleBar(this, Theme.IsDark);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ClearGridSelection();
        using (var g = Graphics.FromHwnd(Handle))
        {
            Program.Trace($"shown dpi={DeviceDpi} scale={Dpi.Scale:F2} gDpi={g.DpiX} " +
                          $"client={ClientSize} header={_header.Bounds} " +
                          $"title={_header.Title.Bounds} fontH={_header.Title.Font.Height} pt={_header.Title.Font.SizeInPoints} " +
                          $"fontGdi={_header.Title.Font.FontFamily.Name} cardH={_cards[0].Height}");
        }
        var cell = _heatmap.GetCellCenter(DateTime.Today);
        if (cell.HasValue)
        {
            var screen = _heatmap.PointToScreen(cell.Value);
            var client = PointToClient(screen);
            Program.Trace($"today cell client = {client.X},{client.Y} screen = {screen.X},{screen.Y} " +
                          $"heat hwnd = {_heatmap.Handle} cell local = {cell.Value.X},{cell.Value.Y}");
        }
        var tp = _header.ThemeToggle.PointToScreen(new Point(_header.ThemeToggle.Width / 2, _header.ThemeToggle.Height / 2));
        Program.Trace($"theme toggle screen = {tp.X},{tp.Y} hwnd = {_header.ThemeToggle.Handle}");
        Program.Trace($"heatmap bounds = {_heatmap.Bounds} cell = {_heatmap.CellSize}px " +
                      $"gridHeight = {Dpi.Px(18) + 7 * (_heatmap.CellSize + Dpi.Px(3))}");
        var rb = _header.ReportButton.PointToScreen(new Point(_header.ReportButton.Width / 2, _header.ReportButton.Height / 2));
        Program.Trace($"report button screen = {rb.X},{rb.Y} hwnd = {_header.ReportButton.Handle}");
    }

    protected override void SetVisibleCore(bool value)
    {
        base.SetVisibleCore(value);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Program.Trace("form closed");
        _tracker.Stop();
        base.OnFormClosed(e);
    }
}
