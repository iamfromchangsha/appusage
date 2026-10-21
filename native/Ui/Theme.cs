using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace AppUsage.Ui;

/// <summary>
/// DPI 缩放助手。
/// PerMonitorV2 下 WinForms 不会自动缩放手工布局（AutoScaleFactor 恒为 1），
/// 所以设计常量统一按 96 DPI 为基准，实际布局时统一乘以当前显示器的缩放系数。
/// </summary>
internal static class Dpi
{
    /// <summary>当前显示器的缩放系数（1.0 = 100%，1.5 = 150%）。</summary>
    public static float Scale { get; set; } = 1f;

    public static int Px(Control _, float value) => (int)Math.Round(value * Scale);

    public static int Px(float value) => (int)Math.Round(value * Scale);

    public static float Of(Control _) => Scale;

    /// <summary>系统级缩放系数（建窗前用）。</summary>
    public static float SystemScale()
    {
        try
        {
            using var g = Graphics.FromHwnd(IntPtr.Zero);
            return g.DpiX / 96f;
        }
        catch { return 1f; }
    }
}

/// <summary>
/// 配色与几何。两套皮肤：
///   深色 = 女神异闻录 V 风格（电光蓝 / 锐角斜切 / 霓虹辉光）
///   浅色 = Claude 风格（暖米白 / 圆角柔和 / 低对比无辉光）
/// </summary>
internal static class Theme
{
    // ---------------- 可切换的状态 ----------------

    public static bool IsDark { get; private set; } = true;

    /// <summary>锐角斜切（SMT）还是圆角柔和（Claude）。</summary>
    public static bool Angular { get; private set; } = true;

    /// <summary>是否用辉光 / 斜体等强效果（浅色版关掉，避免刺眼）。</summary>
    public static bool Effects { get; private set; } = true;

    // ---------------- 颜色 ----------------

    public static Color Bg;
    public static Color BgDeep;
    public static Color Panel;
    public static Color Panel2;
    public static Color Panel3;
    public static Color Border;
    public static Color BorderHot;
    public static Color BorderDim;
    public static Color Accent;
    public static Color Accent2;
    public static Color Accent3;
    public static Color Danger;
    public static Color Text;
    public static Color TextDim;
    public static Color Muted;
    public static Color Muted2;

    private static string[][] _ramps = Array.Empty<string[]>();
    public static string[] RampNames = Array.Empty<string>();

    /// <summary>应用色标盘（跟皮肤走）。</summary>
    public static string[] AppPalette = Array.Empty<string>();

    /// <summary>静态初始化：保证任何控件在构造时都能拿到一套有效配色。</summary>
    static Theme() => Apply(false);

