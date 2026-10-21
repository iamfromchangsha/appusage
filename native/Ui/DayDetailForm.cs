using System.Drawing;
using System.Drawing.Drawing2D;
using AppUsage.Core;

namespace AppUsage.Ui;

/// <summary>点击热力图格子后弹出的当天明细面板。</summary>
internal sealed class DayDetailForm : Form
{
    private readonly Store _store;
    private readonly Label _title = new();
    private readonly Label _sub = new();
    private readonly Label _total = new();
    private readonly TimelineControl _timeline = new();
    private readonly RankPanel _apps = new();
    private readonly Button _close = new();

    private static int P(float v) => Dpi.Px(v);

    public DayDetailForm(Store store)
    {
        _store = store;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Theme.Panel;
        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
        ClientSize = new Size(P(384), P(540));
        Padding = new Padding(1);

        _title.ForeColor = Theme.Text;
        _title.Font = new Font("Segoe UI", 11.5f, FontStyle.Bold);
        _title.AutoSize = false;
        _title.SetBounds(P(22), P(14), P(300), P(26));

        _sub.ForeColor = Theme.Muted2;
        _sub.Font = new Font("Segoe UI", 8.2f);
        _sub.AutoSize = false;
        _sub.SetBounds(P(22), P(38), P(300), P(18));

        _total.ForeColor = Theme.Accent2;
        _total.Font = new Font("Segoe UI", 22f, FontStyle.Bold);
        _total.AutoSize = false;
        _total.SetBounds(P(22), P(58), P(300), P(36));

        _timeline.SetBounds(P(18), P(120), P(348), P(118));

        _close.Text = "✕";
        _close.FlatStyle = FlatStyle.Flat;
        _close.BackColor = Theme.Panel;
        _close.ForeColor = Theme.Muted;
        _close.Font = new Font("Segoe UI", 9f);
        _close.Size = new Size(P(30), P(28));
        _close.Location = new Point(P(340), P(10));
        _close.Click += (_, _) => Hide();
        _close.MouseEnter += (_, _) => _close.ForeColor = Theme.Accent2;
        _close.MouseLeave += (_, _) => _close.ForeColor = Theme.Muted;

        Controls.AddRange(new Control[] { _title, _sub, _total, _timeline, _close });

        _apps.SetBounds(P(14), P(262), P(356), P(262));
        Controls.Add(_apps);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = Theme.PanelPath(r))
        {
            using var fill = new SolidBrush(Theme.Panel);
            g.FillPath(fill, path);
            using var pen = new Pen(Theme.BorderHot);
            g.DrawPath(pen, path);
        }

        // 左缘强调色条
        Theme.FillAccentBar(g, new Rectangle(P(3), P(14), P(6), Height - P(28)), Theme.Accent);

        using var f = new Font("Segoe UI", 8.4f, FontStyle.Bold);
        using var b = new SolidBrush(Theme.Accent);
        g.DrawString("应用分布", f, b, P(22), P(250));
    }

    /// <summary>换皮肤时同步外观。</summary>
    public void ApplyTheme()
    {
        BackColor = Theme.Panel;
        _close.BackColor = Theme.Panel;
        _close.ForeColor = Theme.Muted;
        _title.ForeColor = Theme.Text;
        _sub.ForeColor = Theme.Muted2;
        _total.ForeColor = Theme.Angular ? Theme.Accent2 : Theme.Accent;
        Invalidate(true);
    }

    public void ShowDay(DateTime day)
    {
        var wd = new[] { "周日", "周一", "周二", "周三", "周四", "周五", "周六" }[(int)day.DayOfWeek];
        _title.Text = $"{day:yyyy 年 MM 月 dd 日} · {wd}";
        var items = _store.TopForDay(day, 30);
        _sub.Text = $"{items.Count} 个应用";
        _total.Text = HeatmapControl.FormatDuration(_store.TotalForDay(day));
        _timeline.SetData(_store.HourlyForDay(day));
        _apps.SetItems(items);
        Invalidate();
    }
}
