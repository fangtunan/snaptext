using System.Diagnostics;
using System.Drawing.Imaging;
using SkiaSharp;
using RapidOcrNet;

namespace SnapText;

internal static class PpOcrService
{
    private const string EngineLabel = "PP-OCRv5";
    private static readonly SemaphoreSlim InitLock = new(1, 1);
    private static readonly SemaphoreSlim RunLock = new(1, 1);
    private static RapidOcr? _engine;
    private static bool _permanentlyUnavailable;
    private static string _lastError = string.Empty;

    public static string ModelDirectory => Path.Combine(AppContext.BaseDirectory, "models", "ppocrv5");

    public static bool ModelsPresent
    {
        get
        {
            string dir = ModelDirectory;
            return File.Exists(Path.Combine(dir, "ch_PP-OCRv5_mobile_det.onnx")) &&
                   File.Exists(Path.Combine(dir, "ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx")) &&
                   File.Exists(Path.Combine(dir, "PP-OCRv5_mobile_rec_infer.onnx")) &&
                   File.Exists(Path.Combine(dir, "ppocrv5_dict.txt"));
        }
    }

    public static bool IsInitialized => _engine is not null;

    public static bool IsAvailable => ModelsPresent && !_permanentlyUnavailable;

    public static string LastError => _lastError;

    public static async Task<OcrOutcome> RecognizeAsync(
        Bitmap source,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        RapidOcr engine = await EnsureEngineAsync(progress, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        byte[] pngBytes;
        using (var stream = new MemoryStream())
        {
            source.Save(stream, ImageFormat.Png);
            pngBytes = stream.ToArray();
        }

        using SKBitmap bitmap = SKBitmap.Decode(pngBytes)
            ?? throw new InvalidOperationException("无法解码待识别图片。");

        progress?.Report("正在使用高精度引擎识别…");
        await RunLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var options = RapidOcrOptions.Default;

            OcrResult result = await Task.Run(
                () => engine.Detect(bitmap, options),
                cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            var blocks = result.TextBlocks.ToList();
            var scores = blocks
                .Select(block => block.CharScores is null || block.CharScores.Length == 0
                    ? (float?)null
                    : block.CharScores.Average())
                .Where(score => score.HasValue)
                .Select(score => score!.Value)
                .ToList();

            string raw = result.StrRes;
            if (string.IsNullOrWhiteSpace(raw) && blocks.Count > 0)
            {
                raw = string.Join(Environment.NewLine, blocks
                    .OrderBy(block => block.BoxPoints.Min(point => point.Y))
                    .ThenBy(block => block.BoxPoints.Min(point => point.X))
                    .Select(block => block.Text));
            }

            stopwatch.Stop();
            return new OcrOutcome(
                OcrService.Normalize(raw),
                "ppocrv5",
                EngineLabel,
                stopwatch.Elapsed,
                string.Empty,
                scores.Count > 0 ? scores.Average() : null);
        }
        finally
        {
            RunLock.Release();
        }
    }

    public static void MarkUnavailable(string reason)
    {
        _permanentlyUnavailable = true;
        _lastError = reason;
    }

    private static async Task<RapidOcr> EnsureEngineAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (_engine is not null)
        {
            return _engine;
        }

        if (_permanentlyUnavailable)
        {
            throw new InvalidOperationException("高精度 OCR 引擎不可用：" + _lastError);
        }

        await InitLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_engine is not null)
            {
                return _engine;
            }

            if (!ModelsPresent)
            {
                throw new FileNotFoundException("缺少 PP-OCRv5 模型文件。", ModelDirectory);
            }

            progress?.Report("正在加载高精度模型（首次使用）…");
            string dir = ModelDirectory;
            RapidOcr engine = await Task.Run(() =>
            {
                var created = new RapidOcr();
                created.InitModels(
                    Path.Combine(dir, "ch_PP-OCRv5_mobile_det.onnx"),
                    Path.Combine(dir, "ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx"),
                    Path.Combine(dir, "PP-OCRv5_mobile_rec_infer.onnx"),
                    Path.Combine(dir, "ppocrv5_dict.txt"));
                return created;
            }, cancellationToken).ConfigureAwait(false);

            _engine = engine;
            return engine;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _permanentlyUnavailable = true;
            _lastError = ex.Message;
            AppLog.Write("高精度 OCR 引擎初始化失败", ex);
            throw;
        }
        finally
        {
            InitLock.Release();
        }
    }
}
