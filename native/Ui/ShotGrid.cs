using System.Drawing;
using System.Drawing.Drawing2D;
using AppUsage.Core;

namespace AppUsage.Ui;

/// <summary>随机使用截图的缩略图网格。图片在后台线程解码，不卡 UI。</summary>
internal sealed class ShotGrid : Control
{
    private List<ShotRef> _shots = new();
    private readonly Dictionary<string, Image> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _loading = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    public event Action<ShotRef>? ShotClicked;

    public ShotGrid()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Panel;
        Cursor = Cursors.Hand;
    }

    public void SetShots(List<ShotRef> shots, Config config)
    {
        _shots = shots;
        lock (_sync)
        {
            var alive = new HashSet<string>(shots.Select(s => s.ThumbPath), StringComparer.OrdinalIgnoreCase);
            foreach (var key in _cache.Keys.ToList())
                if (!alive.Contains(key)) { _cache[key].Dispose(); _cache.Remove(key); }
        }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.Clear(Theme.Panel);

        if (_shots.Count == 0)
        {
            using var f = new Font("Segoe UI", 8.6f);
            using var b = new SolidBrush(Theme.Muted2);
            g.DrawString("还没有截图。在「设置」里开启截图功能后，这里会显示当天的使用画面。", f, b, 4, 6);
            return;
        }

        int cellW = Dpi.Px(150);
        int cellH = Dpi.Px(96);
        int gap = Dpi.Px(10);
        int pad = Dpi.Px(2);
        int cols = Math.Max(1, (Width - pad * 2) / (cellW + gap));
        using var font = new Font("Segoe UI", 7.4f);

        for (int i = 0; i < _shots.Count; i++)
        {
            int cx = pad + (i % cols) * (cellW + gap);
            int cy = pad + (i / cols) * (cellH + gap + Dpi.Px(14));
            if (cy + cellH > Height) break;

            var shot = _shots[i];
            var rect = new Rectangle(cx, cy, cellW, cellH);

            Image? img = null;
            lock (_sync)
            {
                if (_cache.TryGetValue(shot.ThumbPath, out var cached)) img = cached;
            }

            if (img is null)
            {
                using (var b = new SolidBrush(Theme.Panel2))
                    g.FillRectangle(b, rect);
                using (var pen = new Pen(Theme.Border))
                    g.DrawRectangle(pen, rect);
                using (var b = new SolidBrush(Theme.Muted2))
                    g.DrawString("加载中…", font, b, cx + Dpi.Px(8), cy + Dpi.Px(38));
                RequestLoad(shot);
            }
            else
            {
                using (var b = new SolidBrush(Theme.Panel2))
                    g.FillRectangle(b, rect);
                g.DrawImage(img, rect);
                using (var pen = new Pen(Theme.Border))
                    g.DrawRectangle(pen, rect);
            }

            using (var b = new SolidBrush(Theme.Muted2))
                g.DrawString($"{shot.At:HH:mm:ss}  {shot.App}", font, b, cx, cy + cellH + Dpi.Px(2));
        }
    }

    private void RequestLoad(ShotRef shot)
    {
        lock (_sync)
        {
            if (_cache.ContainsKey(shot.ThumbPath) || !_loading.Add(shot.ThumbPath)) return;
        }
        var path = shot.ThumbPath;
        _ = Task.Run(() =>
        {
            try
            {
                if (!File.Exists(path)) return;
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var img = Image.FromStream(fs);
                lock (_sync) { _cache[path] = img; }
            }
            catch { }
            finally
            {
                lock (_sync) { _loading.Remove(path); }
                try { BeginInvoke(new Action(Invalidate)); } catch { }
            }
        });
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        int cellW = Dpi.Px(150);
        int cellH = Dpi.Px(96);
        int gap = Dpi.Px(10);
        int pad = Dpi.Px(2);
        int cols = Math.Max(1, (Width - pad * 2) / (cellW + gap));

        for (int i = 0; i < _shots.Count; i++)
        {
            int cx = pad + (i % cols) * (cellW + gap);
            int cy = pad + (i / cols) * (cellH + gap + Dpi.Px(14));
            var rect = new Rectangle(cx, cy, cellW, cellH);
            if (!rect.Contains(e.Location)) continue;
            ShotClicked?.Invoke(_shots[i]);
            return;
        }
    }
}
