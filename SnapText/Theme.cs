using System.Drawing.Drawing2D;
using System.Reflection;
using SnapText.Controls;

namespace SnapText;

internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(246, 247, 251);
    public static readonly Color Surface = Color.White;
    public static readonly Color SurfaceMuted = Color.FromArgb(249, 250, 253);
    public static readonly Color Border = Color.FromArgb(229, 232, 242);
    public static readonly Color BorderStrong = Color.FromArgb(214, 219, 233);

    public static readonly Color Text = Color.FromArgb(31, 36, 48);
    public static readonly Color TextSecondary = Color.FromArgb(100, 108, 128);
    public static readonly Color TextMuted = Color.FromArgb(145, 152, 170);

    public static readonly Color Primary = Color.FromArgb(108, 92, 231);
    public static readonly Color PrimaryHover = Color.FromArgb(124, 108, 240);
    public static readonly Color PrimaryPressed = Color.FromArgb(91, 75, 209);
    public static readonly Color PrimarySoft = Color.FromArgb(243, 241, 255);
    public static readonly Color PrimarySoftPressed = Color.FromArgb(235, 231, 255);

    public static readonly Color Accent = Color.FromArgb(34, 211, 238);
    public static readonly Color Success = Color.FromArgb(18, 183, 106);
    public static readonly Color Warning = Color.FromArgb(245, 158, 11);
    public static readonly Color Danger = Color.FromArgb(217, 45, 32);

    public const string UiFontFamily = "Microsoft YaHei UI";
    public const string HotkeyText = "Ctrl+Alt+S";

    public static Font UiFont(float size, FontStyle style = FontStyle.Regular)
        => new(UiFontFamily, size, style, GraphicsUnit.Point);

    public static GraphicsPath RoundedRect(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2f, Math.Min(bounds.Width, bounds.Height));
        if (d <= 0.5f)
        {
            path.AddRectangle(bounds);
            return path;
        }

        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        => RoundedRect(new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height), radius);

    public static void FillRounded(Graphics g, RectangleF bounds, float radius, Color color)
    {
        using var path = RoundedRect(bounds, radius);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    public static void DrawRoundedBorder(Graphics g, RectangleF bounds, float radius, Color color, float width = 1f)
    {
        var inset = width / 2f;
        var r = RectangleF.Inflate(bounds, -inset, -inset);
        using var path = RoundedRect(r, Math.Max(0, radius - inset));
        using var pen = new Pen(color, width) { Alignment = PenAlignment.Center };
        g.DrawPath(pen, path);
    }

    public static Icon LoadAppIcon()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("SnapText.Assets.app.ico");
            if (stream is not null)
            {
                return new Icon(stream);
            }
        }
        catch
        {
            // Fall back to the generic icon below.
        }

        return (Icon)SystemIcons.Application.Clone();
    }

    public static void DrawButtonGlyph(Graphics g, ButtonGlyph glyph, RectangleF r, Color color)
    {
        using var pen = new Pen(color, Math.Max(1.4f, r.Width / 12f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        using var brush = new SolidBrush(color);
        float x = r.X, y = r.Y, w = r.Width, h = r.Height;
        switch (glyph)
        {
            case ButtonGlyph.Crop:
                // Four scan corners.
                g.DrawLine(pen, x, y + h * 0.28f, x, y + h * 0.08f);
                g.DrawLine(pen, x, y + h * 0.08f, x + w * 0.28f, y + h * 0.08f);
                g.DrawLine(pen, x + w * 0.72f, y + h * 0.08f, x + w, y + h * 0.08f);
                g.DrawLine(pen, x + w, y + h * 0.08f, x + w, y + h * 0.28f);
                g.DrawLine(pen, x + w, y + h * 0.72f, x + w, y + h * 0.92f);
                g.DrawLine(pen, x + w, y + h * 0.92f, x + w * 0.72f, y + h * 0.92f);
                g.DrawLine(pen, x + w * 0.28f, y + h * 0.92f, x, y + h * 0.92f);
                g.DrawLine(pen, x, y + h * 0.92f, x, y + h * 0.72f);
                g.FillRectangle(brush, x + w * 0.40f, y + h * 0.48f, w * 0.20f, h * 0.06f);
                break;

            case ButtonGlyph.Image:
                using (var path = RoundedRect(new RectangleF(x + w * 0.06f, y + h * 0.12f, w * 0.88f, h * 0.76f), w * 0.14f))
                {
                    g.DrawPath(pen, path);
                }
                g.FillEllipse(brush, x + w * 0.24f, y + h * 0.30f, w * 0.14f, h * 0.14f);
                g.DrawLines(pen, new[]
                {
                    new PointF(x + w * 0.16f, y + h * 0.76f),
                    new PointF(x + w * 0.42f, y + h * 0.50f),
                    new PointF(x + w * 0.58f, y + h * 0.66f),
                    new PointF(x + w * 0.70f, y + h * 0.56f),
                    new PointF(x + w * 0.86f, y + h * 0.74f)
                });
                break;

            case ButtonGlyph.Paste:
                g.DrawRectangle(pen, x + w * 0.16f, y + h * 0.18f, w * 0.68f, h * 0.74f);
                g.FillRectangle(brush, x + w * 0.36f, y + h * 0.08f, w * 0.28f, h * 0.16f);
                g.DrawLine(pen, x + w * 0.32f, y + h * 0.46f, x + w * 0.68f, y + h * 0.46f);
                g.DrawLine(pen, x + w * 0.32f, y + h * 0.66f, x + w * 0.60f, y + h * 0.66f);
                break;

            case ButtonGlyph.Copy:
                g.DrawRectangle(pen, x + w * 0.32f, y + h * 0.30f, w * 0.56f, h * 0.60f);
                g.DrawLines(pen, new[]
                {
                    new PointF(x + w * 0.62f, y + h * 0.30f),
                    new PointF(x + w * 0.62f, y + h * 0.10f),
                    new PointF(x + w * 0.12f, y + h * 0.10f),
                    new PointF(x + w * 0.12f, y + h * 0.62f),
                    new PointF(x + w * 0.32f, y + h * 0.62f)
                });
                break;

            case ButtonGlyph.Trash:
                g.DrawLine(pen, x + w * 0.16f, y + h * 0.24f, x + w * 0.84f, y + h * 0.24f);
                g.DrawLine(pen, x + w * 0.38f, y + h * 0.24f, x + w * 0.42f, y + h * 0.10f);
                g.DrawLine(pen, x + w * 0.42f, y + h * 0.10f, x + w * 0.58f, y + h * 0.10f);
                g.DrawLine(pen, x + w * 0.58f, y + h * 0.10f, x + w * 0.62f, y + h * 0.24f);
                g.DrawLines(pen, new[]
                {
                    new PointF(x + w * 0.24f, y + h * 0.24f),
                    new PointF(x + w * 0.30f, y + h * 0.90f),
                    new PointF(x + w * 0.70f, y + h * 0.90f),
                    new PointF(x + w * 0.76f, y + h * 0.24f)
                });
                break;
        }
    }
}

internal enum ButtonGlyph
{
    None,
    Crop,
    Image,
    Paste,
    Copy,
    Trash
}
