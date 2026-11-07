using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace SnapText;

internal enum CaptureSource
{
    Region,
    Window
}

internal sealed class CaptureOverlayForm : Form
{
    private readonly Bitmap _screenshot;
    private readonly Rectangle _virtualBounds;
    private readonly List<WindowInfo> _windows;
    private readonly List<Rectangle> _regions = new();
    private readonly List<Bitmap> _selectedImages = new();
    private readonly Font _instructionFont = Theme.UiFont(9.6f, FontStyle.Bold);
    private readonly Font _badgeFont = Theme.UiFont(9.0f, FontStyle.Bold);
    private readonly Font _hintFont = Theme.UiFont(8.6f);
    private readonly float _scale;

    private Point _dragStart;
    private bool _mouseDown;
    private bool _dragging;
    private bool _shiftGesture;
    private bool _extractHover;
    private Rectangle _selection;
    private Rectangle _extractButtonRect;
    private WindowInfo? _hoverWindow;

    public CaptureOverlayForm(Bitmap screenshot, Rectangle virtualBounds)
    {
        _screenshot = screenshot;
        _virtualBounds = virtualBounds;
        _scale = DeviceDpi / 96f;
        _windows = EnumerateWindows(virtualBounds);

        SetStyle(ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.FromArgb(13, 15, 23);
        Bounds = virtualBounds;
        TopMost = true;
        Cursor = Cursors.Cross;
        KeyPreview = true;
        DoubleBuffered = true;
        Text = "拾字 · 截图";
    }

    public string SelectedWindowTitle { get; private set; } = string.Empty;

    public CaptureSource SelectedSource { get; private set; }

    public List<Bitmap> TakeSelectedImages()
    {
        var images = new List<Bitmap>(_selectedImages);
        _selectedImages.Clear();
        return images;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TOOLWINDOW = 0x80;
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Bounds = _virtualBounds;
        Activate();
        BringToFront();
        NativeMethods.SetForegroundWindow(Handle);
        Focus();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Right)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }

        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        if (_regions.Count > 0 && _extractButtonRect.Contains(e.Location))
        {
            CompleteMulti();
            return;
        }

        _shiftGesture = ShiftHeld;
        if (_regions.Count > 0 && !_shiftGesture)
        {
            // Multi-selection is in progress; use the button/Enter to finish.
            return;
        }

