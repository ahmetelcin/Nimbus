using System.Text.Json;

namespace Nimbus;

// SyncedGoogleTicks: yerel dosyanın eşit olduğu son Google sürümünün değişiklik zamanı.
internal sealed record UploadRecord(long Length, long LastWriteUtcTicks, string FileId, long LastOpenedUtcTicks = 0, long SyncedGoogleTicks = 0);

internal sealed record RecentFile(string Path, string FileId, DateTime OpenedUtc)
{
    public string OpenedAgo(DateTime utcNow)
    {
        var elapsed = utcNow - OpenedUtc;
        return elapsed switch
        {
            { TotalMinutes: < 1 } => Strings.TimeJustNow,
            { TotalMinutes: < 2 } => Strings.TimeMinuteAgo,
            { TotalHours: < 1 } => string.Format(Strings.TimeMinutesAgo, (int)elapsed.TotalMinutes),
            { TotalHours: < 2 } => Strings.TimeHourAgo,
            { TotalDays: < 1 } => string.Format(Strings.TimeHoursAgo, (int)elapsed.TotalHours),
            { TotalDays: < 2 } => Strings.TimeYesterday,
            _ => string.Format(Strings.TimeDaysAgo, (int)elapsed.TotalDays),
        };
    }
}

// Hangi yerel dosyanın hangi Google kopyasına karşılık geldiğini tutar.
internal static class UploadHistory
{
    public static UploadRecord? Find(string path) => Load().GetValueOrDefault(path);

    public static string? FindUnchangedCopy(string path)
    {
        var record = Load().GetValueOrDefault(path);
        var file = new FileInfo(path);
        return record is not null && record.Length == file.Length && record.LastWriteUtcTicks == file.LastWriteTimeUtc.Ticks
            ? record.FileId
            : null;
    }

    // Kaydı olmayan ya da kayıttan sonra değişen dosya, üzerine yazılırsa kaybolacak içerik taşır.
    public static bool HasLocalChanges(string path) => File.Exists(path) && FindUnchangedCopy(path) is null;

    // Boyut ve tarih yüklemeden önce okunmalı; yükleme sırasında değişen dosya sonraki açılışta yeniden yüklensin.
    public static UploadRecord Capture(string path, string fileId = "", long syncedGoogleTicks = 0)
    {
        var file = new FileInfo(path);
        return new UploadRecord(file.Length, file.LastWriteTimeUtc.Ticks, fileId, DateTime.UtcNow.Ticks, syncedGoogleTicks);
    }

    public static void Save(string path, UploadRecord record)
    {
        var records = Load();
        records[path] = record;
        Write(records);
    }

    public static void MarkOpened(string path)
    {
        var records = Load();
        if (records.TryGetValue(path, out var record))
        {
            records[path] = record with { LastOpenedUtcTicks = DateTime.UtcNow.Ticks };
            Write(records);
        }
    }

    // Aynı belgeye bağlı tüm yollar (örneğin .ppt ve .pptx) güncellenir.
    public static void MarkSynced(string fileId, long googleTicks)
    {
        var records = Load();
        foreach (var (path, record) in records.Where(entry => entry.Value.FileId == fileId).ToList())
            records[path] = record with { SyncedGoogleTicks = googleTicks };
        Write(records);
    }

    public static void Remove(string path)
    {
        var records = Load();
        if (records.Remove(path))
            Write(records);
    }

    // Aynı Google belgesine bağlı yollardan (örneğin .ppt ve .pptx) yalnızca en son kullanılan listelenir.
    public static IReadOnlyList<RecentFile> Recent(int count) =>
        Load()
            .Select(entry => new RecentFile(
                entry.Key,
                entry.Value.FileId,
                new DateTime(entry.Value.LastOpenedUtcTicks > 0 ? entry.Value.LastOpenedUtcTicks : entry.Value.LastWriteUtcTicks, DateTimeKind.Utc)))
            .OrderByDescending(file => file.OpenedUtc)
            .DistinctBy(file => file.FileId)
            .Take(count)
            .ToList();

    private static void Write(Dictionary<string, UploadRecord> records)
    {
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
