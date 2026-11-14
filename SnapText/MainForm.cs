using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using SnapText.Controls;

namespace SnapText;

internal sealed class MainForm : Form
{
    private const int HotkeyId = 0x4A21;
    private const string DefaultEmptyHint = "识别结果将显示在这里\n\n按 {0} 截图，或打开 / 拖入一张图片";
    private static readonly (uint Modifiers, Keys Key, string Label)[] HotkeyCandidates =
    {
        (NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT, Keys.S, "Ctrl+Alt+S"),
        (NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT, Keys.Q, "Ctrl+Shift+Q"),
        (NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT, Keys.S, "Alt+Shift+S")
    };

    private readonly AppSettings _settings;
    private readonly Icon _appIcon;
    private readonly HeaderPanel _header;
    private readonly Panel _content;
    private readonly Panel _actions;
    private readonly Panel _bottom;
    private readonly RoundedButton _captureButton;
    private readonly RoundedButton _openButton;
    private readonly RoundedButton _pasteButton;
    private readonly RoundedButton _copyButton;
    private readonly RoundedButton _clearButton;
    private readonly CheckBox _accurateCheck;
    private readonly CheckBox _translateCheck;
    private readonly Label _languageLabel;
    private readonly ComboBox _languageCombo;
    private readonly CardPanel _card;
    private readonly Label _resultTitle;
    private readonly Label _sourceLabel;
    private readonly Label _charCount;
    private readonly Label _placeholder;
    private readonly Panel _divider;
    private readonly TextBox _resultBox;
    private readonly Label _translationTitle;
    private readonly TextBox _translationBox;
    private readonly Label _translationPlaceholder;
    private readonly Panel _verticalDivider;
    private readonly StatusDot _statusDot;
    private readonly Label _statusLabel;
    private readonly Label _metaLabel;
    private readonly ToolTip _toolTip;
    private readonly System.Windows.Forms.Timer _toastTimer;
    private readonly string? _initialImagePath;

    private NotifyIcon? _tray;
    private ToolStripMenuItem? _trayCaptureItem;
    private ContextMenuStrip? _trayMenu;
    private readonly List<(int Id, string Label)> _registeredHotkeys = new();
    private bool _captureInProgress;
    private bool _closing;
    private bool _exitRequested;
    private bool _trayHintShown;
    private bool _initialSized;
    private bool _suppressModeChange;
    private bool _suppressQualityChange;
    private bool _suppressTranslateChange;
    private string _activeHotkey = "Ctrl+Alt+S";
    private string _emptyHint = string.Empty;
    private string _baseStatusText = "就绪";
    private Color _baseStatusColor = Theme.TextMuted;
    private string _translationHint = "译文将在这里显示";
    private bool _translationSucceeded;
    private double _opacityBeforeCapture = 1d;
    private readonly List<Bitmap> _lastImages = new();
    private int _ocrGeneration;
    private CancellationTokenSource? _ocrCts;

    public MainForm(string? initialImagePath = null)
    {
        _initialImagePath = initialImagePath;
        _settings = AppSettings.Load();
        _appIcon = Theme.LoadAppIcon();

        SuspendLayout();
        AutoScaleMode = AutoScaleMode.None;
        Text = "拾字 · SnapText";
        Icon = _appIcon;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.UiFont(10f);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(880, 690);
        MinimumSize = new Size(810, 590);
        DoubleBuffered = true;
        KeyPreview = true;
        AllowDrop = true;

        _header = new HeaderPanel { Dock = DockStyle.Top };
        _content = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Background,
            Padding = new Padding(24, 20, 24, 18)
        };
        Controls.Add(_content);
        Controls.Add(_header);

        _actions = new Panel { BackColor = Theme.Background };
        _content.Controls.Add(_actions);

        _captureButton = new RoundedButton
        {
            Text = "截图取字",
            Glyph = ButtonGlyph.Crop,
            Style = ButtonStyle.Primary,
            LeftAlignText = true,
            HintText = _activeHotkey,
            CornerRadius = 11
        };
        _openButton = new RoundedButton
        {
            Text = "打开图片",
            Glyph = ButtonGlyph.Image,
            Style = ButtonStyle.Secondary
        };
        _pasteButton = new RoundedButton
        {
            Text = "从剪贴板",
            Glyph = ButtonGlyph.Paste,
            Style = ButtonStyle.Secondary
        };
        _actions.Controls.Add(_captureButton);
        _actions.Controls.Add(_openButton);
        _actions.Controls.Add(_pasteButton);

