using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace AppUsage.Core;

/// <summary>抓取前台窗口画面并编码成 JPEG。</summary>
internal static class Screenshotter
{
    private const uint PwRenderFullContent = 0x00000002;
    private const uint Srccopy = 0x00CC0020;

    /// <summary>
    /// 抓一张窗口图。优先 PrintWindow（对普通 Win32 窗口效果好），
    /// Chromium 窗口常常返回黑图 → 退回 BitBlt 抓屏幕区域；
    /// 两条路都拿不到有效画面就返回 false（静默跳过，不报错）。
    /// </summary>
    public static bool TryCapture(IntPtr hwnd, int maxWidth, int jpegQuality, out byte[] jpeg)
    {
        jpeg = Array.Empty<byte>();
        if (hwnd == IntPtr.Zero) return false;

        if (!NativeMethods.GetWindowRect(hwnd, out var rect)) return false;
        int w = rect.Width, h = rect.Height;
        if (w <= 0 || h <= 0 || w > 20000 || h > 20000) return false;

        using var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        bool got = false;

        using (var g = Graphics.FromImage(bmp))
        {
            var hdc = g.GetHdc();
            try
            {
                if (NativeMethods.PrintWindow(hwnd, hdc, PwRenderFullContent))
                    got = !IsBlank(bmp);
            }
            finally { g.ReleaseHdc(hdc); }

            if (!got)
            {
                // 退回屏幕拷贝（要求窗口在屏幕上可见）
                using (var sg = Graphics.FromHwnd(IntPtr.Zero))
                {
                    var sdc = sg.GetHdc();
                    try
                    {
                        got = NativeMethods.BitBlt(hdc, 0, 0, w, h, sdc, rect.Left, rect.Top, Srccopy);
                    }
                    finally { sg.ReleaseHdc(sdc); }
                }
                if (got) got = !IsBlank(bmp);
            }
        }

        if (!got) return false;

        // 缩放到最大宽度
        using var scaled = ScaleTo(bmp, maxWidth);
        using var ms = new MemoryStream();
        var codec = FindJpegCodec();
        if (codec is null)
        {
            scaled.Save(ms, ImageFormat.Jpeg);
        }
        else
        {
            using var ep = new EncoderParameters(1);
            ep.Param[0] = new EncoderParameter(Encoder.Quality, (long)Math.Clamp(jpegQuality, 20, 95));
            scaled.Save(ms, codec, ep);
        }
        jpeg = ms.ToArray();
        return jpeg.Length > 0;
    }

    private static Bitmap ScaleTo(Bitmap src, int maxWidth)
    {
        if (maxWidth <= 0 || src.Width <= maxWidth) return new Bitmap(src);
        int nw = maxWidth;
        int nh = (int)Math.Round(src.Height * (double)nw / src.Width);
        var dst = new Bitmap(nw, nh, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(dst);
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(src, 0, 0, nw, nh);
        return dst;
    }

    /// <summary>判断画面是否几乎全黑（Chromium 窗口 PrintWindow 常见结果）。</summary>
    private static bool IsBlank(Bitmap bmp)
    {
        try
        {
            long sum = 0;
            int n = 0;
            for (int y = 0; y < bmp.Height; y += Math.Max(1, bmp.Height / 20))
            {
                for (int x = 0; x < bmp.Width; x += Math.Max(1, bmp.Width / 20))
                {
                    var c = bmp.GetPixel(x, y);
                    sum += (c.R + c.G + c.B) / 3;
                    n++;
                }
            }
            if (n == 0) return true;
            return sum / n < 6;   // 平均亮度几乎为 0 → 认为是黑图
        }
        catch { return true; }
    }

    private static ImageCodecInfo? FindJpegCodec()
    {
        foreach (var c in ImageCodecInfo.GetImageEncoders())
            if (c.FormatID == ImageFormat.Jpeg.Guid) return c;
        return null;
    }
}
