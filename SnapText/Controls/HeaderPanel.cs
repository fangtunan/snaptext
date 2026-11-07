using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace SnapText.Controls;

internal sealed class StatusDot : Control
{
    private Color _dotColor = Theme.TextMuted;

    public StatusDot()
    {
        SetStyle(ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Size = new Size(12, 12);
    }

    public Color DotColor
    {
        get => _dotColor;
        set
        {
            if (_dotColor == value)
            {
                return;
            }

            _dotColor = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Background);
        using var halo = new SolidBrush(Color.FromArgb(38, _dotColor));
        using var dot = new SolidBrush(_dotColor);
        e.Graphics.FillEllipse(halo, 0, 0, Width - 1, Height - 1);
        e.Graphics.FillEllipse(dot, 3, 3, Width - 7, Height - 7);
    }
}

internal sealed class HeaderPanel : Panel
{
    private readonly Font _titleFont = Theme.UiFont(23f, FontStyle.Bold);
    private readonly Font _subtitleFont = Theme.UiFont(9.5f);
    private readonly Font _iconFont = Theme.UiFont(21f, FontStyle.Bold);
    private readonly Font _privacyFont = Theme.UiFont(8.4f, FontStyle.Bold);

    public HeaderPanel()
    {
        SetStyle(ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Primary;
        Height = 108;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        using (var brush = new LinearGradientBrush(
                   new Point(0, 0), new Point(Width, Height),
                   Color.FromArgb(83, 66, 202), Color.FromArgb(139, 108, 242)))
        {
            g.FillRectangle(brush, ClientRectangle);
        }

        using (var glow = new SolidBrush(Color.FromArgb(16, 255, 255, 255)))
        {
            g.FillEllipse(glow, Width - 210, -110, 320, 320);
            g.FillEllipse(glow, Width - 360, 46, 190, 190);
        }

        using (var ring = new Pen(Color.FromArgb(18, 255, 255, 255), 1.2f))
        {
            g.DrawEllipse(ring, Width - 250, -75, 250, 250);
            g.DrawEllipse(ring, Width - 174, -33, 120, 120);
        }

        float s = DeviceDpi / 96f;
        int iconSize = Math.Max(42, (int)Math.Round(46 * s));
        int iconX = (int)Math.Round(24 * s);
        int iconY = (Height - iconSize) / 2;
        var iconRect = new RectangleF(iconX, iconY, iconSize, iconSize);
        Theme.FillRounded(g, iconRect, 13f, Color.FromArgb(242, 255, 255, 255));
        using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        using (var iconBrush = new SolidBrush(Theme.Primary))
        {
            g.DrawString("字", _iconFont, iconBrush,
                new RectangleF(iconRect.X, iconRect.Y - 1, iconRect.Width, iconRect.Height), format);
        }

        int textX = iconX + iconSize + (int)Math.Round(16 * s);
        using (var white = new SolidBrush(Color.White))
        {
            g.DrawString("拾字", _titleFont, white, textX, 21 * s);
            using var subtitleBrush = new SolidBrush(Color.FromArgb(218, 255, 255, 255));
            g.DrawString("截图 / 图片  →  中文 · English", _subtitleFont, subtitleBrush, textX + 2 * s, 59 * s);
        }

        if (Width > 600)
        {
            string privacy = "本地识别 · 不上传";
            Size textSize = TextRenderer.MeasureText(privacy, _privacyFont, Size.Empty, TextFormatFlags.NoPadding);
            int pillW = textSize.Width + (int)Math.Round(40 * s);
            int pillH = (int)Math.Round(30 * s);
            int pillX = Width - pillW - (int)Math.Round(24 * s);
            int pillY = (Height - pillH) / 2;
            var pill = new RectangleF(pillX, pillY, pillW, pillH);
            Theme.FillRounded(g, pill, pillH / 2f, Color.FromArgb(30, 255, 255, 255));
            Theme.DrawRoundedBorder(g, pill, pillH / 2f, Color.FromArgb(45, 255, 255, 255), 1f);

            // Small shield mark.
            float cx = pillX + 17 * s;
            float cy = pillY + pillH / 2f;
            using var shieldPen = new Pen(Color.FromArgb(235, 255, 255, 255), Math.Max(1f, 1.1f * s))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            var shield = new[]
            {
                new PointF(cx - 5 * s, cy - 6 * s),
                new PointF(cx + 5 * s, cy - 6 * s),
                new PointF(cx + 5 * s, cy + 1 * s),
                new PointF(cx, cy + 6 * s),
                new PointF(cx - 5 * s, cy + 1 * s),
                new PointF(cx - 5 * s, cy - 6 * s)
            };
            g.DrawLines(shieldPen, shield);
            using var checkPen = new Pen(Color.FromArgb(235, 255, 255, 255), Math.Max(1f, 1.2f * s))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            g.DrawLines(checkPen, new[]
            {
                new PointF(cx - 2.4f * s, cy),
                new PointF(cx - 0.4f * s, cy + 2.1f * s),
                new PointF(cx + 3.2f * s, cy - 2.2f * s)
            });

            TextRenderer.DrawText(g, privacy, _privacyFont,
                new Rectangle(pillX + (int)Math.Round(27 * s), pillY, pillW - (int)Math.Round(31 * s), pillH),
                Color.FromArgb(240, 255, 255, 255),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _titleFont.Dispose();
            _subtitleFont.Dispose();
            _iconFont.Dispose();
            _privacyFont.Dispose();
        }

        base.Dispose(disposing);
    }
}
