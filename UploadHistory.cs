using System.Text.Json;

namespace Nimbus;

internal sealed record UploadRecord(long Length, long LastWriteUtcTicks, string FileId);

// Hangi yerel dosyanın hangi Google kopyasına karşılık geldiğini tutar.
internal static class UploadHistory
{
    public static string? FindUnchangedCopy(string path)
    {
        var record = Load().GetValueOrDefault(path);
        var file = new FileInfo(path);
        return record is not null && record.Length == file.Length && record.LastWriteUtcTicks == file.LastWriteTimeUtc.Ticks
            ? record.FileId
            : null;
    }

    // Boyut ve tarih yüklemeden önce okunmalı; yükleme sırasında değişen dosya sonraki açılışta yeniden yüklensin.
    public static UploadRecord Capture(string path, string fileId = "")
    {
        var file = new FileInfo(path);
        return new UploadRecord(file.Length, file.LastWriteTimeUtc.Ticks, fileId);
    }

    public static void Save(string path, UploadRecord record)
    {
        var records = Load();
        records[path] = record;

        Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.UploadHistoryFile)!);
        var temp = AppPaths.UploadHistoryFile + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(records));
        File.Move(temp, AppPaths.UploadHistoryFile, overwrite: true);
    }

    private static Dictionary<string, UploadRecord> Load()
    {
        try
        {
            var records = JsonSerializer.Deserialize<Dictionary<string, UploadRecord>>(File.ReadAllText(AppPaths.UploadHistoryFile));
            return new Dictionary<string, UploadRecord>(records ?? [], StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Dictionary<string, UploadRecord>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
