using System.Drawing;
using System.Drawing.Drawing2D;
using AppUsage.Core;

namespace AppUsage.Ui;

/// <summary>顶部的统计卡片：切角蓝面板 + 电光蓝斜条 + 青色数值。</summary>
internal sealed class StatCard : Panel
{
    public string Label = "";
    public string Value = "";
    public string Sub = "";
    public Color Accent = Theme.Accent;

    public StatCard()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Panel;
        ForeColor = Theme.Text;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = Theme.PanelPath(r))
        {
            using var fill = new SolidBrush(Theme.Panel);
            g.FillPath(fill, path);
            using var pen = new Pen(Theme.BorderDim);
            g.DrawPath(pen, path);
        }

        // 左缘强调色条
        var bar = new Rectangle(Dpi.Px(2), Dpi.Px(8), Dpi.Px(Theme.Angular ? 5 : 4), Height - Dpi.Px(16));
        Theme.FillAccentBar(g, bar, Accent);

        int pad = Dpi.Px(20);
        using (var f = new Font("Segoe UI", 8.6f))
        using (var b = new SolidBrush(Theme.Muted))
            g.DrawString(Label, f, b, pad, Dpi.Px(12));

        using (var f = new Font("Segoe UI", 19f, FontStyle.Bold))
        {
            // 大字号不叠辉光（看着像浮雕，而且很贵），靠颜色本身做层级
            Theme.DrawGlowText(g, Value, f, new PointF(pad - 1, Dpi.Px(30)), Theme.Text, Accent, 0);
        }

        using (var f = new Font("Segoe UI", 7.8f))
        using (var b = new SolidBrush(Theme.Muted2))
            g.DrawString(Sub, f, b, pad, Math.Max(0, Height - Dpi.Px(24)));
    }
}

/// <summary>排行榜面板：一行一个应用，带进度条。</summary>
internal sealed class RankPanel : Control
{
    private List<RankItem> _items = new();
    private int _columns = 1;

    public RankPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Panel;
    }

    public void SetItems(List<RankItem> items, int columns = 1)
    {
        _items = items;
        _columns = Math.Max(1, columns);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(Theme.Panel);

        if (_items.Count == 0)
        {
            using var f = new Font("Segoe UI", 8.6f);
            using var b = new SolidBrush(Theme.Muted2);
            g.DrawString("暂无数据", f, b, Dpi.Px(12), Dpi.Px(14));
            return;
        }

        int rows = (int)Math.Ceiling(_items.Count / (double)_columns);
        int rowH = Math.Max(Dpi.Px(30), Height / Math.Max(1, rows));
        int colW = Width / _columns;
        double max = _items.Max(i => i.Seconds);
        if (max <= 0) max = 1;

        int icon = Dpi.Px(16);
        int nameX = Dpi.Px(24);
        int valuePad = Dpi.Px(92);
        int barYoff = Dpi.Px(22);
        int barH = Dpi.Px(3);

        using var nameFont = new Font("Segoe UI", 8.6f);
        using var valueFont = new Font("Segoe UI", 8.2f);

        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            int col = i / rows;
            int row = i % rows;
            int x = col * colW + Dpi.Px(8);
            int y = row * rowH + Dpi.Px(4);

            // 应用色标：切角小方块
            var chip = new Rectangle(x, y + Dpi.Px(3), icon, icon);
            using (var path = Theme.Chamfer(chip, Dpi.Px(4)))
            using (var b = new SolidBrush(item.Accent))
                g.FillPath(b, path);

            using (var b = new SolidBrush(Color.FromArgb(210, 5, 6, 15)))
                g.DrawString(item.Name.Length > 0 ? item.Name[..1] : "?", nameFont, b,
                             x + Dpi.Px(3), y + Dpi.Px(2));

            int barW = Math.Max(Dpi.Px(60), colW - nameX - valuePad);
            using (var b = new SolidBrush(Theme.Text))
                g.DrawString(item.Name, nameFont, b, x + nameX, y + Dpi.Px(4));

            using (var b = new SolidBrush(Theme.Accent2))
                g.DrawString(HeatmapControl.FormatDuration(item.Seconds), valueFont, b,
                             x + colW - valuePad, y + Dpi.Px(4));

            int trackY = y + barYoff;
            using (var b = new SolidBrush(Color.FromArgb(70, 30, 48, 100)))
                g.FillRectangle(b, x + nameX, trackY, barW, barH);
            double ratio = item.Seconds / max;
            using (var b = new SolidBrush(item.Accent))
                g.FillRectangle(b, x + nameX, trackY, (int)Math.Max(2, ratio * barW), barH);
        }
    }
}

