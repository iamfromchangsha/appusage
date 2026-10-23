using System.Drawing;
using AppUsage.Core;

namespace AppUsage.Ui;

/// <summary>
/// 深色皮肤下的自绘细滚动条。
/// <para>
/// DataGridView 自带的滚动条走 Windows 视觉样式，跟随的是**系统**主题 ——
/// 在深蓝底上就是一条刺眼的白条。而且 .NET 9 已经把 <c>ScrollBar.TrackColor</c> /
/// <c>ThumbColor</c> 删了（那是 .NET Framework 才有的），想改也改不了。
/// </para>
/// <para>
/// 所以这里自己画一条盖在它上面。**位置和滚动都走公开 API**
/// （<see cref="DataGridView.FirstDisplayedScrollingRowIndex"/>），
/// 不去碰原生滚动条的 Value —— 那个值单位是像素，而且程序改它不会触发
/// DataGridView 滚动（<c>Scroll</c> 事件只在用户操作时才抛）。
/// </para>
/// </summary>
internal sealed class SlimScrollBar : Control
{
    private readonly DataGridView _grid;
    private bool _dragging;
    private int _dragOffset;

    public ScrollBar Native { get; }

    public SlimScrollBar(DataGridView grid, ScrollBar native)
    {
        _grid = grid;
        Native = native;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Panel;
        Cursor = Cursors.Hand;
    }

    private int FirstRow => Math.Max(0, _grid.FirstDisplayedScrollingRowIndex);

    private int VisibleRows =>
        Math.Max(1, _grid.DisplayRectangle.Height / Math.Max(1, _grid.RowTemplate.Height));

    private int TotalRows => Math.Max(1, _grid.RowCount);

    private int MaxFirstRow => Math.Max(0, TotalRows - VisibleRows);

    /// <summary>滑块在轨道上的位置与长度（按可见行数 / 总行数算比例）。</summary>
    private (int Top, int Height) ThumbRect()
    {
        int track = Math.Max(1, Height);
        int visible = VisibleRows;
        int total = TotalRows;

        // 全部行都放得下 → 没有滚动可言，什么都不画
        if (visible >= total) return (0, 0);

        double frac = Math.Clamp((double)visible / total, 0.08, 1.0);
        int thumbH = Math.Max(Dpi.Px(28), (int)(track * frac));
        if (thumbH > track) thumbH = track;

        double pos = Math.Clamp((double)FirstRow / Math.Max(1, MaxFirstRow), 0, 1);
        int top = (int)((track - thumbH) * pos);
        return (top, thumbH);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        var (top, h) = ThumbRect();
        if (h <= 0) return;   // 没有可滚的内容 → 什么都不画

        int w = Math.Max(2, Dpi.Px(3));
        using (var b = new SolidBrush(Color.FromArgb(70, Theme.Border.R, Theme.Border.G, Theme.Border.B)))
            g.FillRectangle(b, Width - w, 0, w, Height);
        using (var b = new SolidBrush(Theme.BorderHot))
            g.FillRectangle(b, Width - w, top, w, h);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        var (top, h) = ThumbRect();
        if (h > 0 && e.Y >= top && e.Y <= top + h)
        {
            _dragging = true;
            _dragOffset = e.Y - top;
            Capture = true;
        }
        else
        {
            // 点轨道空白处：翻一屏
            ScrollByRows(e.Y < top ? -VisibleRows : VisibleRows);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;
        var (_, h) = ThumbRect();
        if (h <= 0) return;

        int track = Math.Max(1, Height - h);
        double pos = Math.Clamp((e.Y - _dragOffset) / (double)track, 0, 1);
        ScrollToRow((int)Math.Round(pos * MaxFirstRow));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
        Capture = false;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        ScrollByRows(-Math.Sign(e.Delta) * 3);
    }

    private void ScrollByRows(int rows) => ScrollToRow(FirstRow + rows);

    private void ScrollToRow(int row)
    {
        try
        {
            _grid.FirstDisplayedScrollingRowIndex = Math.Clamp(row, 0, MaxFirstRow);
        }
        catch { /* 行数刚好变化时可能抛，忽略 */ }
        Invalidate();
    }
}
