using System.Drawing;
using System.Drawing.Drawing2D;

namespace AppUsage.Ui;

/// <summary>带标题栏的卡片容器。深色=切角锐利，浅色=圆角柔和。</summary>
internal sealed class Card : Panel
{
    public Panel Content { get; } = new() { Dock = DockStyle.Fill, BackColor = Theme.Panel };
    public Label TitleLabel { get; } = new();
    public Label SubLabel { get; } = new();
    public Panel HeaderExtras { get; } = new() { BackColor = Color.Transparent };

    public Card(string title, string subtitle = "")
    {
        DoubleBuffered = true;
        BackColor = Theme.Panel;
        ForeColor = Theme.Text;
        Padding = new Padding(1);

        TitleLabel.Text = title;
        TitleLabel.ForeColor = Theme.Text;
        TitleLabel.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        TitleLabel.AutoSize = false;
        TitleLabel.TextAlign = ContentAlignment.MiddleLeft;
        TitleLabel.BackColor = Color.Transparent;

        SubLabel.Text = subtitle;
        SubLabel.ForeColor = Theme.Muted2;
        SubLabel.Font = new Font("Segoe UI", 8f);
        SubLabel.AutoSize = false;
        SubLabel.TextAlign = ContentAlignment.MiddleRight;
        SubLabel.BackColor = Color.Transparent;

        Content.Padding = new Padding(Dpi.Px(20), Dpi.Px(38), Dpi.Px(14), Dpi.Px(10));
        Content.BackColor = Theme.Panel;
        Controls.Add(Content);
        Controls.Add(TitleLabel);
        Controls.Add(SubLabel);
        Controls.Add(HeaderExtras);
        TitleLabel.BringToFront();
        SubLabel.BringToFront();
        HeaderExtras.BringToFront();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutChildren();
    }

    private void LayoutChildren()
    {
        int pad = Dpi.Px(20);
        int headH = Dpi.Px(34);
        TitleLabel.SetBounds(pad, 0, Math.Max(40, Width / 2 - pad), headH);
        SubLabel.SetBounds(Width / 2 - Dpi.Px(120), 0, Math.Max(0, Width / 2 - Dpi.Px(200)), headH);
        HeaderExtras.SetBounds(Math.Max(pad, Width - Dpi.Px(300)), 0, Dpi.Px(280), headH);
        HeaderExtras.Visible = Width > Dpi.Px(560);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        LayoutChildren();
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = Theme.PanelPath(r))
        {
            using var fill = new SolidBrush(Theme.Panel);
            g.FillPath(fill, path);
            using var pen = new Pen(Theme.Border);
            g.DrawPath(pen, path);
        }

        // 左缘强调色条
        int barW = Dpi.Px(Theme.Angular ? 6 : 4);
        var bar = new Rectangle(Dpi.Px(2), Dpi.Px(10), barW, Height - Dpi.Px(20));
        Theme.FillAccentBar(g, bar, Theme.Accent);

        // 标题下方的短斜线（浅色皮肤改成普通短横线）
        using (var pen = new Pen(Theme.Angular ? Theme.Accent2 : Theme.Accent, Dpi.Px(1.6f)))
        {
            if (Theme.Angular)
                g.DrawLine(pen, Dpi.Px(20), Dpi.Px(35), Dpi.Px(72), Dpi.Px(35));
            else
                g.DrawLine(pen, Dpi.Px(20), Dpi.Px(35), Dpi.Px(62), Dpi.Px(35));
        }
    }
}

/// <summary>窗口顶栏：深色=斜体发光标题，浅色=朴素暖色标题。</summary>
internal sealed class HeaderBar : Panel
{
    public SlantText Title = new();
    public Label Subtitle = new();
    public Label Status = new();
    public Label Today = new();
    public MiniButton ThemeToggle = new();
    public MiniButton ReportButton = new();

    public HeaderBar()
    {
        DoubleBuffered = true;
        // 顶栏必须不透明：透明控件每次重绘都会让父层重画背景，CPU 代价很高
        BackColor = Theme.BgDeep;
        Height = 56;

        Title.Text = "应用使用时长";
        Title.ForeColor = Theme.Text;
        Title.Font = new Font("Segoe UI", Theme.Angular ? 15f : 14f, FontStyle.Bold);
        Title.Glow = Theme.Angular;
        Title.GlowColor = Theme.Accent;
        Title.Shear = Theme.Angular ? -0.18f : 0f;

        Subtitle.Text = "本机前台应用活跃度追踪 · 数据仅存本地";
        Subtitle.ForeColor = Theme.Muted2;
        Subtitle.Font = new Font("Segoe UI", 8.2f);
        Subtitle.AutoSize = false;
        Subtitle.BackColor = Color.Transparent;

        Status.Text = "";
        Status.ForeColor = Theme.Muted;
        Status.Font = new Font("Segoe UI", 8.6f);
        Status.AutoSize = false;
        Status.TextAlign = ContentAlignment.MiddleRight;
        Status.BackColor = Color.Transparent;

        Today.Text = "";
        Today.ForeColor = Theme.Angular ? Theme.Accent2 : Theme.Accent;
        Today.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        Today.AutoSize = false;
        Today.TextAlign = ContentAlignment.MiddleRight;
        Today.BackColor = Color.Transparent;

        ThemeToggle.Text = "浅色";
        ThemeToggle.ForeColor = Theme.Muted;
        ThemeToggle.Font = new Font("Segoe UI", 8.4f);
        ThemeToggle.Size = new Size(Dpi.Px(70), Dpi.Px(26));

        // 不用 📋 之类的 emoji：本机没有可用的 emoji 字体回退，会渲染成豆腐块
        ReportButton.Text = "日报";
        ReportButton.ForeColor = Theme.Accent2;
        ReportButton.Font = new Font("Segoe UI", 8.4f);
        ReportButton.Size = new Size(Dpi.Px(80), Dpi.Px(26));

        Controls.AddRange(new Control[] { Title, Subtitle, Status, Today, ReportButton, ThemeToggle });
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutChildren();
    }

