using Microsoft.Win32;

namespace Nimbus;

// Uygulamanın eski adı DriveAc'ten kalan verileri yeni konuma taşır; yeniden giriş gerekmez.
internal static class LegacyMigration
{
    private const string LegacyAppUserModelId = "DriveAc.App";

    public static void Run()
    {
        try
        {
            if (Directory.Exists(AppPaths.LegacyDataDirectory) && !Directory.Exists(AppPaths.DataDirectory))
                Directory.Move(AppPaths.LegacyDataDirectory, AppPaths.DataDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Taşınamazsa uygulama yine çalışır; yalnızca yeniden giriş ister.
        }

        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\AppUserModelId\{LegacyAppUserModelId}", throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(
            $@"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\{LegacyAppUserModelId}",
            throwOnMissingSubKey: false);
    }
}
