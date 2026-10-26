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
                g.DrawString(Dur.Short(item.Seconds), small, b, Width - durW, y + Dpi.Px(6));

            y += rowH;
        }
    }

    private static string Trim(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}

/// <summary>时长的统一格式化（全 App 一个口径，别各处各写一遍）。</summary>
internal static class Dur
{
    /// <summary>紧凑版：<c>1h05m</c> / <c>53 分</c> / <c>40 秒</c>。</summary>
    public static string Short(double seconds)
    {
        var s = (long)Math.Round(Math.Max(0, seconds));
        var h = s / 3600;
        var m = (s % 3600) / 60;
        if (h > 0) return $"{h}h{m:00}m";
        if (m > 0) return $"{m} 分";
        return $"{s % 60} 秒";
    }
}

/// <summary>键盘 / 鼠标统计：几个大数 + 最常用按键的条形。</summary>
internal sealed class InputStatsView : Control
{
    private InputDayStat _stat = new();

    public InputStatsView()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Panel;
    }

    public void SetStat(InputDayStat stat)
    {
        _stat = stat ?? new InputDayStat();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(Theme.Panel);

        int pad = Dpi.Px(4);
        using var bigFont = new Font("Segoe UI", 15f, FontStyle.Bold);
        using var labelFont = new Font("Segoe UI", 7.8f);
        using var keyFont = new Font("Segoe UI", 8.2f);
        using var numFont = new Font("Segoe UI", 8.2f);

        if (_stat.TotalKeys <= 0 && _stat.Left + _stat.Middle + _stat.Right + _stat.Side <= 0)
        {
            using var b = new SolidBrush(Theme.Muted2);
            g.DrawString("今天还没有键盘 / 鼠标活动的记录。", labelFont, b, pad, Dpi.Px(10));
            return;
        }

        int combo = _stat.Keys.Count(kv => kv.Key.Contains('+'));
        double meters = _stat.DistanceCm / 100.0;

        // 左栏：四个大数（2×2）；右栏：最常用按键
        int leftW = Width / 2;
        int rightX = leftW;

        var bigs = new (string Label, string Value, bool Hi)[]
        {
            ("按键", _stat.TotalKeys.ToString("N0"), false),
            ("其中组合键", combo.ToString("N0"), false),
            ("鼠标移动", meters >= 100 ? meters.ToString("N0") + " m" : meters.ToString("N1") + " m", false),
            ("点击", (_stat.Left + _stat.Middle + _stat.Right + _stat.Side).ToString("N0"), true),
        };
        int cellW = Math.Max(Dpi.Px(80), leftW / 2 - Dpi.Px(6));
        for (int i = 0; i < bigs.Length; i++)
        {
            int x = pad + (i % 2) * cellW;
            // 行距 33：15pt 粗体的实际行高比字号大（约 1.33 倍），
            // 排得太紧下一行的标签会压在数字上
            int y = Dpi.Px(2) + (i / 2) * Dpi.Px(33);
            using (var b = new SolidBrush(Theme.Muted))
                g.DrawString(bigs[i].Label, labelFont, b, x, y);
            using (var b = new SolidBrush(bigs[i].Hi ? Theme.Accent2 : Theme.Text))
                g.DrawString(bigs[i].Value, bigFont, b, x, y + Dpi.Px(11));
        }

        // 鼠标按键细分（放在第二行大数下面，别和数字叠上）
        using (var b = new SolidBrush(Theme.Muted2))
            g.DrawString($"左 {_stat.Left}  右 {_stat.Right}  中 {_stat.Middle}  侧 {_stat.Side}", labelFont, b, pad, Dpi.Px(68));

        // 右栏：最常用按键
        int ky = Dpi.Px(4);
        using (var b = new SolidBrush(Theme.Muted))
            g.DrawString("最常用按键", labelFont, b, rightX, ky);

        var top = _stat.Keys.OrderByDescending(kv => kv.Value).Take(5).ToList();
        if (top.Count == 0) return;

        ky += Dpi.Px(13);
        int rowH = Dpi.Px(13);
        int max = top[0].Value;
        int nameW = Dpi.Px(66);
        int barX = rightX + nameW;
        int barMax = Math.Max(Dpi.Px(30), Width - barX - Dpi.Px(48));

        foreach (var (key, count) in top)
        {
            using (var b = new SolidBrush(Theme.TextDim))
                g.DrawString(Trim(key, 10), keyFont, b, rightX, ky);
            using (var b = new SolidBrush(Theme.Panel2))
                g.FillRectangle(b, barX, ky + Dpi.Px(3), barMax, Dpi.Px(6));
            int w = (int)Math.Max(Dpi.Px(2), count / (double)max * barMax);
            using (var b = new SolidBrush(Theme.Accent2))
                g.FillRectangle(b, barX, ky + Dpi.Px(3), w, Dpi.Px(6));
            using (var b = new SolidBrush(Theme.Muted2))
                g.DrawString(count.ToString("N0"), numFont, b, barX + barMax + Dpi.Px(5), ky);
            ky += rowH;
        }
    }

    private static string Trim(string s, int n) => s.Length <= n ? s : s[..n];
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
                g.DrawString(Dur.Short(item.Seconds), font, b, Width - Dpi.Px(52), y + Dpi.Px(3));

            y += rowH;
        }
    }
}
