using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Util.Store;

namespace DriveAc;

internal static class GoogleSignIn
{
    // Tarayıcı kapatılırsa görünmez süreç sonsuza dek beklemesin.
    private static readonly TimeSpan LoginTimeout = TimeSpan.FromMinutes(5);

    public static async Task<UserCredential> SignInAsync()
    {
        var secrets = LoadClientSecrets();
        using var timeout = new CancellationTokenSource(LoginTimeout);
        try
        {
            return await GoogleWebAuthorizationBroker.AuthorizeAsync(
                secrets,
                [DriveService.Scope.DriveFile],
                "user",
                timeout.Token,
                TokenStore());
        }
        catch (OperationCanceledException)
        {
            throw new UserMessageException("Google girişi 5 dakika içinde tamamlanmadı. Dosyayı yeniden açmayı deneyin.");
        }
        catch (TokenResponseException)
        {
            throw new UserMessageException("Google girişi tamamlanamadı veya izin verilmedi.");
        }
    }

    public static Task ClearTokenAsync() => TokenStore().ClearAsync();

    private static FileDataStore TokenStore() => new(AppPaths.TokenDirectory, fullPath: true);

    private static ClientSecrets LoadClientSecrets()
    {
        if (!File.Exists(AppPaths.ClientSecretFile))
            throw new UserMessageException("client_secret.json bulunamadı. Dosya, DriveAc.exe ile aynı klasörde olmalı.");

        ClientSecrets? secrets = null;
        try
        {
            secrets = GoogleClientSecrets.FromFile(AppPaths.ClientSecretFile).Secrets;
        }
        catch (Exception)
        {
            // İçerik sızmasın diye özgün hata metni gösterilmez.
        }

        if (string.IsNullOrWhiteSpace(secrets?.ClientId)
            || string.IsNullOrWhiteSpace(secrets.ClientSecret)
            || secrets.ClientId.StartsWith("BURAYA", StringComparison.Ordinal))
        {
            throw new UserMessageException("client_secret.json geçersiz. Dosyayı şablona göre doldurun.");
        }

        return secrets;
    }
}
