using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text;
using System.Text.RegularExpressions;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace SnapText;

internal enum OcrMode
{
    Auto,
    ChineseEnglish,
    English
}

internal sealed record OcrOutcome(
    string Text,
    string EngineTag,
    string EngineLabel,
    TimeSpan Duration,
    string Note,
    float? Confidence = null);

internal static class OcrService
{
    private const string ChineseTag = "zh-Hans-CN";
    private const string EnglishTag = "en-US";
    private static readonly Lazy<IReadOnlyList<Language>> Languages = new(LoadLanguages);
    private static readonly Regex CjkRegex = new(@"[\u3400-\u4DBF\u4E00-\u9FFF\uF900-\uFAFF]", RegexOptions.Compiled);
    private static readonly Regex SpaceBetweenCjk = new(@"(?<=[\u3400-\u4DBF\u4E00-\u9FFF\uF900-\uFAFF])[ \t]+(?=[\u3400-\u4DBF\u4E00-\u9FFF\uF900-\uFAFF])", RegexOptions.Compiled);
    private static readonly Regex SpaceBeforeCjkPunctuation = new(@"[ \t]+(?=[，。！？；：、）》」』】〕〉])", RegexOptions.Compiled);
    private static readonly Regex SpaceAfterCjkOpening = new(@"(?<=[（《「『【〔〈])[ \t]+", RegexOptions.Compiled);
    private static readonly Regex SpaceAfterCjkPunctuation = new(@"(?<=[，。！？；：、）》」』】〕〉])[ \t]+(?=[\u3400-\u4DBF\u4E00-\u9FFF\uF900-\uFAFF，。！？；：、）》」』】〕〉])", RegexOptions.Compiled);
    private static readonly Regex SpaceBetweenDigits = new(@"(?<=\d)[ \t]+(?=\d)", RegexOptions.Compiled);
    private static readonly Regex FullWidthDecimal = new(@"(?<=\d)[．。](?=\d)", RegexOptions.Compiled);
    private static readonly Regex MultipleSpaces = new(@"[ \t]{2,}", RegexOptions.Compiled);

    public static IReadOnlyList<Language> InstalledLanguages => Languages.Value;

    public static async Task<OcrOutcome> RecognizeAsync(Bitmap source, OcrMode mode)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        var stopwatch = Stopwatch.StartNew();
        string note = string.Empty;

        byte[] pngBytes;
        using (Bitmap prepared = PrepareBitmap(source))
        {
            using var ms = new MemoryStream();
            prepared.Save(ms, ImageFormat.Png);
            pngBytes = ms.ToArray();
        }

        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(pngBytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }

        stream.Seek(0);
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
        using SoftwareBitmap softwareBitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

        OcrEngine? chinese = TryCreateEngine(ChineseTag);
        OcrEngine? english = TryCreateEngine(EnglishTag);

        OcrEngine? primary = mode switch
        {
            OcrMode.English => english ?? chinese,
            _ => chinese ?? english
        };

        if (primary is null)
        {
            throw new InvalidOperationException("系统中没有可用的 OCR 语言包。请在 Windows 设置 > 时间和语言 > 语言和区域中安装中文或英文语言包。");
        }

        string primaryTag = mode == OcrMode.English
            ? (english is not null ? EnglishTag : ChineseTag)
            : (chinese is not null ? ChineseTag : EnglishTag);

        string text = await RecognizeTextAsync(primary, softwareBitmap);

        if (mode == OcrMode.Auto &&
            english is not null &&
            primaryTag != EnglishTag &&
            !string.IsNullOrWhiteSpace(text) &&
            !ContainsCjk(text))
        {
            string englishText = await RecognizeTextAsync(english, softwareBitmap);
            if (!string.IsNullOrWhiteSpace(englishText))
            {
                text = englishText;
                primaryTag = EnglishTag;
                note = "检测到纯英文，已使用英文识别";
            }
        }

