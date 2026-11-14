namespace SnapText;

internal enum OcrQuality
{
    Fast,
    Accurate
}

internal static class OcrRouter
{
    public static bool AccurateAvailable => PpOcrService.IsAvailable;

    public static bool AccurateInitialized => PpOcrService.IsInitialized;

    public static async Task<OcrOutcome> RecognizeAsync(
        Bitmap source,
        OcrMode mode,
        OcrQuality quality,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (quality != OcrQuality.Accurate)
        {
            return await OcrService.RecognizeAsync(source, mode).ConfigureAwait(false);
        }

        OcrOutcome? windowsOutcome = null;
        if (mode == OcrMode.English)
        {
            windowsOutcome = await OcrService.RecognizeAsync(source, OcrMode.English)
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(windowsOutcome.Text))
            {
                return windowsOutcome with { Note = "Windows OCR" };
            }
        }
        else if (mode == OcrMode.Auto)
        {
            windowsOutcome = await OcrService.RecognizeAsync(source, OcrMode.Auto)
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(windowsOutcome.Text) &&
                !OcrService.ContainsCjk(windowsOutcome.Text))
            {
                return windowsOutcome with { Note = "Windows OCR" };
            }
        }

        if (PpOcrService.IsAvailable)
        {
            try
            {
                OcrOutcome outcome = await PpOcrService.RecognizeAsync(source, progress, cancellationToken)
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(outcome.Text))
                {
                    return outcome;
                }

                windowsOutcome ??= await OcrService.RecognizeAsync(source, mode)
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(windowsOutcome.Text))
                {
                    return windowsOutcome with { Note = "高精度未识别到文字，已使用 Windows OCR" };
                }

                return outcome;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLog.Write("高精度 OCR 失败，回退 Windows OCR", ex);
                PpOcrService.MarkUnavailable(ex.Message);
                windowsOutcome ??= await OcrService.RecognizeAsync(source, mode)
                    .ConfigureAwait(false);
                return windowsOutcome with { Note = "高精度引擎不可用，已回退 Windows OCR" };
            }
        }

        OcrOutcome result = windowsOutcome ?? await OcrService.RecognizeAsync(source, mode)
            .ConfigureAwait(false);
        return result with { Note = "高精度模型不可用，已使用 Windows OCR" };
    }

    public static async Task<OcrOutcome> RecognizeFileAsync(
        string path,
        OcrMode mode,
        OcrQuality quality,
        CancellationToken cancellationToken = default)
    {
        using var image = new Bitmap(path);
        return await RecognizeAsync(image, mode, quality, null, cancellationToken)
            .ConfigureAwait(false);
    }
}