    /// <summary>切换皮肤。调用方需要负责让所有自绘控件重绘。</summary>
    public static void Apply(bool dark)
    {
        IsDark = dark;
        Angular = dark;
        Effects = dark;

        if (dark)
        {
            // 深色：电光蓝，但整体降饱和、降对比，久看不累
            Bg = ColorTranslator.FromHtml("#070912");
            BgDeep = ColorTranslator.FromHtml("#04050C");
            Panel = ColorTranslator.FromHtml("#0C1027");
            Panel2 = ColorTranslator.FromHtml("#141A38");
            Panel3 = ColorTranslator.FromHtml("#1C2450");
            Border = ColorTranslator.FromHtml("#1A2450");
            BorderHot = ColorTranslator.FromHtml("#2E3E7A");
            BorderDim = ColorTranslator.FromHtml("#141B3C");
            Accent = ColorTranslator.FromHtml("#2E6FE0");
            Accent2 = ColorTranslator.FromHtml("#45B8E0");
            Accent3 = ColorTranslator.FromHtml("#6E9BE8");
            Danger = ColorTranslator.FromHtml("#E0605F");
            Text = ColorTranslator.FromHtml("#D6DEEE");
            TextDim = ColorTranslator.FromHtml("#AEBBD6");
            Muted = ColorTranslator.FromHtml("#7A89AC");
            Muted2 = ColorTranslator.FromHtml("#4E5A7A");

            _ramps = new[]
            {
                new[] { "#0E1533", "#12377A", "#1357B8", "#2E8FD8", "#5FC4E8" },
                new[] { "#151024", "#3A1A6B", "#5E2BB0", "#8A4BD8", "#B98FE8" },
                new[] { "#1B1010", "#5E1F16", "#9A3A18", "#C25E22", "#E08A42" },
                new[] { "#101A16", "#14452F", "#1C7A4A", "#2AA165", "#5FC894" },
                new[] { "#16161A", "#38383F", "#5C5C66", "#8A8A96", "#C2C2CC" },
            };
            RampNames = new[] { "蓝", "紫", "橙", "绿", "灰" };
            AppPalette = new[] { "#00D4FF", "#2E6FE0", "#4C8DFF", "#7B5CFF", "#FF3D7F", "#FFB020" };
        }
        else
        {
            // 浅色：Claude 风格 —— 暖米白底、陶土橘强调色、柔和低对比
            Bg = ColorTranslator.FromHtml("#FAF9F5");
            BgDeep = ColorTranslator.FromHtml("#F2F0E8");
            Panel = ColorTranslator.FromHtml("#FFFFFF");
            Panel2 = ColorTranslator.FromHtml("#F4F2EC");
            Panel3 = ColorTranslator.FromHtml("#EBE8DF");
            Border = ColorTranslator.FromHtml("#E5E1D8");
            BorderHot = ColorTranslator.FromHtml("#D3CEC0");
            BorderDim = ColorTranslator.FromHtml("#EFEDE6");
            Accent = ColorTranslator.FromHtml("#D97757");
            Accent2 = ColorTranslator.FromHtml("#C2603F");
            Accent3 = ColorTranslator.FromHtml("#E6A98D");
            Danger = ColorTranslator.FromHtml("#C0563F");
            Text = ColorTranslator.FromHtml("#1F1E1D");
            TextDim = ColorTranslator.FromHtml("#3F3E3A");
            Muted = ColorTranslator.FromHtml("#6B6A63");
            Muted2 = ColorTranslator.FromHtml("#9A9891");

            _ramps = new[]
            {
                new[] { "#F0EDE4", "#EBD3C2", "#E0A585", "#D97757", "#B85A3C" },
                new[] { "#EDEEF0", "#CFD8E4", "#A9BBD4", "#7C9CC4", "#55769E" },
                new[] { "#EDF0EA", "#D0DCC4", "#ADBF92", "#86A063", "#5F7B3E" },
                new[] { "#F0EDF1", "#DACFE0", "#BFA9C9", "#9B7DAF", "#74588A" },
                new[] { "#F1F0EE", "#DEDCD7", "#C6C3BC", "#ABA79E", "#8C8880" },
            };
            RampNames = new[] { "橘", "蓝", "绿", "紫", "灰" };
            AppPalette = new[] { "#D97757", "#7C9CC4", "#86A063", "#9B7DAF", "#B8853A", "#4E8C8A" };
        }
    }

    public static Color[] Levels(int palette) => _ramps[palette % _ramps.Length]
        .Select(ColorTranslator.FromHtml).ToArray();

    // ---------------- 几何 ----------------

