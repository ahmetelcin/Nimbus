using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Nimbus;

internal static class FileAssociations
{
    private const string AppName = "Nimbus";
    private const string ProgId = "Nimbus.File";
    private const string ClassesKey = @"Software\Classes";
    private const string AppKey = @"Software\Nimbus";
    private const string CapabilitiesKey = AppKey + @"\Capabilities";
    private const string RegisteredApplicationsKey = @"Software\RegisteredApplications";

    private const int ShcneAssocChanged = 0x08000000;

    // registeredAppUser, Ayarlar'ı doğrudan uygulamanın sayfasında açar.
    public const string SettingsUri = "ms-settings:defaultapps?registeredAppUser=" + AppName;

    private static string OpenCommand => $"\"{Environment.ProcessPath}\" \"%1\"";

    public static void Register()
    {
        using (var progId = Registry.CurrentUser.CreateSubKey($@"{ClassesKey}\{ProgId}"))
        {
            progId.SetValue(null, Strings.FileTypeDescription);
            using (var icon = progId.CreateSubKey("DefaultIcon"))
                icon.SetValue(null, $"\"{Environment.ProcessPath}\",0");
            using var command = progId.CreateSubKey(@"shell\open\command");
            command.SetValue(null, OpenCommand);
        }

        using (var capabilities = Registry.CurrentUser.CreateSubKey(CapabilitiesKey))
        {
            capabilities.SetValue("ApplicationName", AppName);
            capabilities.SetValue("ApplicationDescription", Strings.AppDescription);
            using var associations = capabilities.CreateSubKey("FileAssociations");
            foreach (var extension in FileTypes.Extensions)
                associations.SetValue(extension, ProgId);
        }

        foreach (var extension in FileTypes.Extensions)
        {
            using var openWith = Registry.CurrentUser.CreateSubKey($@"{ClassesKey}\{extension}\OpenWithProgids");
            openWith.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
        }

        using (var registered = Registry.CurrentUser.CreateSubKey(RegisteredApplicationsKey))
            registered.SetValue(AppName, CapabilitiesKey);

        NotifyShell();
    }

    public static void Unregister()
    {
        foreach (var extension in FileTypes.Extensions)
            RemoveFromExtension(extension);

        using (var registered = Registry.CurrentUser.OpenSubKey(RegisteredApplicationsKey, writable: true))
            registered?.DeleteValue(AppName, throwOnMissingValue: false);

        Registry.CurrentUser.DeleteSubKeyTree(AppKey, throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree($@"{ClassesKey}\{ProgId}", throwOnMissingSubKey: false);

        NotifyShell();
    }

    // Exe taşındıysa komut eski yolu, dil değiştiyse açıklama eski dili gösterir; bağlıysa güncellenir.
    public static void RefreshIfRegistered()
    {
        using var progId = Registry.CurrentUser.OpenSubKey($@"{ClassesKey}\{ProgId}");
        using var command = progId?.OpenSubKey(@"shell\open\command");
        if (command?.GetValue(null) is string current
            && (current != OpenCommand || progId!.GetValue(null) as string != Strings.FileTypeDescription))
            Register();
    }

    private static void RemoveFromExtension(string extension)
    {
        var extensionPath = $@"{ClassesKey}\{extension}";
        using (var openWith = Registry.CurrentUser.OpenSubKey($@"{extensionPath}\OpenWithProgids", writable: true))
        {
            if (openWith is null)
                return;

            openWith.DeleteValue(ProgId, throwOnMissingValue: false);
            if (openWith.ValueCount > 0 || openWith.SubKeyCount > 0)
                return;
        }

        Registry.CurrentUser.DeleteSubKey($@"{extensionPath}\OpenWithProgids", throwOnMissingSubKey: false);

        bool isEmpty;
        using (var key = Registry.CurrentUser.OpenSubKey(extensionPath))
            isEmpty = key is { ValueCount: 0, SubKeyCount: 0 };

        if (isEmpty)
            Registry.CurrentUser.DeleteSubKey(extensionPath, throwOnMissingSubKey: false);
    }

    private static void NotifyShell() => SHChangeNotify(ShcneAssocChanged, 0, IntPtr.Zero, IntPtr.Zero);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
