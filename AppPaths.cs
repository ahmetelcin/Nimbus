namespace Nimbus;

internal static class AppPaths
{
    private static readonly string AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    public static readonly string DataDirectory = Path.Combine(AppData, "Nimbus");

    public static readonly string LegacyDataDirectory = Path.Combine(AppData, "DriveAc");

    public static readonly string TokenDirectory = Path.Combine(DataDirectory, "token");

    public static readonly string SettingsFile = Path.Combine(DataDirectory, "settings.json");

    public static readonly string UploadHistoryFile = Path.Combine(DataDirectory, "uploads.json");

    public static readonly string BackupDirectory = Path.Combine(DataDirectory, "backups");

    public static readonly string NotificationIconFile = Path.Combine(DataDirectory, "icon.png");

    public static readonly string ClientSecretFile = Path.Combine(AppContext.BaseDirectory, "client_secret.json");
}
