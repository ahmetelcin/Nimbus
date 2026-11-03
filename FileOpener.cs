using System.Diagnostics;
using Google.Apis.Auth.OAuth2.Responses;

namespace Nimbus;

internal sealed class FileOpener(Session session, Notifier notifier)
{
    public async Task OpenAsync(string path)
    {
        var fileType = FileTypes.Find(path)
            ?? throw new UserMessageException(string.Format(Strings.UnsupportedFileType, Path.GetExtension(path)));

        if (!File.Exists(path))
            throw new UserMessageException(string.Format(Strings.FileNotFound, Path.GetFileName(path)));

        try
        {
            string? link;
            try
            {
                link = await GetLinkAsync(path, fileType);
            }
            catch (TokenResponseException)
            {
                // Testing modunda refresh token haftada bir geçersiz olur; yeniden giriş normal akıştır.
                notifier.ClearProgress();
                await session.ExpireAsync();
                notifier.ShowInfo(Strings.SessionExpiredTitle, Strings.SessionExpiredBody);
                link = await GetLinkAsync(path, fileType);
            }

            if (link is not null)
                OpenInBrowser(link);
        }
        finally
        {
            notifier.ClearProgress();
        }
    }

    // Kullanıcı seçim penceresini kapatırsa null döner.
    private async Task<string?> GetLinkAsync(string path, FileType fileType)
    {
        var credential = await session.GetCredentialAsync();
        using var uploader = new DriveUploader(credential);

        if (UploadHistory.Find(path) is { } record && await uploader.GetStateAsync(record.FileId) is { } state)
        {
            if (SyncRules.GoogleChanged(record, state))
            {
                var choice = await ChooseReopenAsync(Path.GetFileName(path));
                if (choice is null)
                    return null;

                if (choice == ReopenChoice.Continue)
                {
                    UploadHistory.MarkOpened(path);
                    return state.Link;
                }
            }
            else if (UploadHistory.FindUnchangedCopy(path) is not null)
            {
                notifier.ShowOptional(OptionalNotification.Opened, Strings.OpenedPreviousTitle, string.Format(Strings.OpenedPreviousBody, Path.GetFileName(path)));
                UploadHistory.MarkOpened(path);
                return state.Link;
            }
        }

        var snapshot = UploadHistory.Capture(path);
        notifier.ShowProgress(Path.GetFileName(path));
        var uploaded = await uploader.UploadAndConvertAsync(path, fileType, notifier.UpdateProgress);
        UploadHistory.Save(path, snapshot with { FileId = uploaded.Id, SyncedGoogleTicks = uploaded.ModifiedUtcTicks });
        return uploaded.WebViewLink;
    }

    private static async Task<ReopenChoice?> ChooseReopenAsync(string fileName)
    {
        var saved = AppSettings.Load();
        if (saved.ReopenChoice != ReopenChoice.Ask)
            return saved.ReopenChoice;

        var window = new ReopenWindow(fileName);
        var closed = new TaskCompletionSource();
        window.Closed += (_, _) => closed.SetResult();
        window.Show();
        window.Activate();
        await closed.Task;

        if (window.Choice is { } choice && window.DontAskAgain)
        {
            var settings = AppSettings.Load();
            settings.ReopenChoice = choice;
            settings.Save();
        }

        return window.Choice;
    }

    internal static event Action? DocumentOpened;

    internal static void OpenInBrowser(string link)
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

        DocumentOpened?.Invoke();
    }
}
