using System.Drawing.Drawing2D;

namespace SnapText.Controls;

internal enum ButtonStyle
{
    Primary,
    Secondary,
    Ghost
}

internal sealed class RoundedButton : Button
{
    private bool _hovered;
    private bool _pressed;

    public RoundedButton()
    {
        SetStyle(ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.UiFont(10.5f, FontStyle.Bold);
        Cursor = Cursors.Hand;
        TabStop = true;
        Height = 42;
    }

    public ButtonStyle Style { get; set; } = ButtonStyle.Secondary;

    public ButtonGlyph Glyph { get; set; } = ButtonGlyph.None;

    public string HintText { get; set; } = string.Empty;

    public int CornerRadius { get; set; } = 11;

    public bool LeftAlignText { get; set; }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _pressed = true;
            Invalidate();
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        Invalidate();
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        Invalidate();
        base.OnLostFocus(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Parent?.BackColor ?? Theme.Background);

        var bounds = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        Color fill;
        Color textColor;
        Color? border = null;
        switch (Style)
        {
            case ButtonStyle.Primary:
                fill = _pressed ? Theme.PrimaryPressed : (_hovered ? Theme.PrimaryHover : Theme.Primary);
                textColor = Color.White;
                break;
            case ButtonStyle.Ghost:
                fill = _pressed ? Theme.PrimarySoftPressed : (_hovered ? Theme.PrimarySoft : Theme.Background);
                textColor = _hovered ? Theme.Primary : Theme.TextSecondary;
                break;
            default:
                fill = _pressed ? Theme.PrimarySoftPressed : (_hovered ? Theme.PrimarySoft : Color.White);
                textColor = _hovered ? Theme.Primary : Theme.Text;
                border = _hovered ? Color.FromArgb(210, 202, 250) : Theme.Border;
                break;
        }

        if (!Enabled)
        {
            fill = Style == ButtonStyle.Primary ? Color.FromArgb(196, 189, 235) : Theme.SurfaceMuted;
            textColor = Style == ButtonStyle.Primary ? Color.FromArgb(248, 247, 255) : Theme.TextMuted;
            border = Style == ButtonStyle.Primary ? null : Theme.Border;
        }

        if (Style == ButtonStyle.Primary && Enabled && !_pressed)
        {
            Theme.FillRounded(g, new RectangleF(bounds.X, bounds.Y + 1.5f, bounds.Width, bounds.Height),
                CornerRadius, Color.FromArgb(22, 70, 50, 200));
        }

        Theme.FillRounded(g, bounds, CornerRadius, fill);
        if (border.HasValue)
        {
            Theme.DrawRoundedBorder(g, bounds, CornerRadius, border.Value, 1f);
        }

        float glyphSize = Math.Max(15f, Height * 0.42f);
        float glyphX = 16f;
        float textLeft = 16f;
        if (Glyph != ButtonGlyph.None)
        {
            var glyphRect = new RectangleF(glyphX, (Height - glyphSize) / 2f - 0.5f, glyphSize, glyphSize);
            Theme.DrawButtonGlyph(g, Glyph, glyphRect, textColor);
            textLeft = glyphX + glyphSize + 9f;
        }

        var textFlags = TextFormatFlags.SingleLine |
                        TextFormatFlags.VerticalCenter |
                        TextFormatFlags.NoPadding |
                        TextFormatFlags.EndEllipsis |
                        TextFormatFlags.NoPrefix;

        float hintWidth = 0f;
        if (!string.IsNullOrEmpty(HintText))
        {
            using var hintFont = Theme.UiFont(8.2f, FontStyle.Bold);
            Size hintSize = TextRenderer.MeasureText(HintText, hintFont, Size.Empty, TextFormatFlags.NoPadding);
            hintWidth = hintSize.Width + 14f;
            var pill = new RectangleF(Width - hintWidth - 12f, (Height - 22f) / 2f, hintWidth, 22f);
            Theme.FillRounded(g, pill, 11f, Color.FromArgb(43, 255, 255, 255));
            TextRenderer.DrawText(g, HintText, hintFont,
                Rectangle.Round(pill), Color.FromArgb(235, 255, 255, 255),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        var textRect = new Rectangle(
            (int)Math.Round(textLeft),
            0,
            Math.Max(1, Width - (int)Math.Round(textLeft) - (int)Math.Round(hintWidth) - 24),
            Height);
        if (!LeftAlignText && Glyph == ButtonGlyph.None && hintWidth <= 0f)
        {
            textRect = new Rectangle(0, 0, Width, Height);
            TextRenderer.DrawText(g, Text, Font, textRect, textColor,
                textFlags | TextFormatFlags.HorizontalCenter);
        }
        else
        {
            TextRenderer.DrawText(g, Text, Font, textRect, textColor, textFlags);
        }

        if (Focused && ShowFocusCues)
        {
            var focus = Rectangle.Inflate(ClientRectangle, -4, -4);
            ControlPaint.DrawFocusRectangle(g, focus, textColor, fill);
        }
    }
}