        _languageLabel = new Label
        {
            Text = "识别语言",
            AutoSize = true,
            ForeColor = Theme.TextSecondary,
            BackColor = Color.Transparent,
            Font = Theme.UiFont(9.2f)
        };
        _languageCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Theme.Text,
            Font = Theme.UiFont(9.5f),
            IntegralHeight = false,
            MaxDropDownItems = 6
        };
        _languageCombo.Items.Add(new OcrModeItem("自动（中 + 英）", OcrMode.Auto));
        _languageCombo.Items.Add(new OcrModeItem("中文（含英文）", OcrMode.ChineseEnglish));
        _languageCombo.Items.Add(new OcrModeItem("English", OcrMode.English));
        _actions.Controls.Add(_languageLabel);
        _actions.Controls.Add(_languageCombo);

        _card = new CardPanel { BackColor = Theme.Background };
        _content.Controls.Add(_card);

        _resultTitle = new Label
        {
            Text = "识别结果",
            AutoSize = true,
            Font = Theme.UiFont(11.5f, FontStyle.Bold),
            ForeColor = Theme.Text,
            BackColor = Color.White
        };
        _sourceLabel = new Label
        {
            Text = string.Empty,
            AutoSize = false,
            AutoEllipsis = true,
            Font = Theme.UiFont(9f),
            ForeColor = Theme.TextMuted,
            BackColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft
        };
        _charCount = new Label
        {
            Text = string.Empty,
            AutoSize = false,
            Font = Theme.UiFont(9f),
            ForeColor = Theme.TextMuted,
            BackColor = Color.White,
            TextAlign = ContentAlignment.MiddleRight
        };
        _divider = new Panel { BackColor = Theme.Border };
        _resultBox = new TextBox
        {
            Multiline = true,
            WordWrap = true,
            AcceptsReturn = true,
            AcceptsTab = false,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.None,
            BackColor = Color.White,
            ForeColor = Theme.Text,
            Font = Theme.UiFont(11f),
            HideSelection = false,
            ShortcutsEnabled = true
        };
        _placeholder = new Label
        {
            AutoSize = false,
            BackColor = Color.White,
            ForeColor = Theme.TextMuted,
            Font = Theme.UiFont(10.2f),
            TextAlign = ContentAlignment.TopLeft,
            UseMnemonic = false,
            Cursor = Cursors.IBeam
        };
        _translationTitle = new Label
        {
            Text = "译文",
            AutoSize = false,
            Font = Theme.UiFont(11.5f, FontStyle.Bold),
            ForeColor = Theme.Text,
            BackColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft,
            Visible = false
        };
        _translationBox = new TextBox
        {
            Multiline = true,
            WordWrap = true,
            AcceptsReturn = true,
            AcceptsTab = false,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.None,
            BackColor = Color.White,
            ForeColor = Theme.Text,
            Font = Theme.UiFont(11f),
            HideSelection = false,
            ShortcutsEnabled = true,
            ReadOnly = true,
            Visible = false
        };
        _translationPlaceholder = new Label
        {
            Text = _translationHint,
            AutoSize = false,
            BackColor = Color.White,
            ForeColor = Theme.TextMuted,
            Font = Theme.UiFont(10.2f),
            TextAlign = ContentAlignment.TopLeft,
            UseMnemonic = false,
            Cursor = Cursors.IBeam,
            Visible = false
        };
        _verticalDivider = new Panel { BackColor = Theme.Border, Visible = false };
        _card.Controls.Add(_resultBox);
        _card.Controls.Add(_translationBox);
        _card.Controls.Add(_placeholder);
        _card.Controls.Add(_translationPlaceholder);
        _card.Controls.Add(_divider);
        _card.Controls.Add(_verticalDivider);
        _card.Controls.Add(_resultTitle);
        _card.Controls.Add(_sourceLabel);
        _card.Controls.Add(_charCount);
        _card.Controls.Add(_translationTitle);
        _placeholder.BringToFront();
        _translationPlaceholder.BringToFront();

        _bottom = new Panel { BackColor = Theme.Background };
        _content.Controls.Add(_bottom);
        _copyButton = new RoundedButton
        {
            Text = "复制全文",
            Glyph = ButtonGlyph.Copy,
            Style = ButtonStyle.Primary,
            Font = Theme.UiFont(9.8f, FontStyle.Bold),
            CornerRadius = 10,
            Enabled = false
        };
        _clearButton = new RoundedButton
        {
            Text = "清空",
            Glyph = ButtonGlyph.Trash,
            Style = ButtonStyle.Ghost,
            Font = Theme.UiFont(9.8f, FontStyle.Bold),
            CornerRadius = 10,
            Enabled = false
        };
        _accurateCheck = new CheckBox
        {
            Text = "高精度",
            AutoSize = false,
            Font = Theme.UiFont(9f),
            ForeColor = Theme.TextSecondary,
            BackColor = Theme.Background,
            FlatStyle = FlatStyle.Flat,
            TextAlign = ContentAlignment.MiddleLeft,
            Checked = _settings.AccurateOcr && OcrRouter.AccurateAvailable,
            Enabled = OcrRouter.AccurateAvailable
        };
        _translateCheck = new CheckBox
        {
            Text = "翻译",
            AutoSize = false,
            Font = Theme.UiFont(9f),
            ForeColor = Theme.TextSecondary,
            BackColor = Theme.Background,
            FlatStyle = FlatStyle.Flat,
            TextAlign = ContentAlignment.MiddleLeft,
            Checked = _settings.TranslateEnabled && _settings.TranslationDisclosureAccepted
        };
        _statusDot = new StatusDot { Size = new Size(12, 12) };
        _statusLabel = new Label
        {
            Text = "就绪",
            AutoSize = false,
            AutoEllipsis = true,
            Font = Theme.UiFont(9.2f),
            ForeColor = Theme.TextSecondary,
            BackColor = Theme.Background,
            TextAlign = ContentAlignment.MiddleLeft
        };
        _metaLabel = new Label
        {
            Text = "本地 OCR · 无需联网",
            AutoSize = false,
            AutoEllipsis = true,
            Font = Theme.UiFont(9f),
            ForeColor = Theme.TextMuted,
            BackColor = Theme.Background,
            TextAlign = ContentAlignment.MiddleRight
        };
        _bottom.Controls.Add(_copyButton);
        _bottom.Controls.Add(_clearButton);
        _bottom.Controls.Add(_accurateCheck);
        _bottom.Controls.Add(_translateCheck);
        _bottom.Controls.Add(_statusDot);
        _bottom.Controls.Add(_statusLabel);
        _bottom.Controls.Add(_metaLabel);

        _toolTip = new ToolTip
        {
            AutoPopDelay = 6000,
            InitialDelay = 350,
            ReshowDelay = 100,
            ShowAlways = true
        };
        _toolTip.SetToolTip(_captureButton, "从屏幕上框选区域或选择窗口");
        _toolTip.SetToolTip(_openButton, "打开一张图片并识别（Ctrl+O）");
        _toolTip.SetToolTip(_pasteButton, "识别剪贴板中的图片");
        _toolTip.SetToolTip(_copyButton, "复制当前结果（Ctrl+C）");
        _toolTip.SetToolTip(_clearButton, "清空当前结果");
        _toolTip.SetToolTip(_accurateCheck, OcrRouter.AccurateAvailable
            ? "使用 PP-OCRv5 本地高精度模型；首次识别会稍慢，关闭则使用 Windows OCR 快速模式。"
            : "未找到高精度模型，当前只能使用 Windows OCR。");
        _toolTip.SetToolTip(_translateCheck,
            "自动判断中英文方向并在线翻译；识别文字会发送到 MyMemory，图片不会上传。");

        _toastTimer = new System.Windows.Forms.Timer { Interval = 2600 };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            SetStatus(_baseStatusText, _baseStatusColor);
        };

        BuildTrayIcon();
        RestoreModeSelection();
        ApplyTranslationChrome();
        _emptyHint = BuildDefaultEmptyHint();
        UpdatePlaceholder();

        _content.Resize += (_, _) => LayoutContent();
        _card.Resize += (_, _) => LayoutCard();
        _actions.Resize += (_, _) => LayoutActions();
        _bottom.Resize += (_, _) => LayoutBottom();

        _captureButton.Click += async (_, _) => await BeginCaptureAsync();
        _openButton.Click += (_, _) => OpenImageDialog();
        _pasteButton.Click += (_, _) => PasteFromClipboard();
        _copyButton.Click += (_, _) => CopyResult();
        _clearButton.Click += (_, _) => ClearResult();
        _languageCombo.SelectedIndexChanged += (_, _) => OnLanguageChanged();
        _accurateCheck.CheckedChanged += (_, _) => OnQualityChanged();
        _translateCheck.CheckedChanged += (_, _) => OnTranslateChanged();
        _resultBox.TextChanged += (_, _) => OnResultTextChanged();
        _translationBox.TextChanged += (_, _) => OnResultTextChanged();
        _placeholder.Click += (_, _) => _resultBox.Focus();
        _translationPlaceholder.Click += (_, _) => _translationBox.Focus();
        FormClosing += OnFormClosing;
        Shown += OnFormShown;
        KeyDown += OnFormKeyDown;

        EnableImageDrop(this);
        LayoutAll();
        ResumeLayout(true);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RegisterHotkeys();

        if (!_initialSized)
        {
            _initialSized = true;
            int width = Math.Max(810, Math.Min(_settings.WindowWidth, 1600));
            int height = Math.Max(590, Math.Min(_settings.WindowHeight, 1200));
            _header.Height = S(108);
            _content.Padding = new Padding(S(24), S(20), S(24), S(18));
            ClientSize = new Size(S(width), S(height));
            MinimumSize = new Size(S(810), S(590));

            Rectangle working = Screen.FromControl(this).WorkingArea;
            if (Width > working.Width || Height > working.Height)
            {
                ClientSize = new Size(Math.Min(ClientSize.Width, working.Width - S(24)),
                    Math.Min(ClientSize.Height, working.Height - S(24)));
            }

            CenterToScreen();
            LayoutAll();
        }
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        UnregisterHotkeys();
        base.OnHandleDestroyed(e);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        _header.Height = S(108);
        _content.Padding = new Padding(S(24), S(20), S(24), S(18));
        MinimumSize = new Size(S(810), S(590));
        LayoutAll();
        Invalidate(true);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_HOTKEY)
        {
            int hotkeyId = m.WParam.ToInt32();
            if (_registeredHotkeys.Any(hotkey => hotkey.Id == hotkeyId))
            {
                _ = BeginCaptureAsync();
                return;
            }
        }

        if (NativeMethods.ShowWindowMessage != 0 && (uint)m.Msg == NativeMethods.ShowWindowMessage)
        {
            if (!_captureInProgress)
            {
                RestoreMainWindow();
            }

            return;
        }

        base.WndProc(ref m);
    }

    private void BuildTrayIcon()
    {
        _trayMenu = new ContextMenuStrip
        {
            Font = Theme.UiFont(9.5f),
            ShowImageMargin = false
        };
        _trayCaptureItem = new ToolStripMenuItem("截图取字");
        _trayCaptureItem.ShortcutKeyDisplayString = _activeHotkey;
        _trayCaptureItem.Click += async (_, _) => await BeginCaptureAsync();
        ToolStripMenuItem showItem = new("显示主窗口");
        showItem.Click += (_, _) => RestoreMainWindow();
        ToolStripMenuItem exitItem = new("退出拾字");
        exitItem.Click += (_, _) =>
        {
            _exitRequested = true;
            Close();
        };
        _trayMenu.Items.Add(_trayCaptureItem);
        _trayMenu.Items.Add(showItem);
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add(exitItem);

        _tray = new NotifyIcon
        {
            Icon = _appIcon,
            Text = "拾字 · 截图取字",
            Visible = true,
            ContextMenuStrip = _trayMenu
        };
        _tray.DoubleClick += (_, _) => RestoreMainWindow();
    }

    private void RestoreModeSelection()
    {        OcrMode mode = OcrMode.Auto;
        if (Enum.TryParse(_settings.OcrMode, ignoreCase: true, out OcrMode saved))
        {
            mode = saved;
        }

        _suppressModeChange = true;
        try
        {
            for (int i = 0; i < _languageCombo.Items.Count; i++)
            {
                if (_languageCombo.Items[i] is OcrModeItem item && item.Mode == mode)
                {
                    _languageCombo.SelectedIndex = i;
                    break;
                }
            }

            if (_languageCombo.SelectedIndex < 0)
            {
                _languageCombo.SelectedIndex = 0;
            }
        }
        finally
        {
            _suppressModeChange = false;
        }
    }

    private OcrMode SelectedMode
    {
        get
        {
            if (_languageCombo.SelectedItem is OcrModeItem item)
            {
                return item.Mode;
            }

            return OcrMode.Auto;
        }
    }

    private OcrQuality SelectedQuality => _accurateCheck.Checked
        ? OcrQuality.Accurate
        : OcrQuality.Fast;

    private void OnQualityChanged()
    {
        if (_suppressQualityChange)
        {
            return;
        }

        _settings.AccurateOcr = _accurateCheck.Checked;
        _settings.Save();

        if (_lastImages.Count > 0)
        {
            _emptyHint = _accurateCheck.Checked
                ? "正在使用高精度引擎重新识别…"
                : "正在使用快速引擎重新识别…";
            UpdatePlaceholder();
            _ = RunOcrAsync();
        }
        else
        {
            ShowToast(_accurateCheck.Checked ? "已开启高精度识别" : "已切换快速识别", Theme.Success);
        }
    }

    private void OnTranslateChanged()
    {
        if (_suppressTranslateChange)
        {
            return;
        }

        if (_translateCheck.Checked && !_settings.TranslationDisclosureAccepted)
        {
            DialogResult answer = MessageBox.Show(
                this,
                "翻译模式会把识别出的文字发送到 MyMemory 在线翻译服务（mymemory.translated.net），用于生成译文。\n\n" +
                "图片、截图和剪贴板内容不会上传；OCR 仍在本机完成。\n\n是否启用在线翻译？",
                "启用在线翻译",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes)
            {
                _suppressTranslateChange = true;
                _translateCheck.Checked = false;
                _suppressTranslateChange = false;
                ApplyTranslationChrome();
                return;
            }

            _settings.TranslationDisclosureAccepted = true;
        }

        _settings.TranslateEnabled = _translateCheck.Checked;
        _settings.Save();
        ApplyTranslationChrome();

        if (_lastImages.Count > 0)
        {
            _emptyHint = _translateCheck.Checked
                ? "正在重新识别并翻译…"
                : "正在重新识别…";
            UpdatePlaceholder();
            _ = RunOcrAsync();
        }
        else
        {
            ShowToast(_translateCheck.Checked ? "已开启翻译模式" : "已关闭翻译模式", Theme.Success);
        }
    }

    private void ApplyTranslationChrome()
    {
        bool online = _translateCheck.Checked;
        _header.PrivacyText = online
            ? "OCR 本地 · 翻译联网"
            : "本地识别 · 不上传";
        _resultTitle.Text = online ? "原文" : "识别结果";
        SetTranslationVisible(online);
        if (online && !_translationSucceeded)
        {
            _translationTitle.Text = "译文";
            _translationHint = _resultBox.TextLength > 0
                ? "正在翻译…"
                : "译文将在这里显示";
        }

        UpdatePlaceholder();
        if (_card.Width > 0)
        {
            LayoutCard();
        }

        OnResultTextChanged();
    }

    private void SetTranslationVisible(bool visible)
    {
        _translationTitle.Visible = visible;
        _translationBox.Visible = visible;
        _translationPlaceholder.Visible = visible && !_translationSucceeded && _translationBox.TextLength == 0;
        _verticalDivider.Visible = visible;
        _charCount.Visible = !visible;
        if (!visible)
        {
            _translationSucceeded = false;
        }

        if (_card.Width > 0)
        {
            LayoutCard();
        }
    }

    private void OnLanguageChanged()
    {
        if (_suppressModeChange)
        {
            return;
        }

        _settings.OcrMode = SelectedMode.ToString();
        _settings.Save();

        if (_lastImages.Count > 0)
        {
            _emptyHint = "正在使用新的识别语言重新识别…";
            UpdatePlaceholder();
            _ = RunOcrAsync();
        }
        else
        {
            ShowToast("识别语言已切换", Theme.Success);
        }
    }

    private async Task BeginCaptureAsync()
    {
        if (_captureInProgress || _closing)
        {
            return;
        }

        _captureInProgress = true;
        _toastTimer.Stop();
        CaptureOverlayForm? overlay = null;
        Bitmap? screenshot = null;
        try
        {
            _opacityBeforeCapture = Opacity;
            Opacity = 0d;
            Hide();
            NativeMethods.ShowWindow(Handle, NativeMethods.SW_HIDE);
            Application.DoEvents();
            NativeMethods.FlushComposition();
            await Task.Delay(280);
            NativeMethods.FlushComposition();

            Rectangle bounds = SystemInformation.VirtualScreen;
            screenshot = CaptureScreen(bounds);
            if (screenshot is null)
            {
                RestoreMainWindow();
                SetStatus("截图失败，请重试", Theme.Danger);
                return;
            }

            overlay = new CaptureOverlayForm(screenshot, bounds);
            screenshot = null; // owned by the overlay from here
            DialogResult result = overlay.ShowDialog();

            List<Bitmap> selected = overlay.TakeSelectedImages();
            string windowTitle = overlay.SelectedWindowTitle;
            CaptureSource source = overlay.SelectedSource;
            RestoreMainWindow();

            if (result == DialogResult.OK && selected.Count > 0)
            {
                string label;
                if (source == CaptureSource.Window)
                {
                    label = $"窗口 · {(string.IsNullOrWhiteSpace(windowTitle) ? "已选择窗口" : windowTitle)}";
                }
                else if (selected.Count > 1)
                {
                    label = $"多区域 · {selected.Count} 个 · 按框选顺序";
                }
                else
                {
                    label = $"截图 · {selected[0].Width} × {selected[0].Height}";
                }

                LoadImagesAndRecognize(selected, label);
            }
            else
            {
                foreach (Bitmap image in selected)
                {
                    image.Dispose();
                }

                SetStatus("已取消截图", Theme.TextMuted);
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("截图流程失败", ex);
            RestoreMainWindow();
            SetStatus("截图失败，请重试", Theme.Danger);
            MessageBox.Show(this, "截图时遇到问题：\n\n" + ex.Message, "拾字",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            overlay?.Dispose();
            screenshot?.Dispose();
            _captureInProgress = false;
        }
    }

    private static Bitmap? CaptureScreen(Rectangle bounds)
    {
        Bitmap? bitmap = null;
        try
        {
            bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size,
                CopyPixelOperation.SourceCopy);
            return bitmap;
        }
        catch (Exception ex)
        {
            bitmap?.Dispose();
            AppLog.Write("屏幕截图失败", ex);
            return null;
        }
    }

    private void RestoreMainWindow()
    {
        if (_closing || IsDisposed)
        {
            return;
        }

        Opacity = _opacityBeforeCapture > 0d ? _opacityBeforeCapture : 1d;
        if (!Visible)
        {
            Show();
        }

        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        Activate();
        BringToFront();
    }

    private void OpenImageDialog()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择要识别的图片",
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|所有文件|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            LoadImageFromFile(dialog.FileName);
        }
    }

    private void LoadImageFromFile(string path)
    {
        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using Image image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
            if (image.Width < 2 || image.Height < 2)
            {
                SetStatus("图片尺寸过小", Theme.Danger);
                return;
            }

            var bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.White);
                graphics.DrawImage(image, 0, 0, image.Width, image.Height);
            }

            string name = Path.GetFileName(path);
            LoadImageAndRecognize(bitmap, $"图片 · {name} · {bitmap.Width} × {bitmap.Height}");
        }
        catch (Exception ex)
        {
            AppLog.Write("打开图片失败", ex);
            SetStatus("无法打开这张图片", Theme.Danger);
            MessageBox.Show(this, "无法读取图片：\n\n" + ex.Message, "拾字",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void PasteFromClipboard()
    {
        try
        {
            if (Clipboard.ContainsImage())
            {
                using Image? image = Clipboard.GetImage();
                if (image is not null)
                {
                    Bitmap bitmap = CopyToOpaqueBitmap(image);
                    LoadImageAndRecognize(bitmap, $"剪贴板 · {bitmap.Width} × {bitmap.Height}");
                    return;
                }
            }

            if (Clipboard.ContainsFileDropList())
            {
                StringCollection? files = Clipboard.GetFileDropList();
                if (files is not null)
                {
                    foreach (string? file in files)
                    {
                        if (!string.IsNullOrWhiteSpace(file) && IsImageFile(file))
                        {
                            LoadImageFromFile(file);
                            return;
                        }
                    }
                }
            }

            SetStatus("剪贴板中没有图片", Theme.Warning);
        }
        catch (ExternalException ex)
        {
            AppLog.Write("读取剪贴板失败", ex);
            SetStatus("剪贴板正被其他程序占用，请稍后再试", Theme.Warning);
        }
        catch (Exception ex)
        {
            AppLog.Write("粘贴图片失败", ex);
            SetStatus("无法读取剪贴板中的图片", Theme.Danger);
        }
    }

    private static Bitmap CopyToOpaqueBitmap(Image image)
    {
        var bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.White);
        graphics.DrawImage(image, 0, 0, image.Width, image.Height);
        return bitmap;
    }

    private void LoadImageAndRecognize(Bitmap image, string sourceLabel)
    {
        LoadImagesAndRecognize(new List<Bitmap> { image }, sourceLabel);
    }

    private void LoadImagesAndRecognize(IReadOnlyList<Bitmap> images, string sourceLabel)
    {
        if (_closing)
        {
            foreach (Bitmap image in images)
            {
                image.Dispose();
            }

            return;
        }

        if (images.Count == 0)
        {
            return;
        }

        DisposeLastImages();
        foreach (Bitmap image in images)
        {
            _lastImages.Add(image);
        }

        _sourceLabel.Text = sourceLabel;
        _resultBox.Clear();
        _metaLabel.Text = images.Count > 1
            ? $"0/{images.Count} 个区域 · 等待识别"
            : "OCR 识别中…";
        _emptyHint = images.Count > 1
            ? $"正在识别 1/{images.Count} 个区域…"
            : "正在识别图片中的文字…";
        UpdatePlaceholder();
        _ = RunOcrAsync();
    }

    private void DisposeLastImages()
    {
        foreach (Bitmap image in _lastImages)
        {
            image.Dispose();
        }

        _lastImages.Clear();
    }

    private async Task RunOcrAsync()
    {
        if (_lastImages.Count == 0 || _closing)
        {
            return;
        }

        _ocrCts?.Cancel();
        _ocrCts?.Dispose();
        var cts = new CancellationTokenSource();
        _ocrCts = cts;
        CancellationToken cancellationToken = cts.Token;

        int generation = ++_ocrGeneration;
        int total = _lastImages.Count;
        OcrMode mode = SelectedMode;
        OcrQuality quality = SelectedQuality;
        var clones = new List<Bitmap>(total);
        try
        {
            foreach (Bitmap image in _lastImages)
            {
                clones.Add(new Bitmap(image));
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("复制待识别图片失败", ex);
            foreach (Bitmap clone in clones)
            {
                clone.Dispose();
            }

            if (ReferenceEquals(_ocrCts, cts))
            {
                _ocrCts = null;
            }

            cts.Dispose();
            _card.Busy = false;
            SetStatus("识别失败，请重试", Theme.Danger);
            return;
        }

        _card.Busy = true;
        _copyButton.Enabled = false;
        _statusDot.DotColor = Theme.Primary;
        if (_translateCheck.Checked)
        {
            _translationSucceeded = false;
            _translationBox.Clear();
            _translationHint = "正在识别…";
            _translationTitle.Text = "译文";
            SetTranslationVisible(true);
            UpdatePlaceholder();
        }
        if (quality == OcrQuality.Accurate && !PpOcrService.IsInitialized)
        {
            _statusLabel.Text = "正在加载高精度模型（首次使用）…";
            _metaLabel.Text = "PP-OCRv5 · 首次加载";
        }
        else
        {
            _statusLabel.Text = total > 1 ? $"正在识别第 1/{total} 个区域…" : "正在识别…";
            _metaLabel.Text = total > 1 ? $"区域 1/{total} · OCR 识别中…" : "OCR 识别中…";
        }

        var progress = new Progress<string>(message =>
        {
            if (generation == _ocrGeneration && !_closing)
            {
                _statusLabel.Text = message;
                _metaLabel.Text = message;
            }
        });

        try
        {
            var texts = new List<string>(total);
            var labels = new List<string>(total);
            var notes = new List<string>();
            var confidences = new List<float?>(total);
            var totalDuration = TimeSpan.Zero;

            for (int i = 0; i < clones.Count; i++)
            {
                if (generation != _ocrGeneration || _closing || cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (quality != OcrQuality.Accurate || PpOcrService.IsInitialized)
                {
                    _statusLabel.Text = total > 1
                        ? $"正在识别第 {i + 1}/{total} 个区域…"
                        : "正在识别…";
                    _metaLabel.Text = total > 1
                        ? $"区域 {i + 1}/{total} · OCR 识别中…"
                        : "OCR 识别中…";
                }

                OcrOutcome outcome = await OcrRouter.RecognizeAsync(
                    clones[i], mode, quality, progress, cancellationToken);
                if (generation != _ocrGeneration || _closing)
                {
                    return;
                }

                texts.Add(outcome.Text);
                labels.Add(outcome.EngineLabel);
                confidences.Add(outcome.Confidence);
                if (!string.IsNullOrWhiteSpace(outcome.Note))
                {
                    notes.Add(outcome.Note);
                }

                totalDuration += outcome.Duration;
                string partial = JoinTexts(texts);
                if (partial.Length > 0)
                {
                    _resultBox.Text = partial;
                    _resultBox.SelectionStart = 0;
                    _resultBox.SelectionLength = 0;
                    _resultBox.ScrollToCaret();
                    OnResultTextChanged();
                }

                _metaLabel.Text = total > 1
                    ? $"已识别 {i + 1}/{total} 个区域"
                    : "OCR 识别中…";
            }

            if (generation != _ocrGeneration || _closing || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await ApplyOutcomesAsync(
                texts, labels, notes, confidences, totalDuration, total, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // A newer request replaced this one.
        }
        catch (Exception ex)
        {
            AppLog.Write("文字识别失败", ex);
            if (generation == _ocrGeneration && !_closing)
            {
                string message = string.IsNullOrWhiteSpace(ex.Message)
                    ? "识别失败，请重试。"
                    : ex.Message;
                if (_resultBox.TextLength == 0)
                {
                    _emptyHint = "识别失败\n\n" + message;
                    UpdatePlaceholder();
                }

                _metaLabel.Text = "OCR 失败";
                SetStatus("识别失败，请重试", Theme.Danger);
            }
        }
        finally
        {
            foreach (Bitmap clone in clones)
            {
                clone.Dispose();
            }

            if (ReferenceEquals(_ocrCts, cts))
            {
                _ocrCts = null;
            }

            cts.Dispose();

            if (generation == _ocrGeneration && !_closing)
            {
                _card.Busy = false;
                OnResultTextChanged();
            }
        }
    }

    private async Task ApplyOutcomesAsync(
        List<string> texts,
        List<string> labels,
        List<string> notes,
        List<float?> confidences,
        TimeSpan duration,
        int total,
        CancellationToken cancellationToken)
    {
        string combined = JoinTexts(texts);
        _translationSucceeded = false;
        _translationBox.Clear();
        _translationHint = _translateCheck.Checked ? "正在识别…" : "译文将在这里显示";
        _translationTitle.Text = "译文";
        SetTranslationVisible(_translateCheck.Checked);
        _resultBox.Text = combined;
        _resultBox.SelectionStart = 0;
        _resultBox.SelectionLength = 0;
        _resultBox.ScrollToCaret();

        string engine = string.Join(" / ", labels.Distinct());
        var scores = confidences
            .Where(score => score.HasValue)
            .Select(score => score!.Value)
            .ToList();
        string confidence = scores.Count > 0 ? $" · {scores.Average():0.00}" : string.Empty;
        string note = notes.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
        string baseMeta = note.Length == 0
            ? $"{engine}{confidence} · {FormatDuration(duration)}"
            : $"{engine}{confidence} · {FormatDuration(duration)} · {note}";

        if (combined.Length == 0)
        {
            _metaLabel.Text = baseMeta;
            _emptyHint = "未识别到文字\n\n可以尝试放大图片，或重新框选更清晰的区域。";
            _translationHint = "未识别到文字";
            SetStatus("未识别到文字", Theme.Warning);
            ResetAccurateAvailability();
            UpdatePlaceholder();
            OnResultTextChanged();
            return;
        }

        TranslationDirection? direction = _translateCheck.Checked
            ? TranslationService.DetectDirection(combined)
            : null;
        if (direction is null)
        {
            _translationHint = "未检测到可翻译的中英文";
            _metaLabel.Text = _translateCheck.Checked
                ? $"{baseMeta} · 无可翻译文字"
                : baseMeta;
            int nonEmpty = texts.Count(text => text.Length > 0);
            if (_translateCheck.Checked)
            {
                SetStatus(total > 1
                    ? $"已识别 {nonEmpty}/{total} 个区域 · 未检测到可翻译文字"
                    : "未检测到可翻译的中英文", Theme.Warning);
            }
            else
            {
                SetStatus(total > 1
                    ? $"已识别 {nonEmpty}/{total} 个区域"
                    : "识别完成", Theme.Success);
            }

            ResetAccurateAvailability();
            UpdatePlaceholder();
            OnResultTextChanged();
            return;
        }

        TranslationDirectionInfo info = TranslationService.Describe(direction.Value);
        _translationHint = "正在翻译…";
        _translationTitle.Text = $"译文 · {info.Label}";
        UpdatePlaceholder();
        SetStatus($"正在翻译（{info.ShortLabel}）…", Theme.Primary);
        _statusDot.DotColor = Theme.Primary;
        _metaLabel.Text = $"在线翻译 · {info.ShortLabel}…";

        var translationWatch = Stopwatch.StartNew();
        try
        {
            string translated = await TranslationService.TranslateAsync(
                combined, direction.Value, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            translationWatch.Stop();

            _translationSucceeded = true;
            _translationBox.Text = translated;
            _translationBox.SelectionStart = 0;
            _translationBox.SelectionLength = 0;
            _translationBox.ScrollToCaret();
            _metaLabel.Text =
                $"{engine}{confidence} · {info.ShortLabel} · {FormatDuration(duration + translationWatch.Elapsed)}";
            SetStatus("翻译完成", Theme.Success);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Write("在线翻译失败", ex);
            translationWatch.Stop();
            _translationSucceeded = false;
            _translationBox.Text = "翻译失败：" + ex.Message;
            _translationTitle.Text = "译文 · 失败";
            _metaLabel.Text = "在线翻译失败 · 已保留原文";
            SetStatus("翻译失败，已保留原文", Theme.Warning);
        }

        ResetAccurateAvailability();
        UpdatePlaceholder();
        OnResultTextChanged();
    }

    private static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalSeconds >= 1
            ? $"{duration.TotalSeconds:0.0} 秒"
            : $"{(int)duration.TotalMilliseconds} ms";
    }

    private void ResetAccurateAvailability()
    {
        if (_accurateCheck.Checked && !OcrRouter.AccurateAvailable)
        {
            _suppressQualityChange = true;
            _accurateCheck.Checked = false;
            _suppressQualityChange = false;
            _accurateCheck.Enabled = false;
            _settings.AccurateOcr = false;
            _settings.Save();
        }
    }

    private static string JoinTexts(IEnumerable<string> texts)
    {
        return string.Join(Environment.NewLine + Environment.NewLine,
            texts.Where(text => !string.IsNullOrWhiteSpace(text)));
    }

    private void OnResultTextChanged()
    {
        bool split = _translateCheck.Checked;
        _placeholder.Visible = _resultBox.TextLength == 0;
        _translationPlaceholder.Text = _translationHint;
        _translationPlaceholder.Visible = split && !_translationSucceeded && _translationBox.TextLength == 0;
        _copyButton.Text = !split
            ? "复制全文"
            : (_translationSucceeded ? "复制译文" : "复制原文");
        _copyButton.Enabled = !_card.Busy &&
                              (_resultBox.TextLength > 0 ||
                               (split && _translationSucceeded && _translationBox.TextLength > 0));
        _clearButton.Enabled = _resultBox.TextLength > 0 || _translationBox.TextLength > 0 || _lastImages.Count > 0;
        _charCount.Text = split || _resultBox.TextLength == 0
            ? string.Empty
            : $"{_resultBox.TextLength:N0} 字符";
    }

    private void UpdatePlaceholder()
    {
        _placeholder.Text = _emptyHint;
        _placeholder.Visible = _resultBox.TextLength == 0;
        _translationPlaceholder.Text = _translationHint;
        _translationPlaceholder.Visible = _translateCheck.Checked &&
                                          !_translationSucceeded &&
                                          _translationBox.TextLength == 0;
    }

    private string BuildDefaultEmptyHint()
    {
        string hotkey = string.IsNullOrWhiteSpace(_activeHotkey) ? "“截图取字”按钮" : _activeHotkey;
        return string.Format(DefaultEmptyHint, hotkey);
    }

    private void CopyResult()
    {
        bool copyTranslation = _translateCheck.Checked &&
                               _translationSucceeded &&
                               _translationBox.TextLength > 0;
        string text = copyTranslation ? _translationBox.Text : _resultBox.Text;
        if (text.Length == 0)
        {
            return;
        }

        try
        {
            Clipboard.SetText(text);
            ShowToast(copyTranslation ? "已复制译文" : "已复制识别结果", Theme.Success);
        }
        catch (ExternalException ex)
        {
            AppLog.Write("复制到剪贴板失败", ex);
            SetStatus("剪贴板被占用，复制失败", Theme.Warning);
        }
    }

    private void ClearResult()
    {
        _ocrCts?.Cancel();
        _ocrCts?.Dispose();
        _ocrCts = null;
        _ocrGeneration++;
        DisposeLastImages();
        _resultBox.Clear();
        _translationBox.Clear();
        _translationSucceeded = false;
        _translationHint = "译文将在这里显示";
        _translationTitle.Text = "译文";
        _sourceLabel.Text = string.Empty;
        _charCount.Text = string.Empty;
        _metaLabel.Text = _translateCheck.Checked ? "OCR 本地 · 翻译联网" : "本地 OCR · 无需联网";
        _emptyHint = BuildDefaultEmptyHint();
        UpdatePlaceholder();
        _card.Busy = false;
        SetStatus("已清空", Theme.TextMuted);
        _resultBox.Focus();
    }

    private void SetStatus(string text, Color color)
    {
        _toastTimer.Stop();
        _baseStatusText = text;
        _baseStatusColor = color;
        _statusLabel.Text = text;
        _statusDot.DotColor = color;
    }

    private void ShowToast(string text, Color color)
    {
        _statusLabel.Text = text;
        _statusDot.DotColor = color;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void OnFormKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.O)
        {
            OpenImageDialog();
            e.SuppressKeyPress = true;
        }
        else if (e.Control && e.Shift && e.KeyCode == Keys.S)
        {
            _ = BeginCaptureAsync();
            e.SuppressKeyPress = true;
        }
    }

    private void OnFormShown(object? sender, EventArgs e)
    {
        LayoutAll();
        if (!string.IsNullOrWhiteSpace(_initialImagePath) && File.Exists(_initialImagePath))
        {
            string path = _initialImagePath;
            BeginInvoke(new Action(() => LoadImageFromFile(path)));
        }
        else
        {
            _captureButton.Focus();
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_exitRequested && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            if (!_trayHintShown)
            {
                _trayHintShown = true;
                _tray?.ShowBalloonTip(2600, "拾字仍在后台运行",
                    $"按 {_activeHotkey} 截图取字；右键托盘图标可退出。",
                    ToolTipIcon.Info);
            }

            return;
        }

        _closing = true;
        _ocrGeneration++;
        _ocrCts?.Cancel();
        _ocrCts?.Dispose();
        _ocrCts = null;
        DisposeLastImages();

        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }

        _trayMenu?.Dispose();
        _trayMenu = null;

        try
        {
            float scale = DeviceDpi / 96f;
            _settings.WindowWidth = Math.Max(810, (int)Math.Round(ClientSize.Width / scale));
            _settings.WindowHeight = Math.Max(590, (int)Math.Round(ClientSize.Height / scale));
            _settings.OcrMode = SelectedMode.ToString();
            _settings.Save();
        }
        catch
        {
            // Ignore shutdown-time settings errors.
        }

        _appIcon.Dispose();
    }

    private void RegisterHotkeys()
    {
        if (_registeredHotkeys.Count > 0)
        {
            return;
        }

        for (int i = 0; i < HotkeyCandidates.Length; i++)
        {
            (uint modifiers, Keys key, string label) = HotkeyCandidates[i];
            int id = HotkeyId + i;
            if (NativeMethods.RegisterHotKey(Handle, id, modifiers, (uint)key))
            {
                _registeredHotkeys.Add((id, label));
            }
        }

        if (_registeredHotkeys.Count > 0)
        {
            _activeHotkey = _registeredHotkeys[0].Label;
            string all = string.Join(" / ", _registeredHotkeys.Select(hotkey => hotkey.Label));
            _captureButton.HintText = _activeHotkey;
            _captureButton.Invalidate();
            if (_trayCaptureItem is not null)
            {
                _trayCaptureItem.ShortcutKeyDisplayString = _activeHotkey;
                _trayCaptureItem.ToolTipText = "快捷键：" + all;
            }

            _toolTip.SetToolTip(_captureButton, $"截图取字（{all}）");
            _emptyHint = BuildDefaultEmptyHint();
            UpdatePlaceholder();
            return;
        }

        _activeHotkey = string.Empty;
        _captureButton.HintText = "点击使用";
        _captureButton.Invalidate();
        if (_trayCaptureItem is not null)
        {
            _trayCaptureItem.ShortcutKeyDisplayString = string.Empty;
        }

        _emptyHint = BuildDefaultEmptyHint();
        UpdatePlaceholder();
        SetStatus("全局快捷键被占用，可点击按钮截图", Theme.Warning);
    }

    private void UnregisterHotkeys()
    {
        foreach ((int id, _) in _registeredHotkeys)
        {
            NativeMethods.UnregisterHotKey(Handle, id);
        }

        _registeredHotkeys.Clear();
    }

    private void LayoutAll()
    {
        LayoutContent();
        LayoutActions();
        LayoutCard();
        LayoutBottom();
    }

    private void LayoutContent()
    {
        Rectangle r = _content.DisplayRectangle;
        if (r.Width <= 0 || r.Height <= 0)
        {
            return;
        }

        int actionHeight = S(44);
        int bottomHeight = S(38);
        int gap = S(14);
        int cardTop = r.Y + actionHeight + S(16);
        int bottomTop = r.Bottom - bottomHeight;
        int cardHeight = Math.Max(S(180), bottomTop - gap - cardTop);

        _actions.SetBounds(r.X, r.Y, r.Width, actionHeight);
        _card.SetBounds(r.X, cardTop, r.Width, cardHeight);
        _bottom.SetBounds(r.X, bottomTop, r.Width, bottomHeight);
    }

    private void LayoutActions()
    {
        int h = _actions.Height;
        int buttonHeight = S(44);
        int y = Math.Max(0, (h - buttonHeight) / 2);
        int x = 0;
        _captureButton.SetBounds(x, y, S(210), buttonHeight);
        x += S(210) + S(8);
        _openButton.SetBounds(x, y, S(112), buttonHeight);
        x += S(112) + S(8);
        _pasteButton.SetBounds(x, y, S(128), buttonHeight);

        int comboHeight = S(31);
        int comboWidth = S(166);
        int comboX = _actions.Width - comboWidth;
        int comboY = Math.Max(0, (h - comboHeight) / 2);
        _languageCombo.SetBounds(comboX, comboY, comboWidth, comboHeight);

        int labelWidth = S(66);
        int labelX = comboX - S(8) - labelWidth;
        int labelY = Math.Max(0, (h - _languageLabel.PreferredHeight) / 2);
        _languageLabel.SetBounds(labelX, labelY, labelWidth, _languageLabel.PreferredHeight);
    }

    private void LayoutCard()
    {
        int w = _card.Width;
        int h = _card.Height;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        int left = S(20);
        int top = S(15);
        int dividerY = S(45);
        bool split = _translateCheck.Checked;
        _translationTitle.Visible = split;
        _translationBox.Visible = split;
        _verticalDivider.Visible = split;
        _charCount.Visible = !split;

        _divider.SetBounds(left, dividerY, w - left * 2, 1);
        _resultTitle.Location = new Point(left, top);
        if (!split)
        {
            _sourceLabel.SetBounds(
                left + _resultTitle.PreferredWidth + S(10), top,
                Math.Max(S(40), w - left * 2 - _resultTitle.PreferredWidth - S(150)), S(24));
            _charCount.SetBounds(w - S(20) - S(130), top, S(130), S(24));

            int textTop = dividerY + S(7);
            _resultBox.SetBounds(left + S(2), textTop, w - left * 2 - S(4),
                Math.Max(S(40), h - textTop - S(12)));
            _placeholder.SetBounds(left + S(5), textTop + S(6), w - left * 2 - S(10),
                Math.Max(S(40), h - textTop - S(22)));
            _translationPlaceholder.Visible = false;
            return;
        }

        int contentWidth = w - left * 2;
        int gap = S(18);
        int leftWidth = (int)(contentWidth * 0.48);
        int rightX = left + leftWidth + gap;
        int rightWidth = Math.Max(S(80), w - left - rightX);
        int textTop2 = dividerY + S(7);
        _sourceLabel.SetBounds(
            left + _resultTitle.PreferredWidth + S(10), top,
            Math.Max(S(40), leftWidth - _resultTitle.PreferredWidth - S(18)), S(24));
        _translationTitle.SetBounds(rightX, top, rightWidth, S(24));
        _verticalDivider.SetBounds(rightX - gap / 2, dividerY + 1, 1, Math.Max(S(40), h - dividerY - S(12)));
        _resultBox.SetBounds(left + S(2), textTop2, leftWidth - S(4),
            Math.Max(S(40), h - textTop2 - S(12)));
        _translationBox.SetBounds(rightX + S(2), textTop2, rightWidth - S(4),
            Math.Max(S(40), h - textTop2 - S(12)));
        _placeholder.SetBounds(left + S(5), textTop2 + S(6), leftWidth - S(10),
            Math.Max(S(40), h - textTop2 - S(22)));
        _translationPlaceholder.SetBounds(rightX + S(5), textTop2 + S(6), rightWidth - S(10),
            Math.Max(S(40), h - textTop2 - S(22)));
        _translationPlaceholder.Visible = !_translationSucceeded && _translationBox.TextLength == 0;
    }

    private void LayoutBottom()
    {
        int h = _bottom.Height;
        int buttonHeight = S(36);
        int y = Math.Max(0, (h - buttonHeight) / 2);
        _copyButton.SetBounds(0, y, S(126), buttonHeight);
        _clearButton.SetBounds(S(136), y, S(90), buttonHeight);
        _accurateCheck.SetBounds(S(236), Math.Max(0, (h - S(24)) / 2), S(96), S(24));
        _translateCheck.SetBounds(S(336), Math.Max(0, (h - S(24)) / 2), S(80), S(24));

        int statusX = S(420);
        int metaWidth = S(230);
        int statusWidth = Math.Max(S(72), _bottom.Width - statusX - metaWidth - S(12));
        _statusDot.SetBounds(statusX, y + (buttonHeight - _statusDot.Height) / 2, _statusDot.Width, _statusDot.Height);
        _statusLabel.SetBounds(statusX + S(18), y, statusWidth - S(18), buttonHeight);
        _metaLabel.SetBounds(_bottom.Width - metaWidth, y, metaWidth, buttonHeight);
    }

    private int S(int value) => (int)Math.Round(value * DeviceDpi / 96f);

    private void EnableImageDrop(Control control)
    {
        control.AllowDrop = true;
        control.DragEnter += OnImageDragEnter;
        control.DragLeave += OnImageDragLeave;
        control.DragDrop += OnImageDragDrop;
        foreach (Control child in control.Controls)
        {
            EnableImageDrop(child);
        }
    }

    private void OnImageDragEnter(object? sender, DragEventArgs e)
    {
        if (CanAcceptDrop(e.Data))
        {
            e.Effect = DragDropEffects.Copy;
            _card.Highlighted = true;
            SetStatus("松开鼠标以识别图片", Theme.Primary);
        }
        else
        {
            e.Effect = DragDropEffects.None;
        }
    }

    private void OnImageDragLeave(object? sender, EventArgs e)
    {
        Point point = PointToClient(Cursor.Position);
        if (!ClientRectangle.Contains(point))
        {
            _card.Highlighted = false;
            SetStatus(_baseStatusText, _baseStatusColor);
        }
    }

    private void OnImageDragDrop(object? sender, DragEventArgs e)
    {
        _card.Highlighted = false;

        if (e.Data is null)
        {
            return;
        }

        if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
            e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            string? imageFile = files.FirstOrDefault(IsImageFile);
            if (imageFile is not null)
            {
                LoadImageFromFile(imageFile);
                return;
            }
        }

        if (e.Data.GetDataPresent(DataFormats.Bitmap) &&
            e.Data.GetData(DataFormats.Bitmap) is Image image)
        {
            Bitmap bitmap = CopyToOpaqueBitmap(image);
            LoadImageAndRecognize(bitmap, $"拖入图片 · {bitmap.Width} × {bitmap.Height}");
            return;
        }

        SetStatus("请拖入 PNG、JPG、BMP 或 GIF 图片", Theme.Warning);
    }

    private static bool CanAcceptDrop(IDataObject? data)
    {
        if (data is null)
        {
            return false;
        }

        if (data.GetDataPresent(DataFormats.FileDrop) &&
            data.GetData(DataFormats.FileDrop) is string[] files &&
            files.Any(IsImageFile))
        {
            return true;
        }

        return data.GetDataPresent(DataFormats.Bitmap);
    }

    private static bool IsImageFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string extension = Path.GetExtension(path);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".tif", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record OcrModeItem(string Label, OcrMode Mode)
    {
        public override string ToString() => Label;
    }
}
