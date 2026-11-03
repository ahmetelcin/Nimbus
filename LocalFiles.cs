using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Nimbus;

// Kullanıcının dosyasına yazmadan önceki güvenlik adımları.
internal static class LocalFiles
{
    private const int MaxBackupsPerFile = 5;
    private const string BackupTimeFormat = "yyyyMMdd-HHmmss-fff";
    private static readonly TimeSpan AutoBackupInterval = TimeSpan.FromHours(1);

    public static void EnsureWritable(string path)
    {
        var name = Path.GetFileName(path);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // Henüz var olmayan dosyayı kilitleyen bir program da yoktur.
        }
        catch (UnauthorizedAccessException)
        {
            throw new UserMessageException($"{name} dosyasına yazma izni yok. Dosya salt okunur olabilir.");
        }
        catch (IOException)
        {
            throw new UserMessageException($"{name} başka bir programda açık. Dosyayı kapatıp yeniden deneyin.");
        }
    }

    // İndirilen şey bir hata sayfası ya da boş dosyaysa özgün dosyanın üzerine yazılmamalı.
    public static void EnsureComplete(string download, string name)
    {
        using var stream = File.OpenRead(download);
        var isValid = Path.GetExtension(download).ToLowerInvariant() switch
        {
            ".csv" => true, // Boş bir sayfa boş CSV olarak gelir.
            ".rtf" => stream.Length > 0,
            _ => stream.ReadByte() == 'P' && stream.ReadByte() == 'K', // Office ve OpenDocument biçimleri zip tabanlıdır.
        };
        if (!isValid)
            throw new UserMessageException($"{name} Google'dan eksik indirildi. Dosyaya dokunulmadı; yeniden deneyin.");
    }

    // Otomatik kayıt birkaç dakikada bir çalışır; her turda yedeklense 5 yedek özgün dosyayı hızla siler.
    // Atlanan sürüm yerelde değişmemiştir, yani Google'ın sürüm geçmişinde durur.
    public static void BackupUnlessRecent(string path)
    {
        if (!HasRecentBackup(Path.Combine(AppPaths.BackupDirectory, BackupFolderName(path))))
            Backup(path);
    }

    public static void Backup(string path)
    {
        var folder = Path.Combine(AppPaths.BackupDirectory, BackupFolderName(path));
        try
        {
            Directory.CreateDirectory(folder);
            File.Copy(path, Path.Combine(folder, DateTime.Now.ToString(BackupTimeFormat, CultureInfo.InvariantCulture) + Path.GetExtension(path)), overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new UserMessageException($"{Path.GetFileName(path)} yedeklenemediği için dosyaya dokunulmadı.");
        }

        PruneBackups(folder);
    }

    // Önce hedefin yanına kopyalanır; yarıda kalan bir yazma özgün dosyayı bozmasın.
    public static void Replace(string downloaded, string target)
    {
        var temp = Path.Combine(Path.GetDirectoryName(target)!, $"~nimbus-{Guid.NewGuid():N}.tmp");
        try
        {
            File.Copy(downloaded, temp);
            if (File.Exists(target))
                File.Replace(temp, target, destinationBackupFileName: null);
            else
                File.Move(temp, target);
        }
        catch (UnauthorizedAccessException)
        {
            throw new UserMessageException($"{Path.GetFileName(target)} kaydedilemedi: bu konuma yazma izni yok.");
        }
        catch (IOException)
        {
            throw new UserMessageException($"{Path.GetFileName(target)} kaydedilemedi. Dosya başka bir programda açık ya da disk dolu olabilir.");
        }
        finally
        {
            TryDelete(temp);
        }
    }

    public static string CopyPathFor(string target)
    {
        var folder = Path.GetDirectoryName(target)!;
        var name = Path.GetFileNameWithoutExtension(target);
        var extension = Path.GetExtension(target);

        var candidate = Path.Combine(folder, $"{name} (Nimbus kopyası){extension}");
        for (var i = 2; File.Exists(candidate); i++)
            candidate = Path.Combine(folder, $"{name} (Nimbus kopyası {i}){extension}");
        return candidate;
    }

    public static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    // Aynı ada sahip farklı klasörlerdeki dosyaların yedekleri karışmasın diye yolun özeti eklenir.
    private static string BackupFolderName(string path)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant()));
        return $"{Path.GetFileName(path)}-{Convert.ToHexStringLower(hash)[..8]}";
    }

    private static bool HasRecentBackup(string folder)
    {
        if (!Directory.Exists(folder))
            return false;

        return Directory.GetFiles(folder).Any(file =>
            DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file), BackupTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            && DateTime.Now - time < AutoBackupInterval);
    }

    private static void PruneBackups(string folder)
    {
        var old = new DirectoryInfo(folder).GetFiles()
            .OrderByDescending(f => f.Name, StringComparer.Ordinal)
            .Skip(MaxBackupsPerFile);
        foreach (var file in old)
            TryDelete(file.FullName);
    }
}
