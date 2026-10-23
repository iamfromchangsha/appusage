using System.Drawing;
using System.Drawing.Drawing2D;
using AppUsage.Core;

namespace AppUsage.Ui;

/// <summary>使用构成：一条堆叠横条 + 图例。</summary>
internal sealed class CategoryBar : Control
{
    private List<CategorySlice> _items = new();

    public CategoryBar()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Panel;
    }

    public void SetItems(List<CategorySlice> items)
    {
        _items = items;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(Theme.Panel);

        int barH = Dpi.Px(16);
        var bar = new Rectangle(0, Dpi.Px(2), Width, barH);
        double total = _items.Sum(i => i.Seconds);

        if (total <= 0 || _items.Count == 0)
        {
            using var b = new SolidBrush(Theme.Panel2);
            g.FillRectangle(b, bar);
            using var f = new Font("Segoe UI", 8.4f);
            using var tb = new SolidBrush(Theme.Muted2);
            g.DrawString("当天没有记录", f, tb, 0, barH + Dpi.Px(6));
            return;
        }

        int x = 0;
        foreach (var item in _items)
        {
            int w = (int)(item.Seconds / total * bar.Width);
            if (w <= 0) continue;
            using (var b = new SolidBrush(item.Color))
                g.FillRectangle(b, x, bar.Y, w, barH);
            x += w;
        }

        // 图例
        int ly = barH + Dpi.Px(8);
        int lx = 0;
        using var font = new Font("Segoe UI", 8.2f);
        foreach (var item in _items)
        {
            var pct = item.Seconds / total;
            var text = $"{item.Name} {pct:P0}";

            using (var b = new SolidBrush(item.Color))
                g.FillRectangle(b, lx, ly + Dpi.Px(3), Dpi.Px(9), Dpi.Px(9));

            var size = g.MeasureString(text, font);
            if (lx + size.Width + Dpi.Px(18) > Width) break;
            using (var b = new SolidBrush(Theme.TextDim))
                g.DrawString(text, font, b, lx + Dpi.Px(14), ly);
            lx += (int)size.Width + Dpi.Px(26);
        }
    }
}

/// <summary>今天看了什么：站点徽标 + 标题 + 时长。</summary>
internal sealed class TopicList : Control
{
    private List<TopicSlice> _items = new();

    public TopicList()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Panel;
    }

    public void SetItems(List<TopicSlice> items)
    {
        _items = items;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Panel);

        if (_items.Count == 0)
        {
            using var f = new Font("Segoe UI", 8.6f);
            using var b = new SolidBrush(Theme.Muted2);
            g.DrawString("当天没有可识别的网页 / 文档标题", f, b, 4, 6);
            return;
        }

        int rowH = Dpi.Px(25);
        int y = 0;
        using var font = new Font("Segoe UI", 8.6f);
        using var small = new Font("Segoe UI", 7.6f);

        foreach (var item in _items)
        {
            if (y + rowH > Height) break;

            // 站点徽标
            if (item.SiteName.Length > 0 || item.Site.Length > 0)
            {
                var label = item.SiteName.Length > 0 ? item.SiteName : item.Site;
                var badge = new Rectangle(0, y + Dpi.Px(4), Dpi.Px(76), Dpi.Px(17));
                using (var path = Theme.RoundRect(badge, Dpi.Px(4)))
                using (var b = new SolidBrush(Color.FromArgb(40, Theme.Accent.R, Theme.Accent.G, Theme.Accent.B)))
                    g.FillPath(b, path);
                using (var b = new SolidBrush(Theme.Accent))
                    g.DrawString(Trim(label, 7), small, b, badge.X + Dpi.Px(6), badge.Y + Dpi.Px(2));
            }

            // 标题
            int titleX = item.SiteName.Length > 0 ? Dpi.Px(84) : Dpi.Px(4);
            int durW = Dpi.Px(64);
            var rect = new Rectangle(titleX, y + Dpi.Px(4), Width - titleX - durW - Dpi.Px(6), Dpi.Px(18));
            using (var b = new SolidBrush(Theme.Text))
                TextRenderer.DrawText(g, item.Text, font, rect, Theme.Text,
                    TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

            // 时长
            using (var b = new SolidBrush(Theme.Accent2))
                g.DrawString(FormatDuration(item.Seconds), small, b, Width - durW, y + Dpi.Px(6));

            y += rowH;
        }
    }

    private static string Trim(string s, int n) => s.Length <= n ? s : s[..n] + "…";

    private static string FormatDuration(double seconds)
    {
        var s = (long)Math.Round(Math.Max(0, seconds));
        var h = s / 3600;
        var m = (s % 3600) / 60;
        if (h > 0) return $"{h}h{m:00}m";
        if (m > 0) return $"{m} 分";
        return $"{s % 60} 秒";
    }
}

/// <summary>站点分布：横向条形列表。</summary>
internal sealed class SiteList : Control
{
    private List<SiteSlice> _items = new();

    public SiteList()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Panel;
    }

    public void SetItems(List<SiteSlice> items)
    {
        _items = items;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Panel);

        if (_items.Count == 0)
        {
            using var f = new Font("Segoe UI", 8.4f);
            using var b = new SolidBrush(Theme.Muted2);
            g.DrawString("当天没有识别到站点", f, b, 4, 6);
            return;
        }

        double max = _items.Max(i => i.Seconds);
        int rowH = Dpi.Px(22);
        int y = 0;
        using var font = new Font("Segoe UI", 8.4f);

        foreach (var item in _items)
        {
            if (y + rowH > Height) break;
            using (var b = new SolidBrush(Theme.Text))
                g.DrawString(item.Name, font, b, 0, y + Dpi.Px(3));

            int barX = Dpi.Px(110);
            int barW = Width - barX - Dpi.Px(56);
            int barH = Dpi.Px(9);
            using (var b = new SolidBrush(Theme.Panel2))
                g.FillRectangle(b, barX, y + Dpi.Px(6), barW, barH);
            using (var b = new SolidBrush(Theme.Accent))
                g.FillRectangle(b, barX, y + Dpi.Px(6), (int)Math.Max(2, item.Seconds / max * barW), barH);

            using (var b = new SolidBrush(Theme.Muted))
                g.DrawString($"{item.Seconds / 60:0} 分", font, b, Width - Dpi.Px(52), y + Dpi.Px(3));

            y += rowH;
        }
    }
}
