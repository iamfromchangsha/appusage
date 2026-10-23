using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace AppUsage.Ui;

/// <summary>斜切平行四边形按钮（SMT 招牌形状）：锐角、电光蓝描边、选中时填充发光。</summary>
internal sealed class SlantButton : Control
{
    private bool _hover;
    private bool _selected;

    public SlantButton()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.UserMouse | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        ForeColor = Theme.TextDim;
        Cursor = Cursors.Hand;
        Font = new Font("Segoe UI", 8.4f);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Slant { get; set; } = 8;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selected
    {
        get => _selected;
        set { _selected = value; Invalidate(); }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        // 注意：这里**不要**自己切换 _selected。
        // 选中态是外部通过 Selected 属性设的（时间范围 / 色阶那两组由 StyleSeg 统一管）。
        // 以前这里按下就取反，结果「刷新」这种没人管 Selected 的按钮点一下就永远保持
        // 高亮，看起来像被选中了；按下后拖走（没触发 Click）也会把状态弄乱。
        base.OnMouseDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        int slant = Dpi.Px(Slant);

        Color border = _selected ? Theme.Accent2 : (_hover ? Theme.BorderHot : Theme.Border);
        Color fill = _selected
            ? Color.FromArgb(52, Theme.Accent2.R, Theme.Accent2.G, Theme.Accent2.B)
            : (_hover ? Color.FromArgb(28, Theme.Accent.R, Theme.Accent.G, Theme.Accent.B)
                      : Color.FromArgb(150, Theme.Panel2.R, Theme.Panel2.G, Theme.Panel2.B));

        using (var path = Theme.ButtonPath(r))
        {
            using var b = new SolidBrush(fill);
            g.FillPath(b, path);

            if (_selected)
            {
                using var glow = new Pen(Color.FromArgb(90, Theme.Accent2), Dpi.Px(2.4f));
                g.DrawPath(glow, path);
            }
            using var pen = new Pen(border, _selected ? Dpi.Px(1.4f) : Dpi.Px(1f));
            g.DrawPath(pen, path);
        }

        using var textBrush = new SolidBrush(_selected ? Theme.Accent2 : (_hover ? Theme.Text : Theme.Muted));
        var size = g.MeasureString(Text, Font);
        var state = g.Save();
        g.TranslateTransform((Width - size.Width) / 2f + slant / 2f, (Height - size.Height) / 2f);
        g.DrawString(Text, Font, textBrush, 0, 0);
        g.Restore(state);
    }
}

/// <summary>斜体（切变）文字控件 —— SMT 的标题字都是斜的。</summary>
internal sealed class SlantText : Control
{
    private float _shear = -0.18f;
    private bool _glow;

    public SlantText()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 12.5f, FontStyle.Bold);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float Shear
    {
        get => _shear;
        set { _shear = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Glow
    {
        get => _glow;
        set { _glow = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color GlowColor { get; set; } = Theme.Accent;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var state = g.Save();
        g.TranslateTransform(Dpi.Px(4), (Height - Font.Height) / 2f);
        if (Theme.Angular)
        {
            using (var shear = Theme.Shear(_shear))
            {
                g.Transform = shear;
                if (_glow) Theme.DrawGlowText(g, Text, Font, PointF.Empty, ForeColor, GlowColor, Dpi.Px(7));
                else
                {
                    using var brush = new SolidBrush(ForeColor);
                    g.DrawString(Text, Font, brush, PointF.Empty);
                }
            }
        }
        else
        {
            using var brush = new SolidBrush(ForeColor);
            g.DrawString(Text, Font, brush, PointF.Empty);
        }
        g.Restore(state);
    }
}

/// <summary>
/// 小圆角按钮（自定义控件）。
/// 不用 WinForms 的 Button：Button 收不到「投递」的鼠标消息（自动化测试点不动），
/// 自定义控件走自己的 OnMouseDown，投递消息也能触发。
/// </summary>
internal sealed class MiniButton : Control
{
    private bool _hover;

    public event Action? Pressed;

    public MiniButton()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        ForeColor = Theme.Muted;
        Cursor = Cursors.Hand;
        Font = new Font("Segoe UI", 8.4f);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            // 只在这里抛 Pressed：基类的 Click 是在 MouseUp 时抛的，
            // 两个都订阅会一次点击触发两次（主题来回切等于没切）
            Pressed?.Invoke();
            Invalidate();
        }
        base.OnMouseDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = Theme.Angular ? Theme.Parallelogram(r, Dpi.Px(6)) : Theme.RoundRect(r, Dpi.Px(8)))
        {
            using var fill = new SolidBrush(_hover ? Theme.Panel2 : Theme.Panel);
            g.FillPath(fill, path);
            using var pen = new Pen(_hover ? Theme.Accent : Theme.Border);
            g.DrawPath(pen, path);
        }

        using var brush = new SolidBrush(_hover ? Theme.Text : Theme.Muted);
        var size = g.MeasureString(Text, Font);
        g.DrawString(Text, Font, brush, (Width - size.Width) / 2f, (Height - size.Height) / 2f);
    }
}

/// <summary>斜切分隔线（深色皮肤里的斜杠装饰）。</summary>
internal sealed class SlashRule : Control
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Color { get; set; } = Theme.Accent;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Thickness { get; set; } = 2;

    public SlashRule()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int t = Math.Max(1, Dpi.Px(Thickness));
        using var pen = new Pen(Color, t);
        var h = Height;
        g.DrawLine(pen, Dpi.Px(6), h - t, Width - Dpi.Px(6), 0);
    }
}
