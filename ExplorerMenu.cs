using Microsoft.Win32;

namespace Nimbus;

// Desteklenen uzantıların Explorer sağ tık menüsüne "Nimbus ile aç" ekler.
internal static class ExplorerMenu
{
    private const string AssociationsKey = @"Software\Classes\SystemFileAssociations";
    private const string VerbName = "Nimbus";
    private const string VerbText = "Nimbus ile aç";

    public static void SetEnabled(bool enabled)
    {
        foreach (var extension in FileTypes.Extensions)
        {
            if (enabled)
                Add(extension);
            else
                Remove(extension);
        }
    }

    // Exe taşındıysa kayıtlı komut eski yolu gösterir; ayara göre açılışta güncellenir.
    public static void Refresh() => SetEnabled(AppSettings.Load().ExplorerMenu);

    private static void Add(string extension)
    {
        using var verb = Registry.CurrentUser.CreateSubKey($@"{AssociationsKey}\{extension}\shell\{VerbName}");
        verb.SetValue(null, VerbText);
        verb.SetValue("Icon", $"\"{Environment.ProcessPath}\",0");
        using var command = verb.CreateSubKey("command");
        command.SetValue(null, $"\"{Environment.ProcessPath}\" \"%1\"");
    }

    private static void Remove(string extension)
    {
        var extensionPath = $@"{AssociationsKey}\{extension}";
        Registry.CurrentUser.DeleteSubKeyTree($@"{extensionPath}\shell\{VerbName}", throwOnMissingSubKey: false);

        // Başka uygulamaların kayıtlarına dokunulmaz; yalnızca boş kalan anahtarlar silinir.
        if (DeleteIfEmpty($@"{extensionPath}\shell"))
            DeleteIfEmpty(extensionPath);
    }

    private static bool DeleteIfEmpty(string path)
    {
        using (var key = Registry.CurrentUser.OpenSubKey(path))
        {
            if (key is null)
                return true;
            if (key.ValueCount > 0 || key.SubKeyCount > 0)
                return false;
        }

        Registry.CurrentUser.DeleteSubKey(path, throwOnMissingSubKey: false);
        return true;
    }
}
