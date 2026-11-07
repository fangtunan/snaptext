using System.Text;

namespace SnapText;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length >= 3 && string.Equals(args[0], "--ocr", StringComparison.OrdinalIgnoreCase))
        {
            RunOcrProbe(args);
            return;
        }

        using var mutex = new Mutex(true, @"Local\SnapText.SingleInstance.v1", out bool createdNew);
        if (!createdNew)
        {
            NativeMethods.PostMessage(NativeMethods.HWND_BROADCAST, NativeMethods.ShowWindowMessage,
                IntPtr.Zero, IntPtr.Zero);
            return;
        }

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => HandleFatal(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            HandleFatal(e.ExceptionObject as Exception ?? new Exception("未知错误"));

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        string? initialImage = args.FirstOrDefault(arg =>
            !string.IsNullOrWhiteSpace(arg) &&
            !arg.StartsWith("-", StringComparison.Ordinal) &&
            File.Exists(arg));

        Application.Run(new MainForm(initialImage));
        GC.KeepAlive(mutex);
    }

    private static void HandleFatal(Exception ex)
    {
        AppLog.Write("未处理的异常", ex);
        try
        {
            MessageBox.Show("拾字遇到未处理的错误：\n\n" + ex.Message + "\n\n日志：" + AppLog.LogPath,
                "拾字", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch
        {
            // Nothing else we can do.
        }
    }

    private static void RunOcrProbe(string[] args)
    {
        string input = args[1];
        string output = args[2];
        OcrMode mode = OcrMode.Auto;
        if (args.Length >= 4)
        {
            string value = args[3].ToLowerInvariant();
            mode = value switch
            {
                "zh" or "chinese" or "zh-cn" => OcrMode.ChineseEnglish,
                "en" or "english" => OcrMode.English,
                _ => OcrMode.Auto
            };
        }

        OcrQuality quality = OcrQuality.Fast;
        if (args.Length >= 5)
        {
            quality = string.Equals(args[4], "accurate", StringComparison.OrdinalIgnoreCase)
                ? OcrQuality.Accurate
                : OcrQuality.Fast;
        }

        try
        {
            OcrOutcome outcome = OcrRouter.RecognizeFileAsync(input, mode, quality)
                .GetAwaiter().GetResult();
            File.WriteAllText(output, outcome.Text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            var meta = new StringBuilder();
            meta.AppendLine("EngineTag=" + outcome.EngineTag);
            meta.AppendLine("EngineLabel=" + outcome.EngineLabel);
            meta.AppendLine("DurationMs=" + (int)outcome.Duration.TotalMilliseconds);
            meta.AppendLine("Chars=" + outcome.Text.Length);
            meta.AppendLine("Confidence=" + (outcome.Confidence.HasValue ? outcome.Confidence.Value.ToString("0.000") : ""));
            meta.AppendLine("Note=" + outcome.Note);
            meta.Append("Installed=" + string.Join(", ",
                OcrService.InstalledLanguages.Select(l => l.LanguageTag)));
            File.WriteAllText(output + ".meta.txt", meta.ToString(), new UTF8Encoding(true));
            Environment.ExitCode = 0;
        }
        catch (Exception ex)
        {
            AppLog.Write("OCR 测试模式失败", ex);
            File.WriteAllText(output, "ERROR: " + ex, new UTF8Encoding(true));
            Environment.ExitCode = 1;
        }
    }
}
