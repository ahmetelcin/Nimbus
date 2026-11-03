using Google.Apis.Auth.OAuth2.Responses;

namespace Nimbus;

// Son açılan belgelerdeki Google düzenlemelerini arka planda yerel dosyaya yazar.
internal sealed class AutoSync(Session session, Notifier notifier, Func<Func<Task>, Task<bool>> enqueue)
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan OpenedWindow = TimeSpan.FromHours(24);

    // Yanıtlanmamış çakışma için her turda yeni bildirim çıkmaz.
    private readonly HashSet<string> _notifiedConflicts = [];

    public async Task RunAsync(CancellationToken cancellation)
    {
        using var timer = new PeriodicTimer(CheckInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellation))
                await enqueue(RunRoundAsync);
        }
        catch (OperationCanceledException)
        {
        }
    }

    internal async Task RunRoundAsync()
    {
        if (!AppSettings.Load().AutoSaveBack || !session.IsSignedIn)
            return;

        var cutoff = DateTime.UtcNow - OpenedWindow;
        var files = UploadHistory.Recent(int.MaxValue).Where(file => file.OpenedUtc >= cutoff).ToList();
        if (files.Count == 0)
            return;

        try
        {
            var credential = await session.GetCredentialAsync();
            using var drive = new DriveUploader(credential);
            foreach (var file in files)
            {
                try
                {
                    await SyncAsync(drive, file);
                }
                catch (Exception ex) when (ex is not TokenResponseException)
                {
                    // Kilitli dosya, internet kesintisi vb.: sonraki turda yeniden denenir.
                }
            }
        }
        catch (Exception ex) when (ex is not TokenResponseException)
        {
        }
    }

    private async Task SyncAsync(DriveUploader drive, RecentFile file)
    {
        if (!File.Exists(file.Path)
            || UploadHistory.Find(file.Path) is not { } record
            || await drive.GetStateAsync(file.FileId) is not { } state)
            return;

        // Çakışma başka yoldan çözüldüyse (elle kaydetme) sonraki çakışma yeniden bildirilir.
        if (!SyncRules.GoogleChanged(record, state))
            _notifiedConflicts.Remove(file.FileId);

        var target = FileTypes.SavePath(file.Path);
        switch (SyncRules.Decide(record, state, UploadHistory.HasLocalChanges(target), DateTime.UtcNow))
        {
            case SyncAction.Export:
                LocalFiles.EnsureWritable(target);
                var download = await SaveBack.DownloadAsync(drive, file);
                try
                {
                    SaveBack.Apply(download, target, file, state.ModifiedUtcTicks, automatic: true);
                }
                finally
                {
                    LocalFiles.TryDelete(download);
                }

                notifier.ShowQuietInfo(
                    file.FileId,
                    "Otomatik kaydedildi",
                    $"{Path.GetFileName(target)}, Google'daki sürümle güncellendi.");
                break;

            case SyncAction.Conflict when _notifiedConflicts.Add(file.FileId):
                notifier.ShowConflict(file.FileId, Path.GetFileName(target), choice => _ = enqueue(() => ResolveAsync(file, choice)));
                break;
        }
    }

    private async Task ResolveAsync(RecentFile file, ConflictChoice choice)
    {
        _notifiedConflicts.Remove(file.FileId);

        var credential = await session.GetCredentialAsync();
        using var drive = new DriveUploader(credential);
        var state = await drive.GetStateAsync(file.FileId)
            ?? throw new UserMessageException($"{Path.GetFileName(file.Path)} dosyasının Google kopyası bulunamadı.");

        if (choice == ConflictChoice.KeepLocal)
        {
            UploadHistory.MarkSynced(file.FileId, state.ModifiedUtcTicks);
            return;
        }

        var target = FileTypes.SavePath(file.Path);
        var keepBoth = choice == ConflictChoice.KeepBoth;
        var destination = keepBoth ? LocalFiles.CopyPathFor(target) : target;
        LocalFiles.EnsureWritable(destination);

        var download = await SaveBack.DownloadAsync(drive, file);
        try
        {
            SaveBack.Apply(download, destination, file, state.ModifiedUtcTicks, automatic: false);
        }
        finally
        {
            LocalFiles.TryDelete(download);
        }

        if (keepBoth)
        {
            // Belge artık kopyaya bağlı; yerel dosya yeniden açılınca kendi Google kopyasını alır.
            UploadHistory.Remove(target);
            notifier.ShowInfo("Kopya kaydedildi", $"Google'daki sürüm {Path.GetFileName(destination)} olarak kaydedildi. Mevcut dosya değişmedi.");
        }
        else
        {
            notifier.ShowInfo("Bilgisayara kaydedildi", $"{Path.GetFileName(target)}, Google'daki sürümle güncellendi. Önceki sürüm yedeklendi.");
        }
    }
}
