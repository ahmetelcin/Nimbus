using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace Nimbus;

// Güncellemenin ağ dışındaki adımları: paketi açma, doğrulama ve exe değişimi.
internal static class UpdateInstaller
{
    private const string ExeName = "Nimbus.exe";
    private const int DeleteAttempts = 20;
    private static readonly TimeSpan DeleteRetryDelay = TimeSpan.FromMilliseconds(500);

    public static Version CurrentVersion
    {
        get
        {
            var version = typeof(UpdateInstaller).Assembly.GetName().Version ?? new Version(0, 0, 0);
            return new Version(version.Major, version.Minor, Math.Max(version.Build, 0));
        }
    }

    // Geliştirme derlemesi exe ve dll olarak gelir; tek exe ile değiştirilirse bozulur.
    public static bool CanSelfUpdate => !File.Exists(Path.Combine(AppContext.BaseDirectory, "Nimbus.dll"));

    public static (string Tag, Version Version)? ParseLatest(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var tag = document.RootElement.GetProperty("tag_name").GetString();
            return tag is not null && Version.TryParse(tag.TrimStart('v'), out var version) ? (tag, version) : null;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    public static bool IsNewer(Version candidate, Version current) => candidate > current;

    // Pakette yalnızca exe kullanılır: client_secret.json imzalı değildir ve yerel kopyası korunur.
    public static void ExtractExe(string zipPath, string destination)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var entry = zip.Entries.FirstOrDefault(e => e.FullName == ExeName)
                ?? throw new UserMessageException("Güncelleme paketinde Nimbus.exe yok; güncelleme kurulmadı.");
            entry.ExtractToFile(destination, overwrite: true);
        }
        catch (InvalidDataException)
        {
            throw new UserMessageException("Güncelleme paketi bozuk; güncelleme kurulmadı.");
        }
    }

    // Exe'nin içindeki sürüm imzanın kapsamındadır; eski imzalı exe yeni etiketle sunulsa da kurulmaz.
    public static void VerifyPackage(string exePath, string hashFileText, string signatureText, string publicKey, Version tagVersion, Version currentVersion)
    {
        switch (UpdateSignature.Verify(exePath, hashFileText, signatureText, publicKey))
        {
            case UpdateVerification.HashMismatch:
                throw new UserMessageException("Güncellemenin SHA-256 özeti tutmadı; güncelleme kurulmadı.");
            case UpdateVerification.BadSignature:
                throw new UserMessageException("Güncellemenin imzası doğrulanamadı; güncelleme kurulmadı.");
        }

        var exeVersion = ReadVersion(exePath);
        if (exeVersion != tagVersion || !IsNewer(exeVersion, currentVersion))
            throw new UserMessageException("Güncellemenin sürüm bilgisi tutmadı; güncelleme kurulmadı.");
    }

    public static void Swap(string currentExe, string newExe)
    {
        var old = OldPath(currentExe);
        try
        {
            File.Move(currentExe, old, overwrite: true);
            try
            {
                File.Move(newExe, currentExe);
            }
            catch
            {
                File.Move(old, currentExe);
                throw;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new UserMessageException("Nimbus klasörüne yazılamadığı için güncelleme kurulmadı.");
        }
    }

    public static void Rollback(string currentExe) => File.Move(OldPath(currentExe), currentExe, overwrite: true);

    public static string NewPath(string currentExe) => currentExe + ".new";

    public static string OldPath(string currentExe) => currentExe + ".old";

    // Eski süreç henüz kapanıyor olabilir; silinemezse bir sonraki açılışta yeniden denenir.
    public static async Task DeleteOldAsync(string currentExe)
    {
        LocalFiles.TryDelete(NewPath(currentExe));

        var old = OldPath(currentExe);
        for (var i = 0; i < DeleteAttempts && File.Exists(old); i++)
        {
            LocalFiles.TryDelete(old);
            if (File.Exists(old))
                await Task.Delay(DeleteRetryDelay);
        }
    }

    private static Version ReadVersion(string exePath)
    {
        var text = FileVersionInfo.GetVersionInfo(exePath).ProductVersion?.Split('+')[0];
        return Version.TryParse(text, out var version) ? version : new Version(0, 0, 0);
    }
}
