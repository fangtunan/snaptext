using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace SnapText;

internal enum TranslationDirection
{
    ChineseToEnglish,
    EnglishToChinese
}

internal readonly record struct TranslationDirectionInfo(
    string From,
    string To,
    string Label,
    string ShortLabel);

internal static class TranslationService
{
    private const string Endpoint = "https://api.mymemory.translated.net/get";
    private const int MaxChunkLength = 450;
    private static readonly HttpClient Client = CreateClient();

    public const string ProviderName = "MyMemory";

    public static TranslationDirectionInfo Describe(TranslationDirection direction)
    {
        return direction == TranslationDirection.ChineseToEnglish
            ? new TranslationDirectionInfo("zh-CN", "en", "中文 → English", "中→英")
            : new TranslationDirectionInfo("en", "zh-CN", "English → 中文", "英→中");
    }

    public static TranslationDirection? DetectDirection(string text)
    {
        int cjk = 0;
        int latin = 0;
        foreach (char c in text)
        {
            if (IsCjk(c))
            {
                cjk++;
            }
            else if (c <= 0x7f && char.IsLetter(c))
            {
                latin++;
            }
        }

        if (cjk == 0 && latin == 0)
        {
            return null;
        }

        return cjk >= latin
            ? TranslationDirection.ChineseToEnglish
            : TranslationDirection.EnglishToChinese;
    }

    public static async Task<string> TranslateAsync(
        string text,
        TranslationDirection direction,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        TranslationDirectionInfo info = Describe(direction);
        string normalized = text.Trim().Replace("\r\n", "\n").Replace('\r', '\n');
        var outputBlocks = new List<string>();
        foreach (string block in normalized.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string flat = string.Join(" ",
                block.Split('\n')
                    .Select(line => line.Trim())
                    .Where(line => line.Length > 0));
            if (flat.Length == 0)
            {
                continue;
            }

            List<string> chunks = SplitLine(flat, MaxChunkLength);
            var translatedParts = new List<string>(chunks.Count);
            foreach (string chunk in chunks)
            {
                translatedParts.Add(await TranslateChunkAsync(chunk, info, cancellationToken).ConfigureAwait(false));
                await Task.Delay(120, cancellationToken).ConfigureAwait(false);
            }

            outputBlocks.Add(string.Join(" ", translatedParts));
        }

        return string.Join(Environment.NewLine + Environment.NewLine, outputBlocks);
    }

    private static async Task<string> TranslateChunkAsync(
        string text,
        TranslationDirectionInfo info,
        CancellationToken cancellationToken)
    {
        // Only use MyMemory's machine translation, not public translation-memory
        // entries, which can contain unrelated or malicious matching sentences.
        string url = $"{Endpoint}?q={Uri.EscapeDataString(text)}&langpair={Uri.EscapeDataString(info.From)}%7C{Uri.EscapeDataString(info.To)}&onlyprivate=1&mt=1";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("application/json");
        using HttpResponseMessage response = await Client.SendAsync(
            request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(response.StatusCode == HttpStatusCode.TooManyRequests
                ? "在线翻译请求过于频繁，请稍后再试。"
                : $"在线翻译服务返回 HTTP {(int)response.StatusCode}。");
        }

        string translated;
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            int status = root.TryGetProperty("responseStatus", out JsonElement statusElement)
                ? statusElement.GetInt32()
                : 200;
            bool quotaFinished = root.TryGetProperty("quotaFinished", out JsonElement quotaElement) &&
                                 quotaElement.ValueKind == JsonValueKind.True;
            if (quotaFinished)
            {
                throw new InvalidOperationException("MyMemory 今日免费翻译额度已用完，请明天再试。");
            }

            if (!root.TryGetProperty("responseData", out JsonElement data) ||
                !data.TryGetProperty("translatedText", out JsonElement translatedElement))
            {
                throw new InvalidOperationException("在线翻译服务返回了无法识别的结果。");
            }

            translated = translatedElement.GetString() ?? string.Empty;
            if (status != 200 && string.IsNullOrWhiteSpace(translated))
            {
                throw new InvalidOperationException($"在线翻译失败（状态 {status}）。");
            }
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("在线翻译服务返回了无法解析的内容。");
        }

        translated = WebUtility.HtmlDecode(translated).Trim();
        if (translated.Length == 0)
        {
            throw new InvalidOperationException("在线翻译没有返回译文。");
        }

        if (translated.StartsWith("MYMEMORY WARNING", StringComparison.OrdinalIgnoreCase) ||
            translated.Contains("YOU USED ALL AVAILABLE FREE TRANSLATIONS", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("MyMemory 今日免费翻译额度已用完，请明天再试。");
        }

        return translated;
    }

    private static List<string> SplitLine(string line, int maxLength)
    {
        var chunks = new List<string>();
        if (line.Length <= maxLength)
        {
            chunks.Add(line);
            return chunks;
        }

        SplitLongLine(line, maxLength, chunks);
        return chunks;
    }

    private static void SplitLongLine(string line, int maxLength, List<string> chunks)
    {
        int start = 0;
        while (start < line.Length)
        {
            int remaining = line.Length - start;
            if (remaining <= maxLength)
            {
                chunks.Add(line.Substring(start));
                return;
            }

            int cut = FindCut(line, start, maxLength);
            chunks.Add(line.Substring(start, cut));
            start += cut;
        }
    }

    private static int FindCut(string text, int start, int maxLength)
    {
        int limit = Math.Min(text.Length - start, maxLength);
        for (int i = limit - 1; i >= Math.Max(1, limit / 2); i--)
        {
            char c = text[start + i];
            if (c is '。' or '！' or '？' or '；' or '.' or '!' or '?' or ';' or '，' or ',')
            {
                return i + 1;
            }
        }

        return limit;
    }

    private static bool IsCjk(char c)
    {
        return c is >= '\u3400' and <= '\u9fff' or >= '\uf900' and <= '\ufaff';
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SnapText/1.2 (+https://github.com/fangtunan/snaptext)");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }
}
