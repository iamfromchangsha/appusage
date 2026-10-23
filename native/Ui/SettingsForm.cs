using System.Drawing;
using AppUsage.Core;

namespace AppUsage.Ui;

/// <summary>设置页：DeepSeek / GitHub 私有仓 / 截图。</summary>
internal sealed class SettingsForm : Form
{
    private readonly Config _config;
    private readonly AppPaths _paths;

    private readonly CheckBox _aiEnabled = new() { Text = "启用 AI 总结", AutoSize = true };
    private readonly TextBox _aiBase = new();
    private readonly TextBox _aiKey = new() { UseSystemPasswordChar = true };
    private readonly ComboBox _aiModel = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _aiThinking = new() { Text = "思考模式（更准也稍慢）", AutoSize = true };
    private readonly CheckBox _aiTopics = new() { Text = "把页面标题一并发给 AI", AutoSize = true };
    private readonly Label _aiStatus = new() { AutoSize = false, Height = 20 };

    private readonly TextBox _ghToken = new() { UseSystemPasswordChar = true };
    private readonly TextBox _ghOwner = new();
    private readonly TextBox _ghRepo = new();
    private readonly CheckBox _ghAuto = new() { Text = "仓库不存在时自动创建（私有）", AutoSize = true };
    private readonly Label _ghStatus = new() { AutoSize = false, Height = 20 };

    private readonly CheckBox _shotEnabled = new() { Text = "启用随机使用截图", AutoSize = true };
    private readonly NumericUpDown _shotInterval = new() { Minimum = 5, Maximum = 240, Increment = 5 };
    private readonly NumericUpDown _shotMax = new() { Minimum = 1, Maximum = 100 };
    private readonly TextBox _shotExcluded = new();
    private readonly Label _shotStatus = new() { AutoSize = false, Height = 34 };

    public SettingsForm(Config config, AppPaths paths)
    {
        _config = config;
        _paths = paths;

        Text = "设置";
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9f);
        ClientSize = new Size(660, 620);
        MinimumSize = new Size(620, 560);
        StartPosition = FormStartPosition.CenterParent;
        DoubleBuffered = true;