        stopwatch.Stop();
        return new OcrOutcome(
            Normalize(text),
            primaryTag,
            LabelForTag(primaryTag),
            stopwatch.Elapsed,
            note);
    }

    public static async Task<OcrOutcome> RecognizeFileAsync(string path, OcrMode mode = OcrMode.Auto)
    {
        using var image = new Bitmap(path);
        return await RecognizeAsync(image, mode);
    }

    public static bool IsChineseInstalled => TryCreateEngine(ChineseTag) is not null;

    public static bool IsEnglishInstalled => TryCreateEngine(EnglishTag) is not null;

    public static string LabelForTag(string tag)
    {
        if (tag.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return "中文 + 英文";
        }

        if (tag.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            return "English";
        }

        return tag;
    }

    private static IReadOnlyList<Language> LoadLanguages()
    {
        try
        {
            return OcrEngine.AvailableRecognizerLanguages.ToList();
        }
        catch (Exception ex)
        {
            AppLog.Write("读取 OCR 语言列表失败", ex);
            return Array.Empty<Language>();
        }
    }

    private static OcrEngine? TryCreateEngine(string tag)
    {
        try
        {
            Language? language = InstalledLanguages.FirstOrDefault(
                l => string.Equals(l.LanguageTag, tag, StringComparison.OrdinalIgnoreCase));
            if (language is null)
            {
                return null;
            }

            return OcrEngine.TryCreateFromLanguage(language);
        }
        catch (Exception ex)
        {
            AppLog.Write($"创建 OCR 引擎失败：{tag}", ex);
            return null;
        }
    }

    private static async Task<string> RecognizeTextAsync(OcrEngine engine, SoftwareBitmap bitmap)
    {
        OcrResult result = await engine.RecognizeAsync(bitmap);
        var lines = new List<string>();
        foreach (OcrLine line in result.Lines)
        {
            string value = BuildLineText(line);
            if (value.Length > 0)
            {
                lines.Add(value);
            }
        }

        return string.Join("\n", lines);
    }

    private static string BuildLineText(OcrLine line)
    {
        var words = line.Words
            .Where(w => !string.IsNullOrWhiteSpace(w.Text))
            .OrderBy(w => w.BoundingRect.X)
            .ToList();
        if (words.Count == 0)
        {
            return line.Text?.Trim() ?? string.Empty;
        }

        if (words.Count == 1)
        {
            return words[0].Text.Trim();
        }

        var heights = words
            .Select(w => (double)w.BoundingRect.Height)
            .Where(h => h >= 4)
            .OrderBy(h => h)
            .ToList();
        double lineHeight = heights.Count > 0 ? heights[heights.Count / 2] : 20d;
        double spaceThreshold = Math.Max(2d, lineHeight * 0.18d);

        var builder = new StringBuilder();
        for (int i = 0; i < words.Count; i++)
        {
            string token = words[i].Text.Trim();
            if (token.Length == 0)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                var previous = words[i - 1];
                double previousRight = previous.BoundingRect.X + previous.BoundingRect.Width;
                double gap = words[i].BoundingRect.X - previousRight;
                if (gap > spaceThreshold && !StartsWithClosingPunctuation(token))
                {
                    builder.Append(' ');
                }
            }

            builder.Append(token);
        }

        return builder.ToString().Trim();
    }

    private static bool StartsWithClosingPunctuation(string token)
    {
        if (token.Length == 0)
        {
            return false;
        }

        char c = token[0];
        return "，。！？；：、）》」』】〕〉,.!?;:%)]}".IndexOf(c) >= 0;
    }

    private static Bitmap PrepareBitmap(Bitmap source)
    {
        int maxDimension = Math.Min((int)OcrEngine.MaxImageDimension, 10000);
        double scale = 1d;

        if (Math.Max(source.Width, source.Height) > maxDimension)
        {
            scale = maxDimension / (double)Math.Max(source.Width, source.Height);
        }
        else if (source.Width < 1400 && source.Height < 900)
        {
            // Windows OCR does noticeably better on small UI text when it is enlarged first.
            scale = 2d;
        }
        else if (source.Width < 2200 && source.Height < 1400)
        {
            scale = 1.35d;
        }

        int targetWidth = Math.Max(1, (int)Math.Round(source.Width * scale));
        int targetHeight = Math.Max(1, (int)Math.Round(source.Height * scale));

        var prepared = new Bitmap(targetWidth, targetHeight, PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(prepared);
        graphics.Clear(Color.White);
        graphics.CompositingMode = CompositingMode.SourceOver;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = scale > 1 ? InterpolationMode.HighQualityBicubic : InterpolationMode.HighQualityBilinear;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        using var attributes = new ImageAttributes();
        attributes.SetWrapMode(WrapMode.TileFlipXY);
        attributes.SetColorMatrix(new ColorMatrix(new[]
        {
            new[] { 0.299f, 0.299f, 0.299f, 0f, 0f },
            new[] { 0.587f, 0.587f, 0.587f, 0f, 0f },
            new[] { 0.114f, 0.114f, 0.114f, 0f, 0f },
            new[] { 0f, 0f, 0f, 1f, 0f },
            new[] { 0f, 0f, 0f, 0f, 1f }
        }));
        graphics.DrawImage(source,
            new Rectangle(0, 0, targetWidth, targetHeight),
            0, 0, source.Width, source.Height,
            GraphicsUnit.Pixel, attributes);
        return prepared;
    }

    internal static string Normalize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        string text = raw.Replace("\r\n", "\n").Replace('\r', '\n');
        text = SpaceBetweenCjk.Replace(text, string.Empty);
        text = SpaceBeforeCjkPunctuation.Replace(text, string.Empty);
        text = SpaceAfterCjkOpening.Replace(text, string.Empty);
        text = SpaceAfterCjkPunctuation.Replace(text, string.Empty);
        text = SpaceBetweenDigits.Replace(text, string.Empty);
        text = FullWidthDecimal.Replace(text, ".");
        text = MultipleSpaces.Replace(text, " ");

        var lines = text.Split('\n')
            .Select(line => line.TrimEnd())
            .ToList();

        // Remove leading/trailing blank lines but preserve intentional paragraph breaks.
        while (lines.Count > 0 && lines[0].Length == 0)
        {
            lines.RemoveAt(0);
        }

        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        var builder = new StringBuilder();
        for (int i = 0; i < lines.Count; i++)
        {
            if (i > 0)
            {
                builder.AppendLine();
            }

            builder.Append(lines[i]);
        }

        return builder.ToString().Trim();
    }

    internal static bool ContainsCjk(string text) => CjkRegex.IsMatch(text);
}