    public static GraphicsPath RoundRect(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0) { path.AddRectangle(r); return path; }
        var d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void FillRound(Graphics g, Rectangle r, int radius, Color color)
    {
        using var path = RoundRect(r, radius);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    /// <summary>斜切平行四边形（深色皮肤用）。</summary>
    public static GraphicsPath Parallelogram(Rectangle r, int slant)
    {
        var path = new GraphicsPath();
        path.AddPolygon(new[]
        {
            new Point(r.Left + slant, r.Top),
            new Point(r.Right, r.Top),
            new Point(r.Right - slant, r.Bottom),
            new Point(r.Left, r.Bottom),
        });
        return path;
    }

    /// <summary>切角矩形（深色皮肤用）。</summary>
    public static GraphicsPath Chamfer(Rectangle r, int cut)
    {
        var path = new GraphicsPath();
        path.AddPolygon(new[]
        {
            new Point(r.Left + cut, r.Top),
            new Point(r.Right, r.Top),
            new Point(r.Right, r.Bottom - cut),
            new Point(r.Right - cut, r.Bottom),
            new Point(r.Left, r.Bottom),
            new Point(r.Left, r.Top + cut),
        });
        return path;
    }

    /// <summary>面板轮廓：深色切角锐利，浅色圆角柔和。</summary>
    public static GraphicsPath PanelPath(Rectangle r) =>
        Angular ? Chamfer(r, Dpi.Px(14)) : RoundRect(r, Dpi.Px(13));

    /// <summary>按钮轮廓：深色平行四边形，浅色圆角。</summary>
    public static GraphicsPath ButtonPath(Rectangle r) =>
        Angular ? Parallelogram(r, Dpi.Px(8)) : RoundRect(r, Dpi.Px(9));

    /// <summary>面板左缘的强调色条。</summary>
    public static void FillAccentBar(Graphics g, Rectangle r, Color color)
    {
        if (Angular) FillSlantBar(g, r, Dpi.Px(6), color);
        else FillRound(g, r, Dpi.Px(3), color);
    }

    public static void FillSlantBar(Graphics g, Rectangle r, int slant, Color color)
    {
        using var path = Parallelogram(r, slant);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    public static void FillPath(Graphics g, GraphicsPath path, Color color)
    {
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    /// <summary>带辉光的文字。浅色皮肤自动关掉辉光（低对比更舒服）。</summary>
    public static void DrawGlowText(Graphics g, string text, Font font, PointF at,
                                    Color textColor, Color glowColor, int glow = 6)
    {
        if (!Effects || glow <= 0)
        {
            using var plain = new SolidBrush(textColor);
            g.DrawString(text, font, plain, at);
            return;
        }
        // 只画 2 层阴影：大字号文字画 7 层太贵，统计卡每次刷新都要重画
        using (var shadow = new SolidBrush(Color.FromArgb(55, glowColor.R, glowColor.G, glowColor.B)))
        {
            g.DrawString(text, font, shadow, at.X - glow * 0.7f, at.Y - glow * 0.7f);
            g.DrawString(text, font, shadow, at.X + glow * 0.7f, at.Y + glow * 0.7f);
        }
        using var brush = new SolidBrush(textColor);
        g.DrawString(text, font, brush, at);
    }

    /// <summary>斜体（切变）文字矩阵 —— 深色皮肤的标题用。</summary>
    public static Matrix Shear(float factor)
    {
        var m = new Matrix();
        m.Shear(factor, 0f);
        return m;
    }

    /// <summary>背景的斜向条纹纹理（深色皮肤用）。</summary>
    public static void DrawDiagonalTexture(Graphics g, Rectangle area, int spacing, Color color, int alpha)
    {
        if (!Effects) return;
        using var pen = new Pen(Color.FromArgb(alpha, color), 1);
        for (var x = area.Left - area.Height; x < area.Right; x += spacing)
        {
            g.DrawLine(pen, x, area.Bottom, x + area.Height, area.Top);
        }
    }

    /// <summary>顶部光带（深色皮肤用）。</summary>
    public static void DrawTopGlow(Graphics g, Rectangle area)
    {
        if (!Effects) return;
        using var brush = new LinearGradientBrush(
            new Rectangle(area.Left, area.Top, area.Width, 3),
            Color.FromArgb(0, Accent2), Accent2, LinearGradientMode.Horizontal);
        g.FillRectangle(brush, area.Left, area.Top, area.Width, 3);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>把系统标题栏切成深色（浅色皮肤用浅色标题栏）。</summary>
    public static void ApplyTitleBar(IWin32Window window, bool dark)
    {
        try
        {
            var hwnd = window.Handle;
            var use = dark ? 1 : 0;
            // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE（Win10 2004+），19 = 更早的版本
            if (DwmSetWindowAttribute(hwnd, 20, ref use, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, 19, ref use, sizeof(int));
        }
        catch { /* 老系统不支持就算了 */ }
    }
}
