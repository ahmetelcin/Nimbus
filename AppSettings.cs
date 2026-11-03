using System.Text.Json;

namespace DriveAc;

internal sealed class AppSettings
{
    public string? DriveFolderId { get; set; }

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
