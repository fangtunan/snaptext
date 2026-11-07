using System.Drawing.Drawing2D;

namespace SnapText.Controls;

internal sealed class CardPanel : Panel
{
    private readonly System.Windows.Forms.Timer _busyTimer;
    private bool _busy;
    private bool _highlighted;
    private float _phase;

    public CardPanel()
    {
        SetStyle(ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Theme.Background;
        _busyTimer = new System.Windows.Forms.Timer { Interval = 24 };
        _busyTimer.Tick += (_, _) =>
        {
            _phase += 0.018f;
            if (_phase > 1.35f)
            {
                _phase = -0.2f;
            }

            Invalidate();
        };
    }

    public int CornerRadius { get; set; } = 15;

    public bool Busy
    {
        get => _busy;
        set
        {
            if (_busy == value)
            {
                return;
            }

            _busy = value;
            _phase = -0.15f;
            if (_busy)
            {
                _busyTimer.Start();
            }
            else
            {
                _busyTimer.Stop();
            }

            Invalidate();
        }
    }

    public bool Highlighted
    {
        get => _highlighted;
        set
        {
            if (_highlighted == value)
            {
                return;
            }

            _highlighted = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Background);

        var shadow1 = new RectangleF(1.5f, 3f, Width - 3f, Height - 4f);
        Theme.FillRounded(g, shadow1, CornerRadius, Color.FromArgb(9, 37, 42, 70));
        var shadow2 = new RectangleF(3.5f, 5f, Width - 7f, Height - 7f);
        Theme.FillRounded(g, shadow2, CornerRadius, Color.FromArgb(6, 37, 42, 70));

        var bounds = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1.5f);
        Theme.FillRounded(g, bounds, CornerRadius, Theme.Surface);

        Color border = _highlighted ? Theme.Primary : Theme.Border;
        float borderWidth = _highlighted ? 1.8f : 1f;
        Theme.DrawRoundedBorder(g, bounds, CornerRadius, border, borderWidth);

        if (_busy)
        {
            float lineY = Height - 4f;
            float lineX = CornerRadius + 4f;
            float lineW = Math.Max(1f, Width - (CornerRadius + 4f) * 2f);
            using var track = new Pen(Color.FromArgb(239, 236, 255), 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(track, lineX, lineY, lineX + lineW, lineY);
            float segW = Math.Max(46f, lineW * 0.22f);
            float segX = lineX + (lineW + segW) * _phase - segW;
            if (segX < lineX + lineW && segX + segW > lineX)
            {
                float left = Math.Max(lineX, segX);
                float right = Math.Min(lineX + lineW, segX + segW);
                using var pen = new Pen(Theme.Primary, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLine(pen, left, lineY, right, lineY);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _busyTimer.Dispose();
        }

        base.Dispose(disposing);
    }
}
