using System.Text.Json;

namespace SnapText;

internal sealed class AppSettings
{
    public string OcrMode { get; set; } = nameof(SnapText.OcrMode.Auto);

    public bool AccurateOcr { get; set; } = true;

    public int WindowWidth { get; set; } = 880;

    public int WindowHeight { get; set; } = 690;

    private static string SettingsPath
    {
        get
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SnapText");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "settings.json");
        }
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                string json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings is not null)
                {
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("读取设置失败", ex);
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch (Exception ex)
        {
            AppLog.Write("保存设置失败", ex);
        }
    }
}

internal static class AppLog
{
    private static readonly object Sync = new();

    public static string LogPath
    {
        get
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SnapText");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "snaptext.log");
        }
    }

    public static void Write(string context, Exception? ex = null)
    {
        try
        {
            lock (Sync)
            {
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}";
                if (ex is not null)
                {
                    line += Environment.NewLine + ex;
                }

                File.AppendAllText(LogPath, line + Environment.NewLine);
                var info = new FileInfo(LogPath);
                if (info.Exists && info.Length > 512 * 1024)
                {
                    File.Move(LogPath, LogPath + ".old", overwrite: true);
                }
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }
}
