using System.Diagnostics;
using Google.Apis.Auth.OAuth2.Responses;

namespace DriveAc;

internal static class Program
{
    private const string AppTitle = "DriveAç";

    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        if (args.Length != 1)
        {
            ShowError("Açılacak dosyanın yolunu belirtin.\n\nÖrnek: DriveAc.exe \"C:\\Belgeler\\sunum.pptx\"");
            return 2;
        }

        try
        {
            OpenInGoogleAsync(args[0]).GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception ex)
        {
            ShowError(ErrorMessages.For(ex));
            return 1;
        }
    }

    private static async Task OpenInGoogleAsync(string path)
    {
        var fileType = FileTypes.Find(path)
            ?? throw new UserMessageException($"Desteklenmeyen dosya türü: {Path.GetExtension(path)}");

        if (!File.Exists(path))
            throw new UserMessageException($"Dosya bulunamadı: {Path.GetFileName(path)}");

        string link;
        try
        {
            link = await UploadAsync(path, fileType);
        }
        catch (TokenResponseException)
        {
            // Testing modunda refresh token haftada bir geçersiz olur; yeniden giriş normal akıştır.
            await GoogleSignIn.ClearTokenAsync();
            link = await UploadAsync(path, fileType);
        }

        Process.Start(new ProcessStartInfo(link) { UseShellExecute = true });
    }

    private static async Task<string> UploadAsync(string path, FileType fileType)
    {
        var credential = await GoogleSignIn.SignInAsync();
        using var uploader = new DriveUploader(credential);
        return await uploader.UploadAndConvertAsync(path, fileType);
    }

    private static void ShowError(string message) =>
        MessageBox.Show(message, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
}