/// <summary>24 小时活跃时间线。</summary>
internal sealed class TimelineControl : Control
{
    private List<(int Hour, double Seconds)> _hours = new();

    public TimelineControl()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Panel;
    }

    public void SetData(List<(int Hour, double Seconds)> hours)
    {
        _hours = hours;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(Theme.Panel);

        if (_hours.Count == 0) return;

        int axisH = Dpi.Px(14);
        int chartH = Math.Max(Dpi.Px(20), Height - axisH - Dpi.Px(4));
        double max = Math.Max(1, _hours.Max(h => h.Seconds));

        int n = _hours.Count;
        int gap = Dpi.Px(2);
        int padX = Dpi.Px(4);
        int barW = Math.Max(Dpi.Px(3), (Width - padX * 2 - gap * (n - 1)) / n);

        for (int i = 0; i < n; i++)
        {
            var (hour, seconds) = _hours[i];
            int x = padX + i * (barW + gap);
            int h = seconds > 0 ? Math.Max(Dpi.Px(4), (int)(seconds / max * chartH)) : Dpi.Px(2);
            var rect = new Rectangle(x, Dpi.Px(4) + chartH - h, barW, h);

            if (seconds > 0)
            {
                using var b = new LinearGradientBrush(
                    new Rectangle(rect.X, rect.Y, rect.Width, Math.Max(1, rect.Height)),
                    Theme.Accent2, Theme.Accent, LinearGradientMode.Vertical);
                g.FillRectangle(b, rect);
            }
            else
            {
                using var b = new SolidBrush(Color.FromArgb(80, 30, 48, 100));
                g.FillRectangle(b, rect);
            }
        }

        using var f = new Font("Segoe UI", 7f);
        using var b2 = new SolidBrush(Theme.Muted2);
        int ly = Height - Dpi.Px(12);
        g.DrawString("0", f, b2, padX, ly);
        g.DrawString("6", f, b2, Width / 6 - Dpi.Px(3), ly);
        g.DrawString("12", f, b2, Width / 2 - Dpi.Px(6), ly);
        g.DrawString("18", f, b2, Width * 3 / 4 - Dpi.Px(6), ly);
        g.DrawString("23", f, b2, Width - Dpi.Px(14), ly);
    }
}

/// <summary>最近 N 天的柱状趋势。</summary>
internal sealed class BarChartControl : Control
{
    private List<DayPoint> _days = new();

    public BarChartControl()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Panel;
    }

    public void SetData(List<DayPoint> days)
    {
        _days = days;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(Theme.Panel);

        if (_days.Count == 0) return;

        double max = Math.Max(1, _days.Max(d => d.Seconds));
        int gap = Dpi.Px(3);
        int barW = Math.Max(Dpi.Px(4), (Width - gap * (_days.Count - 1)) / _days.Count);
        int chartH = Height - Dpi.Px(16);

        for (int i = 0; i < _days.Count; i++)
        {
            var d = _days[i];
            int x = i * (barW + gap);
            int h = d.Seconds > 0 ? Math.Max(Dpi.Px(4), (int)(d.Seconds / max * chartH)) : Dpi.Px(3);
            var rect = new Rectangle(x, chartH - h, barW, h);

            if (d.Seconds > 0)
            {
                using var b = new LinearGradientBrush(
                    new Rectangle(rect.X, rect.Y, rect.Width, Math.Max(1, rect.Height)),
                    Theme.Accent2, Theme.Accent, LinearGradientMode.Vertical);
                g.FillRectangle(b, rect);
            }
            else
            {
                using var b = new SolidBrush(Color.FromArgb(80, 30, 48, 100));
                g.FillRectangle(b, rect);
            }
        }

        using var f = new Font("Segoe UI", 7f);
        using var b2 = new SolidBrush(Theme.Muted2);
        int ly = Height - Dpi.Px(13);
        g.DrawString($"{_days[0].Date:MM-dd}", f, b2, 0, ly);
        var todayText = "今天";
        var size = g.MeasureString(todayText, f);
        g.DrawString(todayText, f, b2, Width - size.Width, ly);
    }
}