    private void LayoutChildren()
    {
        int x = Dpi.Px(Theme.Angular ? 62 : 58);
        Title.SetBounds(x, Dpi.Px(6), Dpi.Px(360), Dpi.Px(30));
        Subtitle.SetBounds(x, Dpi.Px(36), Dpi.Px(380), Dpi.Px(18));

        // 右侧一排，从右往左依次摆：主题按钮 / 日报按钮 / 今日时长 / 状态点。
        // 之前「今日时长」是个右对齐的透明 Label，盒子正好盖住「日报」按钮，
        // 按钮既看不见也点不到（日报页根本进不去）。现在按间距排开，互不重叠。
        int gap = Dpi.Px(12);
        int right = Width - Dpi.Px(16);

        ThemeToggle.SetBounds(right - Dpi.Px(70), Dpi.Px(12), Dpi.Px(70), Dpi.Px(26));
        right = ThemeToggle.Left - gap;
        ReportButton.SetBounds(right - Dpi.Px(80), Dpi.Px(12), Dpi.Px(80), Dpi.Px(26));
        right = ReportButton.Left - gap;
        // Label 是右对齐的：文字贴着盒子右沿，所以盒子右沿必须让开日报按钮左沿
        Today.SetBounds(right - Dpi.Px(170), Dpi.Px(11), Dpi.Px(170), Dpi.Px(28));
        right = Today.Left - gap;
        Status.SetBounds(right - Dpi.Px(170), Dpi.Px(14), Dpi.Px(170), Dpi.Px(24));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        LayoutChildren();
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        Theme.DrawDiagonalTexture(g, new Rectangle(0, 0, Width, Height), 30, Theme.Accent, 14);
        Theme.DrawTopGlow(g, new Rectangle(0, 0, Width, Height));

        // 底部分割线
        using (var pen = new Pen(Theme.Border))
            g.DrawLine(pen, 0, Height - 1, Width, Height - 1);

        if (Theme.Angular)
        {
            using (var pen = new Pen(Theme.Accent, Dpi.Px(2f)))
                g.DrawLine(pen, 0, Height - 2, Dpi.Px(240), Height - 2);
            using (var pen = new Pen(Theme.Accent2, Dpi.Px(2f)))
                g.DrawLine(pen, Dpi.Px(250), Height - 2, Dpi.Px(330), Height - 2);
            using (var pen = new Pen(Theme.Accent, Dpi.Px(1f)))
                g.DrawLine(pen, Dpi.Px(340), Height - 2, Width, Height - 2);
        }
        else
        {
            using (var pen = new Pen(Theme.Accent, Dpi.Px(2f)))
                g.DrawLine(pen, 0, Height - 2, Dpi.Px(300), Height - 2);
        }

        // Logo：四格方块
        int off = Dpi.Px(16);
        int logo = Dpi.Px(32);
        var rect = new Rectangle(off, Dpi.Px(12), logo, logo);
        var shell = Theme.Angular ? Theme.Chamfer(rect, Dpi.Px(6)) : Theme.RoundRect(rect, Dpi.Px(8));
        using (var b = new SolidBrush(Theme.Angular
                   ? Color.FromArgb(38, Theme.Accent.R, Theme.Accent.G, Theme.Accent.B)
                   : Theme.Panel2))
            g.FillPath(b, shell);
        using (var pen = new Pen(Theme.Angular ? Theme.Accent : Theme.BorderHot))
            g.DrawPath(pen, shell);

        int cell = Dpi.Px(8);
        int gap = Dpi.Px(3);
        int x1 = rect.X + Dpi.Px(6);
        int y1 = rect.Y + Dpi.Px(6);
        void Dot(int dx, int dy, Color color)
        {
            using var b = new SolidBrush(color);
            using var path = Theme.RoundRect(new Rectangle(x1 + dx, y1 + dy, cell, cell), Dpi.Px(2));
            g.FillPath(b, path);
        }
        Dot(0, 0, Theme.Accent2);
        Dot(cell + gap, 0, Theme.Accent);
        Dot(0, cell + gap, Theme.Accent3);
        Dot(cell + gap, cell + gap, Theme.Angular
            ? Color.FromArgb(120, Theme.Accent.R, Theme.Accent.G, Theme.Accent.B)
            : Theme.Border);
    }
}
