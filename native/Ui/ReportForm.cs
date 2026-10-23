using System.Drawing;
using System.Text;
using AppUsage.Core;

namespace AppUsage.Ui;

/// <summary>每日使用汇报页：概览 / AI 总结 / 使用构成 / 今天看了什么 / 专注度 / 随机截图。</summary>
internal sealed class ReportForm : Form
{
    private readonly Store _store;
    private readonly Config _config;
    private readonly AppPaths _paths;
    private readonly Tracker? _tracker;

    private DateTime _date = DateTime.Today;
    private ReportData _report = new();
    private string _lastAiSummary = string.Empty;

    private readonly Label _title = new();
    private readonly Label _sub = new();
    private readonly MiniButton _prev = new();
    private readonly MiniButton _next = new();
    private readonly MiniButton _today = new();
    private readonly MiniButton _copy = new();

    private readonly StatCard[] _stat = new StatCard[4];
    private readonly StatCard[] _focus = new StatCard[3];

    private readonly Label _aiText = new();
    private readonly MiniButton _aiBtn = new();
    private readonly MiniButton _aiSettings = new();
    private readonly Card _aiCard = new("AI 总结");

    private readonly CategoryBar _catBar = new();
    private readonly TopicList _topicList = new();
    private readonly SiteList _siteList = new();
    private readonly TimelineControl _timeline = new();
    private readonly ShotGrid _shotGrid = new();
    private Card _shotsCard = null!;

    public ReportForm(Store store, Config config, AppPaths paths, Tracker? tracker = null)
    {
        _store = store;
        _config = config;
        _paths = paths;
        _tracker = tracker;

        Text = "每日使用汇报";
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9f);
        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
        StartPosition = FormStartPosition.CenterParent;
        DoubleBuffered = true;

        // 窗口尺寸按 96 DPI 设计，再乘上缩放系数（ClientSize 是物理像素）
        _ = Handle;
        var work = Screen.FromControl(this).WorkingArea;
        int w = Math.Min(Dpi.Px(1000), work.Width - Dpi.Px(16));
        int h = Math.Min(Dpi.Px(920), work.Height - Dpi.Px(16));
        ClientSize = new Size(w, h);
        MinimumSize = new Size(Dpi.Px(760), Dpi.Px(560));

