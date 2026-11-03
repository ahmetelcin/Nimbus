using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace Nimbus;

// Son dosyalar menüsündeki belge işlemleri.
internal sealed class RecentActions(Session session, Notifier notifier)
{
    private const double ShareDialogWidth = 520;

    public async Task OpenAsync(RecentFile file)
    {
        using var drive = new DriveUploader(await session.GetCredentialAsync());
        var state = await GetStateAsync(drive, file);
        UploadHistory.MarkOpened(file.Path);
        FileOpener.OpenInBrowser(state.Link);
    }

    public async Task CopyPrivateLinkAsync(RecentFile file)
    {
        using var drive = new DriveUploader(await session.GetCredentialAsync());
        var state = await GetStateAsync(drive, file);
        CopyToClipboard(state.Link);
        notifier.ShowOptional(OptionalNotification.LinkCopied, Strings.LinkCopiedTitle, string.Format(Strings.LinkCopiedPrivate, Path.GetFileName(file.Path)));
    }

    public async Task CopyPublicLinkAsync(RecentFile file)
    {
        var name = Path.GetFileName(file.Path);
        var dialog = new Wpf.Ui.Controls.MessageBox
        {
            Title = Strings.ShareDialogTitle,
            Content = string.Format(Strings.ShareDialogBody, name),
            Width = ShareDialogWidth,
            MaxWidth = ShareDialogWidth,
            PrimaryButtonText = Strings.ShareDialogPrimary,
            CloseButtonText = Strings.Cancel,
        };
        if (await dialog.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary)
            return;

        using var drive = new DriveUploader(await session.GetCredentialAsync());
        var state = await GetStateAsync(drive, file);
        await drive.ShareWithAnyoneAsync(file.FileId);
        CopyToClipboard(state.Link);
        notifier.ShowOptional(OptionalNotification.LinkCopied, Strings.LinkCopiedTitle, string.Format(Strings.LinkCopiedAnyone, name));
    }

    public async Task StopSharingAsync(RecentFile file)
    {
        using var drive = new DriveUploader(await session.GetCredentialAsync());
        await GetStateAsync(drive, file);
        var name = Path.GetFileName(file.Path);
        if (await drive.StopSharingWithAnyoneAsync(file.FileId))
            notifier.ShowInfo(Strings.SharingStoppedTitle, string.Format(Strings.SharingStoppedBody, name));
        else
            notifier.ShowInfo(Strings.SharingAlreadyOffTitle, string.Format(Strings.SharingAlreadyOffBody, name));
    }

    public static void ShowInFolder(RecentFile file)
    {
        if (File.Exists(file.Path))
        {
            Process.Start("explorer.exe", $"/select,\"{file.Path}\"");
            return;
        }

        var folder = Path.GetDirectoryName(file.Path)!;
        if (!Directory.Exists(folder))
            throw new UserMessageException(string.Format(Strings.FolderNotFound, Path.GetFileName(file.Path), folder));

        Process.Start("explorer.exe", $"\"{folder}\"");
    }

    private static async Task<GoogleState> GetStateAsync(DriveUploader drive, RecentFile file) =>
        await drive.GetStateAsync(file.FileId)
            ?? throw new UserMessageException(string.Format(Strings.GoogleCopyMissing, Path.GetFileName(file.Path)));

    private static void CopyToClipboard(string text)
    {
        try
        {
            // SetDataObject, pano başka bir programdayken kısa süre yeniden dener.
            Clipboard.SetDataObject(text, copy: true);
        }
        catch (ExternalException)
        {
            throw new UserMessageException(Strings.ClipboardBusy);
        }
    }
}
