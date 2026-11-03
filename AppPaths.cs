namespace DriveAc;

internal static class AppPaths
{
    private static readonly string DataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DriveAc");

    public static readonly string TokenDirectory = Path.Combine(DataDirectory, "token");

    public static readonly string SettingsFile = Path.Combine(DataDirectory, "settings.json");

    public static readonly string ClientSecretFile = Path.Combine(AppContext.BaseDirectory, "client_secret.json");
}
