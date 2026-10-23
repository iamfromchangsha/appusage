using AppUsage.Core;

namespace AppUsage.Ui;

/// <summary>
/// 编辑「标题关键词 → 站点域名」词表。
/// <para>
/// 内置词表只覆盖常见站点，剩下 30% 的浏览时间会显示「未识别站点」——
/// 这里让用户自己补词条，不用去改 config.json。
/// </para>
/// </summary>
internal sealed class SiteHintsForm : Form
{
    private readonly TextBox _box = new()
    {
        Multiline = true,
        ScrollBars = ScrollBars.Vertical,
        AcceptsReturn = true,
        WordWrap = false,
        Font = new Font("Consolas", 9f),
        BackColor = Theme.Panel,
        ForeColor = Theme.Text,
        BorderStyle = BorderStyle.FixedSingle,
    };

    private readonly Label _status = new()
    {
        AutoSize = false,
        Height = Dpi.Px(34),
        ForeColor = Theme.Muted2,
        Font = new Font("Segoe UI", 8f),
    };

    public SiteHintsForm(Dictionary<string, string> hints, Dictionary<string, string> names)
    {
        Text = "站点关键词";
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9f);
        ClientSize = new Size(Dpi.Px(520), Dpi.Px(400));
        MinimumSize = new Size(Dpi.Px(420), Dpi.Px(320));
        StartPosition = FormStartPosition.CenterParent;
        DoubleBuffered = true;

        var help = new Label
        {
            Text = "每行一条：关键词=域名[=中文显示名]    例：小红书=xiaohongshu.com=小红书\n" +
                   "关键词出现在窗口标题里，就会被记成对应站点（只记域名，不记完整网址）。",
            Dock = DockStyle.Top,
            Height = Dpi.Px(46),
            ForeColor = Theme.Muted,
            Font = new Font("Segoe UI", 8.2f),
        };

        _box.Dock = DockStyle.Fill;
        _box.Text = string.Join(Environment.NewLine, SiteHintsCodec.Format(hints, names));

        _status.Dock = DockStyle.Bottom;
        _status.Text = $"当前 {SiteHintsCodec.Parse(_box.Lines).Hints.Count} 条自定义规则（内置词表不受影响）";

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = Dpi.Px(38),
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = Theme.Bg,
        };
        var ok = MakeButton("保存");
        ok.Size = new Size(Dpi.Px(84), Dpi.Px(28));
        ok.Click += (_, _) =>
        {
            var (h, n) = SiteHintsCodec.Parse(_box.Lines);
            if (h.Count == 0 && _box.Lines.Any(l => l.Trim().Length > 0 && !l.Trim().StartsWith("#")))
            {
                _status.Text = "✗ 一条都没解析出来，检查格式：每行应该是「关键词=域名」";
                _status.ForeColor = Theme.Danger;
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        };
        var cancel = MakeButton("取消");
        cancel.Size = new Size(Dpi.Px(72), Dpi.Px(28));
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Dpi.Px(16)), BackColor = Theme.Bg };
        body.Controls.Add(_box);

        Controls.Add(body);
        Controls.Add(buttons);
        Controls.Add(_status);
        Controls.Add(help);
    }

    /// <summary>编辑后的词表（关键词→域名、域名→显示名）。</summary>
    public (Dictionary<string, string> Hints, Dictionary<string, string> Names) Result =>
        SiteHintsCodec.Parse(_box.Lines);

    private static Button MakeButton(string text)
    {
        var b = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Panel,
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 8.4f),
            Size = new Size(Dpi.Px(88), Dpi.Px(26)),
            Margin = new Padding(Dpi.Px(6), 0, 0, 0),
        };
        b.FlatAppearance.BorderColor = Theme.Border;
        return b;
    }
}