        BuildUi();
        LoadFromConfig();
    }

    private int P(float v) => Dpi.Px(v);

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Theme.Bg,
            Padding = new Padding(P(16)),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, P(196)));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, P(176)));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, P(188)));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(BuildAiCard(), 0, 0);
        root.Controls.Add(BuildGithubCard(), 0, 1);
        root.Controls.Add(BuildShotsCard(), 0, 2);
        root.Controls.Add(BuildButtons(), 0, 3);

        Controls.Add(root);
    }

    private Card BuildAiCard()
    {
        var card = new Card("DeepSeek（AI 总结）");
        card.Dock = DockStyle.Fill;
        card.Margin = new Padding(0, 0, 0, P(8));

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5, BackColor = Theme.Panel };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, P(96)));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 4; i++) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, P(26)));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _aiModel.Items.AddRange(new object[] { "deepseek-v4-pro", "deepseek-v4-flash" });
        _aiModel.DropDownWidth = 200;

        AddRow(grid, 0, "启用", _aiEnabled);
        AddRow(grid, 1, "接口地址", _aiBase);
        AddRow(grid, 2, "API Key", _aiKey);
        AddRow(grid, 3, "模型", _aiModel);

        var opts = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Color.Transparent };
        opts.Controls.Add(_aiThinking);
        opts.Controls.Add(_aiTopics);
        var test = MakeButton("测试连接");
        test.Click += async (_, _) => await TestAiAsync(test);
        opts.Controls.Add(test);
        grid.Controls.Add(opts, 1, 4);

        _aiStatus.ForeColor = Theme.Muted2;
        _aiStatus.Font = new Font("Segoe UI", 7.8f);
        grid.Controls.Add(_aiStatus, 1, 4);

        card.Content.Controls.Add(grid);
        return card;
    }

    private Card BuildGithubCard()
    {
        var card = new Card("GitHub 私有仓库（截图存档）");
        card.Dock = DockStyle.Fill;
        card.Margin = new Padding(0, 0, 0, P(8));

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5, BackColor = Theme.Panel };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, P(96)));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 4; i++) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, P(26)));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        AddRow(grid, 0, "Token", _ghToken);
        AddRow(grid, 1, "拥有者", _ghOwner);
        AddRow(grid, 2, "仓库名", _ghRepo);
        AddRow(grid, 3, "", _ghAuto);

        var opts = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Color.Transparent };
        var test = MakeButton("测试 / 创建仓库");
        test.Click += async (_, _) => await TestGithubAsync(test);
        opts.Controls.Add(test);
        grid.Controls.Add(opts, 1, 4);

        _ghStatus.ForeColor = Theme.Muted2;
        _ghStatus.Font = new Font("Segoe UI", 7.8f);
        grid.Controls.Add(_ghStatus, 1, 4);

        card.Content.Controls.Add(grid);
        return card;
    }

    private Card BuildShotsCard()
    {
        var card = new Card("随机使用截图");
        card.Dock = DockStyle.Fill;
        card.Margin = new Padding(0, 0, 0, P(8));

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, BackColor = Theme.Panel };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, P(96)));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 3; i++) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, P(26)));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        AddRow(grid, 0, "启用", _shotEnabled);
        AddRow(grid, 1, "间隔(分钟)", _shotInterval);
        AddRow(grid, 2, "每天上限", _shotMax);

        _shotExcluded.PlaceholderText = "用逗号分隔，例如 wechat,qq";
        grid.Controls.Add(_shotExcluded, 1, 3);

        _shotStatus.ForeColor = Theme.Muted2;
        _shotStatus.Font = new Font("Segoe UI", 7.8f);
        _shotStatus.Text =
            "⚠ 截图可能包含屏幕上的聊天 / 邮件等敏感内容；\n" +
            "开启后截图会上传到你自己的 GitHub 私有仓库（只有你能看到，但仍属于上传到云端）。";
        grid.Controls.Add(_shotStatus, 1, 3);

        card.Content.Controls.Add(grid);
        return card;
    }

    private Control BuildButtons()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
        var save = MakeButton("保存");
        save.Size = new Size(P(84), P(30));
        save.Location = new Point(panel.Width - P(100), P(8));
        save.Click += (_, _) =>
        {
            SaveToConfig();
            _config.Save(_paths.ConfigPath);
            DialogResult = DialogResult.OK;
            Close();
        };
        var cancel = MakeButton("取消");
        cancel.Size = new Size(P(72), P(30));
        cancel.Location = new Point(panel.Width - P(180), P(8));
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        panel.Resize += (_, _) =>
        {
            save.Location = new Point(panel.ClientSize.Width - P(100), P(8));
            cancel.Location = new Point(panel.ClientSize.Width - P(180), P(8));
        };
        panel.Controls.Add(save);
        panel.Controls.Add(cancel);
        return panel;
    }

    private void AddRow(TableLayoutPanel grid, int row, string label, Control control)
    {
        var l = new Label
        {
            Text = label,
            ForeColor = Theme.Muted,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill,
        };
        grid.Controls.Add(l, 0, row);
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0, 0, 0, P(4));
        grid.Controls.Add(control, 1, row);
    }

    private static Button MakeButton(string text)
    {
        var b = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Panel2,
            ForeColor = Theme.TextDim,
            Font = new Font("Segoe UI", 8.4f),
            Size = new Size(Dpi.Px(88), Dpi.Px(26)),
            Margin = new Padding(Dpi.Px(6), 0, 0, 0),
        };
        b.FlatAppearance.BorderColor = Theme.Border;
        return b;
    }

    // ------------------------------------------------------------ 读写

    private void LoadFromConfig()
    {
        var ai = _config.Ai;
        _aiEnabled.Checked = ai.Enabled;
        _aiBase.Text = ai.BaseUrl;
        _aiKey.Text = ai.ApiKey;
        _aiModel.SelectedItem = ai.Model;
        if (_aiModel.SelectedIndex < 0) _aiModel.SelectedIndex = 0;
        _aiThinking.Checked = ai.Thinking;
        _aiTopics.Checked = ai.IncludeTopics;

        var s = _config.Shots;
        _ghToken.Text = s.GithubToken;
        _ghOwner.Text = s.GithubOwner;
        _ghRepo.Text = s.GithubRepo;
        _ghAuto.Checked = s.AutoCreateRepo;

        _shotEnabled.Checked = s.Enabled;
        _shotInterval.Value = Math.Clamp(s.IntervalMinutes, 5, 240);
        _shotMax.Value = Math.Clamp(s.MaxPerDay, 1, 100);
        _shotExcluded.Text = string.Join(", ", s.Excluded);
    }

    private void SaveToConfig()
    {
        var ai = _config.Ai;
        ai.Enabled = _aiEnabled.Checked;
        ai.BaseUrl = _aiBase.Text.Trim();
        ai.ApiKey = _aiKey.Text.Trim();
        ai.Model = _aiModel.SelectedItem?.ToString() ?? "deepseek-v4-pro";
        ai.Thinking = _aiThinking.Checked;
        ai.IncludeTopics = _aiTopics.Checked;

        var s = _config.Shots;
        s.Enabled = _shotEnabled.Checked;
        s.IntervalMinutes = (int)_shotInterval.Value;
        s.MaxPerDay = (int)_shotMax.Value;
        s.GithubToken = _ghToken.Text.Trim();
        s.GithubOwner = _ghOwner.Text.Trim();
        s.GithubRepo = _ghRepo.Text.Trim();
        s.AutoCreateRepo = _ghAuto.Checked;
        s.Excluded = _shotExcluded.Text
            .Split(new[] { ',', '，', ';', '；', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();
    }

    private async Task TestAiAsync(Button btn)
    {
        SaveToConfig();
        btn.Enabled = false;
        btn.Text = "测试中…";
        try
        {
            using var ai = new AiClient(_config);
            var err = await ai.TestAsync(CancellationToken.None);
            _aiStatus.Text = err is null ? "✓ 连接正常，模型可用" : "✗ " + err;
            _aiStatus.ForeColor = err is null ? Theme.Accent2 : Theme.Danger;
        }
        catch (Exception ex)
        {
            _aiStatus.Text = "✗ " + ex.Message;
            _aiStatus.ForeColor = Theme.Danger;
        }
        finally
        {
            btn.Enabled = true;
            btn.Text = "测试连接";
        }
    }

    private async Task TestGithubAsync(Button btn)
    {
        SaveToConfig();
        btn.Enabled = false;
        btn.Text = "检查中…";
        try
        {
            using var up = new ShotUploader(_config);
            var err = await up.EnsureRepoAsync(CancellationToken.None);
            _ghStatus.Text = err is null
                ? $"✓ 仓库 {_ghOwner.Text}/{_ghRepo.Text} 可用"
                : "✗ " + err;
            _ghStatus.ForeColor = err is null ? Theme.Accent2 : Theme.Danger;
        }
        catch (Exception ex)
        {
            _ghStatus.Text = "✗ " + ex.Message;
            _ghStatus.ForeColor = Theme.Danger;
        }
        finally
        {
            btn.Enabled = true;
            btn.Text = "测试 / 创建仓库";
        }
    }
}
