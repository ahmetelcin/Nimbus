using System.Diagnostics;
using Google.Apis.Auth.OAuth2.Responses;

namespace Nimbus;

internal sealed class FileOpener(Session session, Notifier notifier)
{
    public async Task OpenAsync(string path)
    {
        var fileType = FileTypes.Find(path)
            ?? throw new UserMessageException($"Desteklenmeyen dosya türü: {Path.GetExtension(path)}");

        if (!File.Exists(path))
            throw new UserMessageException($"Dosya bulunamadı: {Path.GetFileName(path)}");

        try
        {
            string link;
            try
            {
                link = await GetLinkAsync(path, fileType);
            }
            catch (TokenResponseException)
            {
                // Testing modunda refresh token haftada bir geçersiz olur; yeniden giriş normal akıştır.
                notifier.ClearProgress();
                await session.ExpireAsync();
                notifier.ShowInfo("Oturumunuz sona erdi", "Devam etmek için yeniden giriş yapın.");
                link = await GetLinkAsync(path, fileType);
            }

            OpenInBrowser(link);
        }
        finally
        {
            notifier.ClearProgress();
        }
    }

    private async Task<string> GetLinkAsync(string path, FileType fileType)
    {
        var credential = await session.GetCredentialAsync();
        using var uploader = new DriveUploader(credential);

        if (UploadHistory.FindUnchangedCopy(path) is { } fileId
            && await uploader.GetExistingLinkAsync(fileId) is { } existingLink)
        {
            notifier.ShowInfo("Önceki kopya açılıyor", $"{Path.GetFileName(path)} değişmediği için yeniden yüklenmedi.");
            return existingLink;
        }

        var snapshot = UploadHistory.Capture(path);
        notifier.ShowProgress(Path.GetFileName(path));
        var uploaded = await uploader.UploadAndConvertAsync(path, fileType, notifier.UpdateProgress);
        UploadHistory.Save(path, snapshot with { FileId = uploaded.Id });
        return uploaded.WebViewLink;
    }

    private static void OpenInBrowser(string link)
    {
        if (AppSettings.Load().BrowserPath is { } browser && File.Exists(browser))
        {
            var startInfo = new ProcessStartInfo(browser) { UseShellExecute = false };
            startInfo.ArgumentList.Add(link);
            Process.Start(startInfo);
        }
        else
        {
            Process.Start(new ProcessStartInfo(link) { UseShellExecute = true });
        }
    }
}