        BuildUi();
        RefreshReport();
    }

    // ---------------------------------------------------------------- UI

    private int P(float v) => Dpi.Px(v);

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            BackColor = Theme.Bg,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, P(46)));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, P(92)));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, P(104)));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, P(98)));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, P(150)));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, P(168)));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildStats(), 0, 1);
        root.Controls.Add(BuildAiCard(), 0, 2);
        root.Controls.Add(BuildCategoryCard(), 0, 3);
        root.Controls.Add(BuildTopicsCard(), 0, 4);
        root.Controls.Add(BuildFocusCard(), 0, 5);
        root.Controls.Add(BuildShotsCard(), 0, 6);

        Controls.Add(root);
    }

    private Control BuildHeader()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(P(18), P(6), P(18), 0) };

        _title.Text = "";
        _title.ForeColor = Theme.Text;
        _title.Font = new Font("Segoe UI", 13f, FontStyle.Bold);
        _title.AutoSize = false;
        _title.BackColor = Color.Transparent;
        _title.SetBounds(P(18), P(4), P(420), P(26));

        _sub.Text = "";
        _sub.ForeColor = Theme.Muted2;
        _sub.Font = new Font("Segoe UI", 8.2f);
        _sub.AutoSize = false;
        _sub.BackColor = Color.Transparent;
        _sub.SetBounds(P(18), P(26), P(520), P(18));

        void Nav(MiniButton b, string text, int x, int w, Action act)
        {
            b.Text = text;
            b.Size = new Size(P(w), P(26));
            b.Location = new Point(P(x), P(8));
            b.Pressed += act;
            panel.Controls.Add(b);
        }

        Nav(_prev, "‹ 前一天", 700, 72, () => { _date = _date.AddDays(-1); RefreshReport(); });
        Nav(_next, "后一天 ›", 776, 72, () => { _date = _date.AddDays(1); RefreshReport(); });
        Nav(_today, "今天", 852, 50, () => { _date = DateTime.Today; RefreshReport(); });
        Nav(_copy, "复制摘要", 906, 80, CopySummary);

        // 跟随窗口宽度摆位
        panel.Resize += (_, _) =>
        {
            int right = panel.ClientSize.Width - P(18);
            _copy.Location = new Point(right - P(80), P(8));
            _today.Location = new Point(right - P(134), P(8));
            _next.Location = new Point(right - P(210), P(8));
            _prev.Location = new Point(right - P(286), P(8));
        };

        panel.Controls.Add(_title);
        panel.Controls.Add(_sub);
        return panel;
    }

    private Control BuildStats()
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            BackColor = Color.Transparent,
            Padding = new Padding(P(18), 0, P(18), P(6)),
        };
        for (int i = 0; i < 4; i++)
        {
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            var c = new StatCard { Dock = DockStyle.Fill, Margin = new Padding(0, 0, P(12), 0) };
            _stat[i] = c;
            row.Controls.Add(c, i, 0);
        }
        _stat[0].Accent = Theme.Accent2;
        _stat[1].Accent = Theme.Muted;
        _stat[2].Accent = Theme.Accent;
        _stat[3].Accent = Theme.Accent3;
        return row;
    }

    private Control BuildAiCard()
    {
        _aiCard.Dock = DockStyle.Fill;
        _aiCard.Margin = new Padding(P(18), 0, P(18), P(6));

        _aiText.Dock = DockStyle.Fill;
        _aiText.ForeColor = Theme.TextDim;
        _aiText.Font = new Font("Segoe UI", 9f);
        _aiText.BackColor = Color.Transparent;
        _aiText.TextAlign = ContentAlignment.TopLeft;

        _aiBtn.Text = "生成总结";
        _aiBtn.Size = new Size(P(84), P(26));
        _aiBtn.Pressed += () => _ = GenerateSummaryAsync();

        _aiSettings.Text = "⚙";
        _aiSettings.Size = new Size(P(30), P(26));
        _aiSettings.Pressed += () => OpenSettings();

        var tools = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Color.Transparent,
        };
        tools.Controls.Add(_aiSettings);
        tools.Controls.Add(_aiBtn);

        _aiCard.Content.Controls.Add(_aiText);
        _aiCard.HeaderExtras.Controls.Add(tools);
        return _aiCard;
    }

    private Control BuildCategoryCard()
    {
        var card = new Card("使用构成");
        card.Dock = DockStyle.Fill;
        card.Margin = new Padding(P(18), 0, P(18), P(6));

        _catBar.Dock = DockStyle.Fill;
        _catBar.BackColor = Theme.Panel;
        card.Content.Controls.Add(_catBar);
        return card;
    }

    private Control BuildTopicsCard()
    {
        var card = new Card("今天看了什么", "从窗口标题还原的活动页 / 文档");
        card.Dock = DockStyle.Fill;
        card.Margin = new Padding(P(18), 0, P(18), P(6));

        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Panel };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));

        _topicList.Dock = DockStyle.Fill;
        _topicList.BackColor = Theme.Panel;
        _siteList.Dock = DockStyle.Fill;
        _siteList.BackColor = Theme.Panel;

        split.Controls.Add(_topicList, 0, 0);
        split.Controls.Add(_siteList, 1, 0);
        card.Content.Controls.Add(split);
        return card;
    }

    private Control BuildFocusCard()
    {
        var card = new Card("专注度", "按逻辑连续段统计");
        card.Dock = DockStyle.Fill;
        card.Margin = new Padding(P(18), 0, P(18), P(6));

        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Panel };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, P(320)));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var metrics = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Color.Transparent,
        };
        for (int i = 0; i < 3; i++)
        {
            metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
            var c = new StatCard { Dock = DockStyle.Fill, Margin = new Padding(0, 0, P(8), 0) };
            _focus[i] = c;
            metrics.Controls.Add(c, i, 0);
        }
        _focus[0].Accent = Theme.Accent2;
        _focus[1].Accent = Theme.Accent;
        _focus[2].Accent = Theme.Accent3;

        _timeline.Dock = DockStyle.Fill;
        _timeline.BackColor = Theme.Panel;

        split.Controls.Add(metrics, 0, 0);
        split.Controls.Add(_timeline, 1, 0);
        card.Content.Controls.Add(split);
        return card;
    }

    private Control BuildShotsCard()
    {
        var card = new Card("随机使用截图", "已存 GitHub 私有仓库");
        _shotsCard = card;
        card.Dock = DockStyle.Fill;
        card.Margin = new Padding(P(18), 0, P(18), P(10));

        _shotGrid.Dock = DockStyle.Fill;
        _shotGrid.BackColor = Theme.Panel;
        _shotGrid.ShotClicked += OnShotClicked;
        card.Content.Controls.Add(_shotGrid);
        return card;
    }

    // ------------------------------------------------------------ 数据刷新

    /// <summary>切到某一天并刷新。</summary>
    public void ShowReport(DateTime day)
    {
        _date = day.Date;
        RefreshReport();
    }

    public void RefreshReport()
    {
        _store.Reload();
        _report = ReportBuilder.Build(_store, _config, _date, DateTime.Today);
        _report.Shots = ShotIndex.ForDay(_paths.DataDir, _date, _config);

        var wd = new[] { "周日", "周一", "周二", "周三", "周四", "周五", "周六" }[(int)_date.DayOfWeek];
        _title.Text = $"{_date:yyyy 年 MM 月 dd 日} · {wd}";
        _sub.Text = _date == DateTime.Today
            ? "今天（实时更新）"
            : $"历史日报 · {_date.AddDays(-1):MM-dd} 的使用情况";

        _next.Enabled = _date < DateTime.Today;
        _today.Enabled = _date != DateTime.Today;

        // 概览
        _stat[0].Label = "总使用时长";
        _stat[0].Value = Fmt(_report.TotalSeconds);
        _stat[0].Sub = _report.Sessions > 0 ? $"{_report.Sessions} 段使用" : "当天没有记录";

        var diff = _report.TotalSeconds - _report.YesterdaySeconds;
        _stat[1].Label = "较前一天";
        _stat[1].Value = _report.YesterdaySeconds > 0 ? Fmt(_report.YesterdaySeconds) : "—";
        _stat[1].Sub = _report.YesterdaySeconds > 0
            ? $"本日 {(diff > 0 ? "多" : diff < 0 ? "少" : "持平")} {Fmt(Math.Abs(diff))}"
            : "前一天没有记录";

        _stat[2].Label = "近 7 日均";
        _stat[2].Value = _report.WeekSampleDays >= 2 ? Fmt(_report.WeekAvgSeconds) : "样本不足";
        _stat[2].Sub = $"{_report.WeekSampleDays} 个有记录日";

        _stat[3].Label = "首次 – 末次活跃";
        _stat[3].Value = _report.FirstActive is null ? "—" : $"{_report.FirstActive:HH:mm} – {_report.LastActive:HH:mm}";
        _stat[3].Sub = _report.FirstActive is null ? "当天没有记录" : "今天的使用时间跨度";

        // 专注度
        _focus[0].Label = "最长连续使用";
        _focus[0].Value = Fmt(_report.LongestSessionSeconds);
        _focus[0].Sub = $"{_report.Sessions} 段";

        _focus[1].Label = "应用切换次数";
        _focus[1].Value = _report.Switches.ToString();
        _focus[1].Sub = _report.Sessions > 0 ? $"平均每段 {Fmt(_report.TotalSeconds / Math.Max(1, _report.Sessions))}" : "—";

        _focus[2].Label = "碎片率";
        _focus[2].Value = $"{_report.FragmentRate:P0}";
        _focus[2].Sub = "越低说明越专注";

        // 构成 / 主题
        _catBar.SetItems(_report.Categories);
        _topicList.SetItems(_report.Topics);
        _siteList.SetItems(_report.Sites);
        _timeline.SetData(_store.HourlyForDay(_date));
        _shotGrid.SetShots(_report.Shots, _config);
        _shotsCard.SubLabel.Text = _report.Shots.Count > 0
            ? $"{_report.Shots.Count} 张 · {ShotService.Current?.Status ?? "已存 GitHub 私有仓库"}"
            : (ShotService.Current?.Status ?? "已存 GitHub 私有仓库");

        SyncAiState();
    }

    // ------------------------------------------------------------ AI 总结

    private void SyncAiState()
    {
        var ai = _config.Ai;
        if (!ai.Enabled || ai.ApiKey.Length == 0)
        {
            _aiText.Text = "尚未配置 DeepSeek。点右上角 ⚙ 填入 API Key 后，就能用 AI 把当天数据写成一段人话总结。\n" +
                           "（不填也能看报告，所有统计都是本地离线算的）";
            _aiBtn.Text = "⚙ 设置";
            _aiBtn.Invalidate();
            return;
        }
        if (_aiText.Text.Length == 0) _aiText.Text = "点「生成总结」让 AI 写一段当天的使用小结。";
        _aiBtn.Text = "生成总结";
        _aiBtn.Invalidate();
    }

    private async Task GenerateSummaryAsync()
    {
        if (!_config.Ai.Enabled || _config.Ai.ApiKey.Length == 0)
        {
            OpenSettings();
            return;
        }

        _aiBtn.Text = "生成中…";
        _aiBtn.Invalidate();
        _aiText.Text = "AI 正在写今天的总结…";
        _aiText.Invalidate();

        using var ai = new AiClient(_config);
        try
        {
            var (text, error) = await ai.SummarizeAsync(_report, CancellationToken.None).ConfigureAwait(true);
            if (error is null && !string.IsNullOrWhiteSpace(text))
            {
                _aiText.Text = text;
                _lastAiSummary = text;
            }
            else
            {
                _aiText.Text = "生成失败：" + (error ?? "未知错误");
            }
            _aiText.Invalidate();
        }
        catch (Exception ex)
        {
            _aiText.Text = "生成失败：" + ex.Message;
            _aiText.Invalidate();
        }
        finally
        {
            _aiBtn.Text = "重新生成";
            _aiBtn.Invalidate();
        }
    }

    private void OpenSettings()
    {
        using var dlg = new SettingsForm(_config, _paths);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            SyncAiState();
            RefreshReport();
        }
    }

    internal void SetAiText(string text)
    {
        _aiText.Text = text;
        _aiText.Invalidate();
    }

    internal void SetAiButtons(string generateText, bool enabled)
    {
        _aiBtn.Text = generateText;
        _aiBtn.Enabled = enabled;
        _aiBtn.Invalidate();
    }

    internal Card AiCard => _aiCard;
    internal MiniButton AiButton => _aiBtn;
    internal MiniButton AiSettingsButton => _aiSettings;

    internal static string Fmt(double seconds)
    {
        var s = (long)Math.Round(Math.Max(0, seconds));
        var h = s / 3600;
        var m = (s % 3600) / 60;
        if (h > 0) return $"{h} 小时 {m} 分";
        if (m > 0) return $"{m} 分";
        return $"{s % 60} 秒";
    }

    private void CopySummary()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"【{_date:yyyy-MM-dd} 使用汇报】");
        sb.AppendLine($"总时长：{Fmt(_report.TotalSeconds)}");
        if (_report.YesterdaySeconds > 0)
        {
            var diff = _report.TotalSeconds - _report.YesterdaySeconds;
            sb.AppendLine($"较前一天{(diff >= 0 ? "多" : "少")} {Fmt(Math.Abs(diff))}");
        }
        if (_report.Categories.Count > 0)
        {
            var total = _report.Categories.Sum(c => c.Seconds);
            sb.AppendLine("使用构成：" + string.Join("、",
                _report.Categories.Take(4).Select(c => $"{c.Name} {c.Seconds / total:P0}")));
        }
        if (_report.Sites.Count > 0)
            sb.AppendLine("主要站点：" + string.Join("、", _report.Sites.Take(4).Select(s => $"{s.Name} {Fmt(s.Seconds)}")));
        if (_report.Topics.Count > 0)
            sb.AppendLine("看过的页面：" + string.Join("；", _report.Topics.Take(5).Select(t => t.Text)));
        sb.AppendLine($"专注度：{_report.Sessions} 段 / {_report.Switches} 次切换 / 最长连续 {Fmt(_report.LongestSessionSeconds)}");

        try
        {
            Clipboard.SetText(sb.ToString());
            _copy.Text = "已复制 ✓";
        }
        catch { _copy.Text = "复制失败"; }
        var timer = new System.Windows.Forms.Timer { Interval = 1800 };
        timer.Tick += (_, _) => { _copy.Text = "复制摘要"; timer.Stop(); timer.Dispose(); };
        timer.Start();
    }

    private void OnShotClicked(ShotRef shot)
    {
        if (shot.GithubUrl.Length > 0)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(shot.GithubUrl) { UseShellExecute = true }); }
            catch { }
        }
        else if (shot.ThumbPath.Length > 0)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(shot.ThumbPath) { UseShellExecute = true }); }
            catch { }
        }
    }

    /// <summary>换皮肤时重刷。</summary>
    public void ApplyTheme()
    {
        BackColor = Theme.Bg;
        foreach (Control c in Controls)
        {
            if (c is TableLayoutPanel tlp)
            {
                tlp.BackColor = Theme.Bg;
                foreach (Control r in tlp.Controls) Restyle(r);
            }
            else Restyle(c);
        }
        _aiText.ForeColor = Theme.TextDim;
        _title.ForeColor = Theme.Text;
        _sub.ForeColor = Theme.Muted2;
        RefreshReport();
    }

    private static void Restyle(Control c)
    {
        switch (c)
        {
            case Card card:
                card.BackColor = Theme.Panel;
                card.Content.BackColor = Theme.Panel;
                card.TitleLabel.ForeColor = Theme.Text;
                card.SubLabel.ForeColor = Theme.Muted2;
                break;
            case StatCard st:
                st.BackColor = Theme.Panel;
                break;
            case CategoryBar cb: cb.BackColor = Theme.Panel; break;
            case TopicList tl: tl.BackColor = Theme.Panel; break;
            case SiteList sl: sl.BackColor = Theme.Panel; break;
            case TimelineControl tc: tc.BackColor = Theme.Panel; break;
            case ShotGrid sg: sg.BackColor = Theme.Panel; break;
        }
        if (c is TableLayoutPanel inner)
            foreach (Control r in inner.Controls) Restyle(r);
    }
}
