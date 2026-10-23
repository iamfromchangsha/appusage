using System.Drawing;
using System.Drawing.Drawing2D;
using AppUsage.Core;

namespace AppUsage.Ui;

/// <summary>GitHub 贡献图样式的每日活跃热力图，自绘 GDI+。</summary>
internal sealed class HeatmapControl : Control
{
    private const float Gap = 3;
    private const float LabelWidth = 30;
    private const float MonthHeight = 18;

    private IReadOnlyList<DayPoint> _days = Array.Empty<DayPoint>();
    private int _weeks = 53;
    private int _palette;
    private int _hoverIndex = -1;
    private Point _hoverPoint;

    public event Action<DateTime>? CellClicked;

    public HeatmapControl()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Panel;
        Cursor = Cursors.Hand;
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Weeks
    {
        get => _weeks;
        set { _weeks = Math.Max(4, value); Invalidate(); }
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Palette
    {
        get => _palette;
        set { _palette = Math.Max(0, value); Invalidate(); }
    }

    public void SetData(IReadOnlyList<DayPoint> days)
    {
        _days = days;
        Invalidate();
    }

    private int GapPx => Dpi.Px(this, Gap);
    private int LabelPx => Dpi.Px(this, LabelWidth);
    private int MonthPx => Dpi.Px(this, MonthHeight);

    /// <summary>格子尺寸（同时考虑可用宽和高）。</summary>
    public int CellSize
    {
        get
        {
            // 宽高两个方向都要算：只按宽度算的话，窗口不够高时最底下一行会被裁掉
            var availW = Width - LabelPx - Dpi.Px(this, 4);
            var availH = Height - MonthPx - Dpi.Px(this, 4);
            if (availW <= 0 || availH <= 0) return Dpi.Px(this, 8);

            var byWidth = (int)((availW - (_weeks - 1) * (double)GapPx) / _weeks);
            var byHeight = (int)((availH - 6 * (double)GapPx) / 7);
            return Math.Clamp(Math.Min(byWidth, byHeight), Dpi.Px(this, 6), Dpi.Px(this, 22));
        }
    }

    private (int Col, int Row) HitTest(Point p)
    {
        int size = CellSize;
        int gap = GapPx;
        int col = (p.X - LabelPx) / (size + gap);
        int row = (p.Y - MonthPx) / (size + gap);
        if (col < 0 || col >= _weeks || row < 0 || row > 6) return (-1, -1);
        int index = col * 7 + row;
        if (index < 0 || index >= _days.Count) return (-1, -1);
        return (col, row);
    }

    /// <summary>取某个日期格子在控件内的中心坐标（找不到返回 null）。</summary>
    public Point? GetCellCenter(DateTime date)    {
        int size = CellSize;
        int gap = GapPx;
        int index = -1;
        for (int i = 0; i < _days.Count; i++)
        {
            if (_days[i].Date == date) { index = i; break; }
        }
        if (index < 0) return null;
        int col = index / 7, row = index % 7;
        int x = LabelPx + col * (size + gap) + size / 2;
        int y = MonthPx + row * (size + gap) + size / 2;
        return new Point(x, y);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var (col, row) = HitTest(e.Location);
        int index = col < 0 ? -1 : col * 7 + row;
        if (index != _hoverIndex)
        {
            _hoverIndex = index;
            Invalidate();
        }
        _hoverPoint = e.Location;
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hoverIndex = -1;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        var (col, row) = HitTest(e.Location);
        if (col >= 0)
        {
            var index = col * 7 + row;
            if (index >= 0 && index < _days.Count)
            {
                var day = _days[index];
                if (day.Date <= DateTime.Today) CellClicked?.Invoke(day.Date);
            }
        }
        base.OnMouseDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(Theme.Panel);

        if (_days.Count == 0) return;

        int size = CellSize;
        int gap = GapPx;
        int x0 = LabelPx;
        int y0 = MonthPx;
        var levels = Theme.Levels(_palette);
        var quantiles = ComputeQuartiles();
        var font = new Font("Segoe UI", 7.6f);

        // 月份标签
        using (var brush = new SolidBrush(Theme.Muted2))
        {
            int lastMonth = -1;
            int lastYear = -1;
            for (int col = 0; col < _weeks; col++)
            {
                int index = col * 7;
                if (index >= _days.Count) break;
                var date = _days[index].Date;
                // 原来要求「1 号正好落在这一列的第一天（也就是周日）」才画标签，
                // 结果一年里只有两三个月碰巧对得上，月份轴几乎是空的。
                // 改成：只要**跨到了新的一月**就在这一列标出来（和 GitHub 一样）。
                if (lastMonth == -1)
                {
                    lastMonth = date.Month;
                    lastYear = date.Year;
                    g.DrawString($"{date.Month}月", font, brush, x0 + col * (size + gap), Dpi.Px(this, 1));
                    continue;
                }
                if (date.Month != lastMonth || date.Year != lastYear)
                {
                    lastMonth = date.Month;
                    lastYear = date.Year;
                    g.DrawString($"{date.Month}月", font, brush, x0 + col * (size + gap), Dpi.Px(this, 1));
                }
            }
        }

        // 星期标签（一 / 三 / 五）
        using (var brush = new SolidBrush(Theme.Muted2))
        {
            string[] dow = { "", "一", "", "三", "", "五", "" };
            for (int row = 0; row < 7; row++)
            {
                if (dow[row].Length == 0) continue;
                g.DrawString(dow[row], font, brush, Dpi.Px(this, 6),
                             y0 + row * (size + gap) + (size - Dpi.Px(this, 12)) / 2);
            }
        }
        font.Dispose();

        // 格子
        var today = DateTime.Today;
        var futureBrush = new SolidBrush(Color.FromArgb(40, 30, 48, 100));
        var todayPen = new Pen(Theme.Danger, Dpi.Px(this, 1.8f));
        var todayGlow = new Pen(Color.FromArgb(110, Theme.Danger.R, Theme.Danger.G, Theme.Danger.B), Dpi.Px(this, 3.4f));
        var hoverPen = new Pen(Theme.Accent2, Dpi.Px(this, 1.6f));
        var hoverGlow = new Pen(Color.FromArgb(120, Theme.Accent2.R, Theme.Accent2.G, Theme.Accent2.B), Dpi.Px(this, 3.2f));

        for (int col = 0; col < _weeks; col++)
        {
            for (int row = 0; row < 7; row++)
            {
                int index = col * 7 + row;
                if (index >= _days.Count) break;
                var point = _days[index];
                var rect = new Rectangle(x0 + col * (size + gap), y0 + row * (size + gap), size, size);

                if (point.Date > today)
                {
                    g.FillRectangle(futureBrush, rect);
                    continue;
                }

                var level = LevelOf(point.Seconds, quantiles);
                using (var b = new SolidBrush(levels[level]))
                    g.FillRectangle(b, rect);

                if (level >= 3)
                {
                    using var pen = new Pen(Color.FromArgb(90, 79, 227, 255), Dpi.Px(this, 1f));
                    g.DrawRectangle(pen, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
                }

                if (point.Date == today)
                {
                    g.DrawRectangle(todayGlow, rect.X - 2, rect.Y - 2, rect.Width + 3, rect.Height + 3);
                    g.DrawRectangle(todayPen, rect.X - 1, rect.Y - 1, rect.Width + 1, rect.Height + 1);
                }
                if (index == _hoverIndex)
                {
                    g.DrawRectangle(hoverGlow, rect.X - 2, rect.Y - 2, rect.Width + 3, rect.Height + 3);
                    g.DrawRectangle(hoverPen, rect.X - 1, rect.Y - 1, rect.Width + 1, rect.Height + 1);
                }
            }
        }

        futureBrush.Dispose();
        todayPen.Dispose();
        hoverPen.Dispose();

        DrawTooltip(g);
    }

    private (double Q1, double Q2, double Q3) ComputeQuartiles()
    {
        var values = _days.Where(d => d.Date <= DateTime.Today && d.Seconds > 0)
                           .Select(d => d.Seconds).OrderBy(v => v).ToList();
        if (values.Count == 0) return (1, 1, 1);
        double At(double p) => values[Math.Min(values.Count - 1, (int)(values.Count * p))];
        return (At(0.25), At(0.50), At(0.75));
    }

    private static int LevelOf(double seconds, (double Q1, double Q2, double Q3) q)
    {
        if (seconds <= 0) return 0;
        // 所有非零日都一样（数据还很少）→ 直接给最高级，避免看起来是空的
        if (q.Q1 == q.Q2 && q.Q2 == q.Q3) return 4;
        if (seconds <= q.Q1) return 1;
        if (seconds <= q.Q2) return 2;
        if (seconds <= q.Q3) return 3;
        return 4;
    }

    private void DrawTooltip(Graphics g)
    {
        if (_hoverIndex < 0 || _hoverIndex >= _days.Count) return;
        var point = _days[_hoverIndex];

        string text = $"{point.Date:yyyy-MM-dd}  {FormatDuration(point.Seconds)}";
        using var font = new Font("Segoe UI", 8f);
        var sz = g.MeasureString(text, font);
        int pad = Dpi.Px(this, 7);
        int w = (int)sz.Width + pad * 2;
        int h = (int)sz.Height + Dpi.Px(this, 8);
        int x = Math.Clamp(_hoverPoint.X - w / 2, 2, Math.Max(2, Width - w - 2));
        int y = Math.Max(2, _hoverPoint.Y - h - Dpi.Px(this, 10));

        var tipBg = Theme.IsDark ? ColorTranslator.FromHtml("#010409") : Color.White;
        var tipText = Theme.IsDark ? Theme.Text : Theme.Text;

        using var shadow = new SolidBrush(Color.FromArgb(Theme.IsDark ? 120 : 40, Color.Black));
        g.FillRectangle(shadow, x + 2, y + 2, w, h);
        Theme.FillRound(g, new Rectangle(x, y, w, h), Dpi.Px(this, 4), tipBg);
        using var pen = new Pen(Theme.IsDark ? Theme.Border : Theme.BorderHot);
        g.DrawRectangle(pen, x, y, w, h);
        using var brush = new SolidBrush(tipText);
        g.DrawString(text, font, brush, x + pad, y + Dpi.Px(this, 4));
    }

    internal static string FormatDuration(double seconds)
    {
        var s = (long)Math.Round(Math.Max(0, seconds));
        var h = s / 3600;
        var m = (s % 3600) / 60;
        if (h > 0) return $"{h} 小时 {m} 分";
        if (m > 0) return $"{m} 分";
        return $"{s % 60} 秒";
    }
}
