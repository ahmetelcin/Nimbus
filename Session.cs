using Google;
using Google.Apis.Auth.OAuth2;

namespace DriveAc;

internal sealed class Session(Notifier notifier)
{
    public event Action? Changed;

    public bool IsSignedIn { get; private set; }

    public string? Email { get; private set; }

    // Tarayıcı açmaz. Oturum geçersizse TokenResponseException fırlatır.
    public async Task LoadAsync()
    {
        var credential = await GoogleSignIn.TryLoadCredentialAsync();
        if (credential is null)
        {
            SetState(false, null);
            return;
        }

        SetState(true, AppSettings.Load().AccountEmail);
        await RefreshEmailAsync(credential);
    }

    public async Task<UserCredential> GetCredentialAsync()
    {
        if (!await GoogleSignIn.HasTokenAsync())
            notifier.ShowInfo("Google ile giriş yapın", "Giriş sayfası tarayıcınızda açılıyor.");

        var credential = await GoogleSignIn.SignInAsync();
        if (!IsSignedIn || Email is null)
        {
            SetState(true, Email);
            await RefreshEmailAsync(credential);
        }

        return credential;
    }

    public async Task SignOutAsync()
    {
        var revoked = await GoogleSignIn.RevokeAndClearAsync();
        ForgetAccount();
        notifier.ShowInfo(
            "Çıkış yapıldı",
            revoked ? "Google hesabınızın bağlantısı kaldırıldı." : "Bu bilgisayardan çıkış yapıldı, ancak Google'a ulaşılamadığı için izin hesabınızda duruyor.");
    }

    public async Task ExpireAsync()
    {
        await GoogleSignIn.ClearTokenAsync();
        ForgetAccount();
    }

    private async Task RefreshEmailAsync(UserCredential credential)
    {
        try
        {
            using var uploader = new DriveUploader(credential);
            var email = await uploader.GetAccountEmailAsync();
            SaveEmail(email);
            SetState(true, email);
        }
        catch (Exception ex) when (ex is GoogleApiException or HttpRequestException or TaskCanceledException or IOException)
        {
            // E-posta yalnızca menüdeki bilgi satırı için; alınamaması akışı durdurmaz.
        }
    }

    private void ForgetAccount()
    {
        SaveEmail(null);
        SetState(false, null);
    }

    private static void SaveEmail(string? email)
    {
        var settings = AppSettings.Load();
        settings.AccountEmail = email;
        settings.Save();
    }

    private void SetState(bool signedIn, string? email)
    {
        IsSignedIn = signedIn;
        Email = email;
        Changed?.Invoke();
    }
}
