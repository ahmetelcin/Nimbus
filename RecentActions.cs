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
        notifier.ShowOptional(OptionalNotification.LinkCopied, "Bağlantı kopyalandı", $"{Path.GetFileName(file.Path)}: yalnızca erişim izni olanlar açabilir.");
    }

    public async Task CopyPublicLinkAsync(RecentFile file)
    {
        var name = Path.GetFileName(file.Path);
        var dialog = new Wpf.Ui.Controls.MessageBox
        {
            Title = "Bağlantıyı herkese aç",
            Content = $"{name} için bağlantıya sahip herkes belgeyi görüntüleyebilecek. Bu izni daha sonra Google Drive'daki paylaşım ayarlarından kaldırabilirsiniz.",
            Width = ShareDialogWidth,
            MaxWidth = ShareDialogWidth,
            PrimaryButtonText = "Herkese aç ve kopyala",
            CloseButtonText = "Vazgeç",
        };
        if (await dialog.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary)
            return;

        using var drive = new DriveUploader(await session.GetCredentialAsync());
        var state = await GetStateAsync(drive, file);
        await drive.ShareWithAnyoneAsync(file.FileId);
        CopyToClipboard(state.Link);
        notifier.ShowOptional(OptionalNotification.LinkCopied, "Bağlantı kopyalandı", $"{name}: bağlantıya sahip herkes görüntüleyebilir.");
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
            throw new UserMessageException($"{Path.GetFileName(file.Path)} dosyasının klasörü bulunamadı: {folder}");

        Process.Start("explorer.exe", $"\"{folder}\"");
    }

    private static async Task<GoogleState> GetStateAsync(DriveUploader drive, RecentFile file) =>
        await drive.GetStateAsync(file.FileId)
            ?? throw new UserMessageException($"{Path.GetFileName(file.Path)} dosyasının Google kopyası bulunamadı. Silinmiş, çöp kutusuna taşınmış ya da başka bir hesapta olabilir.");

    private static void CopyToClipboard(string text)
    {
        try
        {
            // SetDataObject, pano başka bir programdayken kısa süre yeniden dener.
            Clipboard.SetDataObject(text, copy: true);
        }
        catch (ExternalException)
        {
            throw new UserMessageException("Pano başka bir program tarafından kullanılıyor. Yeniden deneyin.");
        }
    }
}
