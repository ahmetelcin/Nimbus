using System.Diagnostics;
using Google.Apis.Auth.OAuth2.Responses;

namespace DriveAc;

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
                link = await UploadAsync(path, fileType);
            }
            catch (TokenResponseException)
            {
                // Testing modunda refresh token haftada bir geçersiz olur; yeniden giriş normal akıştır.
                notifier.ClearProgress();
                await session.ExpireAsync();
                notifier.ShowInfo("Oturumunuz sona erdi", "Devam etmek için yeniden giriş yapın.");
                link = await UploadAsync(path, fileType);
            }

            Process.Start(new ProcessStartInfo(link) { UseShellExecute = true });
        }
        finally
        {
            notifier.ClearProgress();
        }
    }

    private async Task<string> UploadAsync(string path, FileType fileType)
    {
        var credential = await session.GetCredentialAsync();
        notifier.ShowProgress(Path.GetFileName(path));
        using var uploader = new DriveUploader(credential);
        return await uploader.UploadAndConvertAsync(path, fileType, notifier.UpdateProgress);
    }
}