        _mouseDown = true;
        _dragging = false;
        _dragStart = e.Location;
        _selection = Rectangle.Empty;
        _hoverWindow = null;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_mouseDown)
        {
            if (!_dragging)
            {
                if (Math.Abs(e.X - _dragStart.X) >= 4 || Math.Abs(e.Y - _dragStart.Y) >= 4)
                {
                    _dragging = true;
                }
            }

            if (_dragging)
            {
                Point current = ClampToClient(e.Location);
                _selection = NormalizeRectangle(_dragStart, current);
                _hoverWindow = null;
                Invalidate();
            }

            return;
        }

        bool extractHover = _regions.Count > 0 && _extractButtonRect.Contains(e.Location);
        if (extractHover != _extractHover)
        {
            _extractHover = extractHover;
            Cursor = extractHover ? Cursors.Hand : Cursors.Cross;
            Invalidate();
        }

        WindowInfo? hit = _regions.Count == 0 ? HitTestWindow(PointToScreen(e.Location)) : null;
        if (!Equals(hit, _hoverWindow))
        {
            _hoverWindow = hit;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || !_mouseDown)
        {
            return;
        }

        bool wasDragging = _dragging;
        _mouseDown = false;

        if (wasDragging)
        {
            Rectangle selection = Rectangle.Intersect(_selection, ClientRectangle);
            if (selection.Width >= 8 && selection.Height >= 8)
            {
                if (_shiftGesture)
                {
                    AddRegion(selection);
                }
                else
                {
                    CompleteRegion(selection);
                }

                return;
            }

            _dragging = false;
            _selection = Rectangle.Empty;
            _shiftGesture = false;
            Invalidate();
            return;
        }

        if (_regions.Count > 0 && !_shiftGesture)
        {
            return;
        }

        WindowInfo? target = HitTestWindow(PointToScreen(e.Location));
        if (target is not null)
        {
            if (_shiftGesture)
            {
                AddRegion(ToLocal(target.Bounds));
            }
            else
            {
                CompleteWindow(target);
            }
        }

        _shiftGesture = false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            e.Handled = true;
            return;
        }

        if (e.KeyCode == Keys.Enter)
        {
            if (_regions.Count > 0)
            {
                CompleteMulti();
                e.Handled = true;
                return;
            }

            if (_hoverWindow is not null)
            {
                CompleteWindow(_hoverWindow);
                e.Handled = true;
                return;
            }
        }

        if ((e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete) && _regions.Count > 0)
        {
            _regions.RemoveAt(_regions.Count - 1);
            _extractHover = false;
            Invalidate();
            e.Handled = true;
            return;
        }

        if (e.KeyCode == Keys.ShiftKey)
        {
            Invalidate();
        }

        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.ShiftKey)
        {
            Invalidate();
        }

        base.OnKeyUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        if (ClientSize.Width != _screenshot.Width || ClientSize.Height != _screenshot.Height)
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.DrawImage(_screenshot, ClientRectangle);
        }
        else
        {
            g.DrawImageUnscaled(_screenshot, 0, 0);
        }

        using (var dim = new SolidBrush(Color.FromArgb(116, 10, 12, 20)))
        {
            g.FillRectangle(dim, ClientRectangle);
        }

        foreach (Rectangle region in _regions)
        {
            Reveal(g, Rectangle.Intersect(region, ClientRectangle));
        }

        if (_dragging && _selection.Width > 1 && _selection.Height > 1)
        {
            Reveal(g, Rectangle.Intersect(_selection, ClientRectangle));
        }

        if (_regions.Count == 0 && !_dragging && _hoverWindow is not null)
        {
            Rectangle local = Rectangle.Intersect(ToLocal(_hoverWindow.Bounds), ClientRectangle);
            Reveal(g, local);
            DrawSelectionDecoration(g, local, _hoverWindow);
            DrawWindowLabel(g, local, _hoverWindow);
        }

        for (int i = 0; i < _regions.Count; i++)
        {
            Rectangle region = Rectangle.Intersect(_regions[i], ClientRectangle);
            if (region.Width > 1 && region.Height > 1)
            {
                DrawExistingRegion(g, region, i + 1);
            }
        }

        if (_dragging && _selection.Width > 1 && _selection.Height > 1)
        {
            DrawDraftRegion(g, Rectangle.Intersect(_selection, ClientRectangle));
            DrawSizeLabel(g, _selection);
        }

        DrawInstruction(g);
        DrawMultiToolbar(g);
        DrawPrivacyHint(g);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _screenshot.Dispose();
            foreach (Bitmap image in _selectedImages)
            {
                image.Dispose();
            }

            _selectedImages.Clear();
            _instructionFont.Dispose();
            _badgeFont.Dispose();
            _hintFont.Dispose();
        }

        base.Dispose(disposing);
    }

    private bool ShiftHeld => (ModifierKeys & Keys.Shift) == Keys.Shift;

    private void Reveal(Graphics g, Rectangle local)
    {
        if (local.Width > 0 && local.Height > 0)
        {
            g.DrawImage(_screenshot, local, local, GraphicsUnit.Pixel);
        }
    }

    private void DrawExistingRegion(Graphics g, Rectangle rect, int number)
    {
        Color accent = Color.FromArgb(255, 124, 92, 255);
        using (var whitePen = new Pen(Color.FromArgb(130, 255, 255, 255), 1f))
        {
            g.DrawRectangle(whitePen, rect.X - 1, rect.Y - 1, rect.Width + 2, rect.Height + 2);
        }

        using (var pen = new Pen(accent, 2f))
        {
            g.DrawRectangle(pen, rect);
        }

        int badge = Math.Max(22, (int)Math.Round(24 * _scale));
        int bx = Math.Max(4, rect.Left + (int)Math.Round(5 * _scale));
        int by = Math.Max(4, rect.Top + (int)Math.Round(5 * _scale));
        var badgeRect = new Rectangle(bx, by, badge, badge);
        using (var fill = new SolidBrush(accent))
        {
            g.FillEllipse(fill, badgeRect);
        }

        using (var font = Theme.UiFont(9.2f, FontStyle.Bold))
        {
            TextRenderer.DrawText(g, number.ToString(), font, badgeRect, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    private void DrawDraftRegion(Graphics g, Rectangle rect)
    {
        Color accent = Color.FromArgb(255, 34, 211, 238);
        using (var whitePen = new Pen(Color.FromArgb(150, 255, 255, 255), 1f))
        {
            g.DrawRectangle(whitePen, rect.X - 1, rect.Y - 1, rect.Width + 2, rect.Height + 2);
        }

        using (var pen = new Pen(accent, 2f) { DashStyle = DashStyle.Dash })
        {
            g.DrawRectangle(pen, rect);
        }

        const int handle = 7;
        using var fill = new SolidBrush(Color.White);
        using var border = new Pen(accent, 1.5f);
        foreach (Point p in new[]
                 {
                     new Point(rect.Left, rect.Top),
                     new Point(rect.Right, rect.Top),
                     new Point(rect.Left, rect.Bottom),
                     new Point(rect.Right, rect.Bottom)
                 })
        {
            var hr = new Rectangle(p.X - handle / 2, p.Y - handle / 2, handle, handle);
            g.FillRectangle(fill, hr);
            g.DrawRectangle(border, hr);
        }
    }

    private void DrawSelectionDecoration(Graphics g, Rectangle rect, WindowInfo? window)
    {
        Color accent = window is null ? Color.FromArgb(255, 124, 92, 255) : Color.FromArgb(255, 34, 211, 238);
        var white = Color.FromArgb(150, 255, 255, 255);
        using (var whitePen = new Pen(white, 1f))
        {
            g.DrawRectangle(whitePen, rect.X - 1, rect.Y - 1, rect.Width + 2, rect.Height + 2);
        }

        using (var pen = new Pen(accent, 2f))
        {
            g.DrawRectangle(pen, rect);
        }

        const int handle = 7;
        using var fill = new SolidBrush(Color.White);
        using var border = new Pen(accent, 1.5f);
        foreach (Point p in new[]
                 {
                     new Point(rect.Left, rect.Top),
                     new Point(rect.Right, rect.Top),
                     new Point(rect.Left, rect.Bottom),
                     new Point(rect.Right, rect.Bottom)
                 })
        {
            var hr = new Rectangle(p.X - handle / 2, p.Y - handle / 2, handle, handle);
            g.FillRectangle(fill, hr);
            g.DrawRectangle(border, hr);
        }
    }

    private void DrawInstruction(Graphics g)
    {
        string text = _regions.Count > 0
            ? $"已选 {_regions.Count} 个区域    ·    Shift+拖动 继续添加    ·    Enter 提取    ·    Esc 取消"
            : "拖动框选    ·    Shift+拖动 多选    ·    单击窗口    ·    Esc 取消";

        Size textSize = TextRenderer.MeasureText(text, _instructionFont, Size.Empty, TextFormatFlags.NoPadding);
        int height = (int)Math.Round(40 * _scale);
        int width = textSize.Width + (int)Math.Round(76 * _scale);
        int x = (ClientSize.Width - width) / 2;
        int y = (int)Math.Round(20 * _scale);
        var pill = new RectangleF(x, y, width, height);
        Theme.FillRounded(g, pill, height / 2f, Color.FromArgb(225, 24, 27, 38));
        Theme.DrawRoundedBorder(g, pill, height / 2f, Color.FromArgb(50, 255, 255, 255), 1f);

        float cx = x + 24 * _scale;
        float cy = y + height / 2f;
        using (var pen = new Pen(Color.FromArgb(235, 255, 255, 255), Math.Max(1.2f, 1.4f * _scale)))
        {
            float arm = 5 * _scale;
            g.DrawLine(pen, cx - arm, cy, cx + arm, cy);
            g.DrawLine(pen, cx, cy - arm, cx, cy + arm);
            g.DrawEllipse(pen, cx - 3.2f * _scale, cy - 3.2f * _scale, 6.4f * _scale, 6.4f * _scale);
        }

        TextRenderer.DrawText(g, text, _instructionFont,
            new Rectangle(x + (int)Math.Round(42 * _scale), y, width, height),
            Color.FromArgb(248, 250, 255),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private void DrawMultiToolbar(Graphics g)
    {
        if (_regions.Count == 0)
        {
            _extractButtonRect = Rectangle.Empty;
            return;
        }

        string text = $"提取 {_regions.Count} 个区域";
        string hint = "Enter";
        Size textSize = TextRenderer.MeasureText(text, _instructionFont, Size.Empty, TextFormatFlags.NoPadding);
        Size hintSize = TextRenderer.MeasureText(hint, _badgeFont, Size.Empty, TextFormatFlags.NoPadding);
        int pad = (int)Math.Round(18 * _scale);
        int gap = (int)Math.Round(14 * _scale);
        int height = (int)Math.Round(42 * _scale);
        int width = pad * 2 + textSize.Width + gap + hintSize.Width + (int)Math.Round(16 * _scale);
        int x = (ClientSize.Width - width) / 2;
        int y = ClientSize.Height - (int)Math.Round(92 * _scale);
        var pill = new RectangleF(x, y, width, height);
        Color fill = _extractHover ? Theme.PrimaryHover : Theme.Primary;
        Theme.FillRounded(g, pill, height / 2f, fill);
        Theme.DrawRoundedBorder(g, pill, height / 2f, Color.FromArgb(90, 255, 255, 255), 1f);
        _extractButtonRect = Rectangle.Round(pill);

        TextRenderer.DrawText(g, text, _instructionFont,
            new Rectangle(x + pad, y, textSize.Width + 2, height), Color.White,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        int hintX = x + pad + textSize.Width + gap;
        var hintRect = new Rectangle(hintX, y + (height - (int)Math.Round(23 * _scale)) / 2,
            hintSize.Width + (int)Math.Round(14 * _scale), (int)Math.Round(23 * _scale));
        Theme.FillRounded(g, hintRect, hintRect.Height / 2f, Color.FromArgb(45, 255, 255, 255));
        TextRenderer.DrawText(g, hint, _badgeFont, hintRect, Color.FromArgb(245, 255, 255, 255),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        string help = "按框选顺序识别 · Backspace 撤销上一框";
        Size helpSize = TextRenderer.MeasureText(help, _hintFont, Size.Empty, TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, help, _hintFont,
            new Rectangle((ClientSize.Width - helpSize.Width) / 2, y - (int)Math.Round(24 * _scale),
                helpSize.Width, (int)Math.Round(20 * _scale)),
            Color.FromArgb(175, 245, 248, 255),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private void DrawPrivacyHint(Graphics g)
    {
        string text = "拾字在本地识别，截图不会上传";
        Size size = TextRenderer.MeasureText(text, _hintFont, Size.Empty, TextFormatFlags.NoPadding);
        int y = ClientSize.Height - (int)Math.Round(34 * _scale) - size.Height;
        var rect = new Rectangle((ClientSize.Width - size.Width) / 2, y, size.Width, size.Height);
        TextRenderer.DrawText(g, text, _hintFont, rect, Color.FromArgb(145, 255, 255, 255),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private void DrawSizeLabel(Graphics g, Rectangle rect)
    {
        string text = $"{rect.Width} × {rect.Height}";
        Size size = TextRenderer.MeasureText(text, _badgeFont, Size.Empty, TextFormatFlags.NoPadding);
        int padX = (int)Math.Round(12 * _scale);
        int h = (int)Math.Round(27 * _scale);
        int w = size.Width + padX * 2;
        int x = rect.Right - w;
        int y = rect.Bottom + (int)Math.Round(8 * _scale);
        if (y + h > ClientSize.Height - (int)Math.Round(42 * _scale))
        {
            y = rect.Top - h - (int)Math.Round(8 * _scale);
        }

        x = Math.Max((int)Math.Round(8 * _scale), Math.Min(x, ClientSize.Width - w - (int)Math.Round(8 * _scale)));
        y = Math.Max((int)Math.Round(8 * _scale), y);

        var pill = new RectangleF(x, y, w, h);
        Theme.FillRounded(g, pill, h / 2f, Color.FromArgb(232, 24, 27, 38));
        TextRenderer.DrawText(g, text, _badgeFont, Rectangle.Round(pill),
            Color.FromArgb(245, 250, 252, 255),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private void DrawWindowLabel(Graphics g, Rectangle rect, WindowInfo window)
    {
        string title = window.Title;
        if (string.IsNullOrWhiteSpace(title))
        {
            title = window.ProcessName;
        }

        string prefix = "单击提取窗口 · ";
        string text = prefix + title;
        Size fullSize = TextRenderer.MeasureText(text, _badgeFont, Size.Empty, TextFormatFlags.NoPadding);
        int maxWidth = Math.Min(ClientSize.Width - (int)Math.Round(48 * _scale), (int)Math.Round(620 * _scale));
        int padX = (int)Math.Round(13 * _scale);
        int h = (int)Math.Round(32 * _scale);
        int w = Math.Min(fullSize.Width + padX * 2, maxWidth);
        int x = rect.Left;
        int y = rect.Top - h - (int)Math.Round(8 * _scale);
        if (y < (int)Math.Round(70 * _scale))
        {
            y = rect.Top + (int)Math.Round(8 * _scale);
        }

        x = Math.Max((int)Math.Round(8 * _scale), Math.Min(x, ClientSize.Width - w - (int)Math.Round(8 * _scale)));
        var pill = new RectangleF(x, y, w, h);
        Theme.FillRounded(g, pill, h / 2f, Color.FromArgb(235, 24, 27, 38));
        Theme.DrawRoundedBorder(g, pill, h / 2f, Color.FromArgb(80, 34, 211, 238), 1f);

        var textRect = new Rectangle(x + padX, y, w - padX * 2, h);
        TextRenderer.DrawText(g, text, _badgeFont, textRect, Color.FromArgb(248, 252, 255),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }

    private void AddRegion(Rectangle localRect)
    {
        Rectangle region = Rectangle.Intersect(localRect, new Rectangle(0, 0, _screenshot.Width, _screenshot.Height));
        if (region.Width < 8 || region.Height < 8)
        {
            return;
        }

        _regions.Add(region);
        _selection = Rectangle.Empty;
        _dragging = false;
        _shiftGesture = false;
        _hoverWindow = null;
        _extractHover = false;
        Cursor = Cursors.Cross;
        Invalidate();
    }

    private void CompleteRegion(Rectangle localRect)
    {
        Rectangle source = Rectangle.Intersect(localRect, new Rectangle(0, 0, _screenshot.Width, _screenshot.Height));
        if (source.Width < 2 || source.Height < 2)
        {
            return;
        }

        _selectedImages.Add(_screenshot.Clone(source, PixelFormat.Format32bppArgb));
        SelectedSource = CaptureSource.Region;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void CompleteWindow(WindowInfo window)
    {
        Rectangle local = ToLocal(window.Bounds);
        Rectangle source = Rectangle.Intersect(local, new Rectangle(0, 0, _screenshot.Width, _screenshot.Height));
        if (source.Width < 7 || source.Height < 7)
        {
            return;
        }

        _selectedImages.Add(_screenshot.Clone(source, PixelFormat.Format32bppArgb));
        SelectedWindowTitle = window.Title;
        SelectedSource = CaptureSource.Window;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void CompleteMulti()
    {
        if (_regions.Count == 0)
        {
            return;
        }

        foreach (Rectangle region in _regions)
        {
            Rectangle source = Rectangle.Intersect(region, new Rectangle(0, 0, _screenshot.Width, _screenshot.Height));
            if (source.Width >= 8 && source.Height >= 8)
            {
                _selectedImages.Add(_screenshot.Clone(source, PixelFormat.Format32bppArgb));
            }
        }

        if (_selectedImages.Count == 0)
        {
            return;
        }

        SelectedSource = CaptureSource.Region;
        SelectedWindowTitle = string.Empty;
        DialogResult = DialogResult.OK;
        Close();
    }

    private Rectangle ToLocal(Rectangle screenRect)
    {
        return new Rectangle(screenRect.X - _virtualBounds.X, screenRect.Y - _virtualBounds.Y,
            screenRect.Width, screenRect.Height);
    }

    private WindowInfo? HitTestWindow(Point screenPoint)
    {
        foreach (WindowInfo window in _windows)
        {
            if (window.Bounds.Contains(screenPoint))
            {
                return window;
            }
        }

        return null;
    }

    private Point ClampToClient(Point point)
    {
        return new Point(
            Math.Max(0, Math.Min(point.X, ClientSize.Width)),
            Math.Max(0, Math.Min(point.Y, ClientSize.Height)));
    }

    private static Rectangle NormalizeRectangle(Point a, Point b)
    {
        int left = Math.Min(a.X, b.X);
        int top = Math.Min(a.Y, b.Y);
        int right = Math.Max(a.X, b.X);
        int bottom = Math.Max(a.Y, b.Y);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    private static List<WindowInfo> EnumerateWindows(Rectangle virtualBounds)
    {
        var result = new List<WindowInfo>();
        uint ownPid = (uint)Environment.ProcessId;
        var processNames = new Dictionary<uint, string>();
        var excludedClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Progman",
            "WorkerW",
            "Shell_TrayWnd",
            "Shell_SecondaryTrayWnd",
            "NotifyIconOverflowWindow",
            "Windows.UI.Core.CoreWindow",
            "ApplicationManager_ImmersiveShellWindow",
            "XamlExplorerHostIslandWindow",
            "MultitaskingViewFrame",
            "TaskListThumbnailWnd",
            "ForegroundStaging",
            "InputSiteWindowClass",
            "MSCTFIME UI",
            "IME",
            "tooltips_class32",
            "#32768",
            "SysShadow",
            "Taskbar"
        };

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (hwnd == IntPtr.Zero ||
                !NativeMethods.IsWindow(hwnd) ||
                !NativeMethods.IsWindowVisible(hwnd) ||
                NativeMethods.IsIconic(hwnd))
            {
                return true;
            }

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == ownPid)
            {
                return true;
            }

            long exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0 ||
                (exStyle & NativeMethods.WS_EX_TRANSPARENT) != 0)
            {
                return true;
            }

            if (NativeMethods.IsCloaked(hwnd))
            {
                return true;
            }

            var classBuilder = new System.Text.StringBuilder(256);
            NativeMethods.GetClassName(hwnd, classBuilder, classBuilder.Capacity);
            string className = classBuilder.ToString();
            if (excludedClasses.Contains(className))
            {
                return true;
            }

            Rectangle bounds = NativeMethods.GetExtendedFrameBounds(hwnd);
            if (bounds.Width < 32 || bounds.Height < 24)
            {
                return true;
            }

            if (!bounds.IntersectsWith(virtualBounds))
            {
                return true;
            }

            var titleBuilder = new System.Text.StringBuilder(512);
            NativeMethods.GetWindowText(hwnd, titleBuilder, titleBuilder.Capacity);
            string title = titleBuilder.ToString().Trim();

            if (!processNames.TryGetValue(pid, out string? processName))
            {
                try
                {
                    processName = Process.GetProcessById((int)pid).ProcessName + ".exe";
                }
                catch
                {
                    processName = "窗口";
                }

                processNames[pid] = processName;
            }

            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(className))
            {
                return true;
            }

            result.Add(new WindowInfo(hwnd, bounds, title, processName, className));
            return true;
        }, IntPtr.Zero);

        return result;
    }

    private sealed record WindowInfo(IntPtr Handle, Rectangle Bounds, string Title, string ProcessName, string ClassName);
}
