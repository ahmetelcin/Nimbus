using Wpf.Ui.Controls;

namespace Nimbus;

// Google'daki düzenlemeleri bilgisayardaki dosyaya geri yazar.
internal sealed class SaveBack(Session session, Notifier notifier)
{
    // Üç düğmeli pencere, en uzun düğme metninin kesilmeyeceği genişlikte açılır.
    private const double ConflictDialogWidth = 640;

    private enum Choice
    {
        Overwrite,
        SaveCopy,
        Cancel,
    }

    public async Task SaveAsync(RecentFile file)
    {
        var name = Path.GetFileName(file.Path);
        var folder = Path.GetDirectoryName(file.Path)!;
        if (!Directory.Exists(folder))
            throw new UserMessageException(string.Format(Strings.FolderNotFound, name, folder));

        var target = FileTypes.SavePath(file.Path);
        LocalFiles.EnsureWritable(target);

        var credential = await session.GetCredentialAsync();
        using var drive = new DriveUploader(credential);
        var state = await drive.GetStateAsync(file.FileId)
            ?? throw new UserMessageException(string.Format(Strings.GoogleCopyMissing, name));

        // Google'ın dönüştürdüğü hâli, ortada değişiklik yokken dosyanın yerine geçmesin.
        if (File.Exists(target)
            && UploadHistory.Find(file.Path) is { } record
            && !SyncRules.GoogleChanged(record, state)
            && !UploadHistory.HasLocalChanges(target))
        {
            notifier.ShowInfo(Strings.SaveAlreadyCurrentTitle, string.Format(Strings.SaveAlreadyCurrentBody, Path.GetFileName(target)));
            return;
        }

        notifier.ShowDownload(name);
        string download;
        try
        {
            download = await DownloadAsync(drive, file);
        }
        finally
        {
            notifier.ClearProgress();
        }

        try
        {
            var isCopy = false;
            var original = target;
            if (UploadHistory.HasLocalChanges(target))
            {
                var choice = await AskAboutConflictAsync(target, isOriginal: target == file.Path);
                if (choice == Choice.Cancel)
                    return;
                if (choice == Choice.SaveCopy)
                {
                    target = LocalFiles.CopyPathFor(target);
                    isCopy = true;
                }
            }

            var replacesFile = File.Exists(target);
            Apply(download, target, file, state.ModifiedUtcTicks, automatic: false);
            if (isCopy)
                UploadHistory.Remove(original);
            ShowResult(file.Path, target, isCopy, replacesFile);
        }
        finally
        {
            LocalFiles.TryDelete(download);
        }
    }

    internal static async Task<string> DownloadAsync(DriveUploader drive, RecentFile file)
    {
        var target = FileTypes.SavePath(file.Path);
        var download = Path.Combine(Path.GetTempPath(), $"nimbus-{Guid.NewGuid():N}{Path.GetExtension(target)}");
        try
        {
            await drive.ExportToFileAsync(file.FileId, FileTypes.ExportMimeType(file.Path), download);
            LocalFiles.EnsureComplete(download, Path.GetFileName(target));
            return download;
        }
        catch
        {
            LocalFiles.TryDelete(download);
            throw;
        }
    }

    // googleTicks indirmeden önce okunmalı; indirme sırasında yapılan düzenleme sonraki turda yakalansın.
    internal static void Apply(string download, string target, RecentFile file, long googleTicks, bool automatic)
    {
        if (File.Exists(target))
        {
            LocalFiles.EnsureWritable(target);
            if (automatic)
                LocalFiles.BackupUnlessRecent(target);
            else
                LocalFiles.Backup(target);
        }

        LocalFiles.Replace(download, target);

        var record = UploadHistory.Capture(target, file.FileId, googleTicks);
        // Otomatik yazma "açılma" sayılmaz; yoksa belge 24 saatlik pencereden hiç çıkmaz.
        UploadHistory.Save(target, automatic ? record with { LastOpenedUtcTicks = file.OpenedUtc.Ticks } : record);
        UploadHistory.MarkSynced(file.FileId, googleTicks);
    }

    private static async Task<Choice> AskAboutConflictAsync(string target, bool isOriginal)
    {
        var name = Path.GetFileName(target);
        var dialog = new MessageBox
        {
            Title = Strings.SaveConflictTitle,
            Content = string.Format(isOriginal ? Strings.SaveConflictOriginal : Strings.SaveConflictExisting, name),
            Width = ConflictDialogWidth,
            MaxWidth = ConflictDialogWidth,
            PrimaryButtonText = Strings.SaveConflictOverwrite,
            SecondaryButtonText = Strings.SaveConflictCopy,
            CloseButtonText = Strings.Cancel,
        };

        return await dialog.ShowDialogAsync() switch
        {
            MessageBoxResult.Primary => Choice.Overwrite,
            MessageBoxResult.Secondary => Choice.SaveCopy,
            _ => Choice.Cancel,
        };
    }

    private void ShowResult(string original, string target, bool isCopy, bool replacedFile)
    {
        var name = Path.GetFileName(target);
        if (isCopy)
        {
            notifier.ShowInfo(Strings.SaveCopyTitle, string.Format(Strings.SaveCopyBody, name));
        }
        else if (target != original)
        {
            notifier.ShowInfo(
                Strings.SaveLegacyTitle,
                string.Format(Strings.SaveLegacyBody, Path.GetFileName(original), name));
        }
        else
        {
            var backup = replacedFile ? Strings.SaveBackupNote : "";
            var csv = Path.GetExtension(target).Equals(".csv", StringComparison.OrdinalIgnoreCase) ? Strings.SaveCsvNote : "";
            notifier.ShowInfo(Strings.SaveDoneTitle, string.Format(Strings.SaveDoneBody, name, backup, csv));
        }
    }
}
