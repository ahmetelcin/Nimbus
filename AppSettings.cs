using System.Text.Json;

namespace Nimbus;

internal enum ReopenChoice
{
    Ask,
    Continue,
    StartOver,
}

internal sealed class AppSettings
{
    public string? DriveFolderId { get; set; }

    public string? AccountEmail { get; set; }

    public string? BrowserPath { get; set; }

    public int CleanupDays { get; set; } = 30;

    public bool AutoSaveBack { get; set; } = true;

    public ReopenChoice ReopenChoice { get; set; } = ReopenChoice.Ask;

    public static AppSettings Load()
    {
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile)) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.SettingsFile)!);
        File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(this));
    }
}
