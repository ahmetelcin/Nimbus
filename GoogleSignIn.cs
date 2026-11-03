using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Util.Store;

namespace Nimbus;

internal static class GoogleSignIn
{
    private const string UserId = "user";

    // Tarayıcı kapatılırsa giriş sonsuza dek beklemesin.
    private static readonly TimeSpan LoginTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RevokeTimeout = TimeSpan.FromSeconds(10);

    public static async Task<UserCredential> SignInAsync()
    {
        var secrets = LoadClientSecrets();
        using var timeout = new CancellationTokenSource(LoginTimeout);
        try
        {
            return await GoogleWebAuthorizationBroker.AuthorizeAsync(
                secrets,
                [DriveService.Scope.DriveFile],
                UserId,
                timeout.Token,
                TokenStore());
        }
        catch (OperationCanceledException)
        {
            throw new UserMessageException(Strings.SignInTimeoutError);
        }
        catch (TokenResponseException)
        {
            throw new UserMessageException(Strings.SignInDeniedError);
        }
    }

    public static async Task<bool> HasTokenAsync() => await LoadTokenAsync() is not null;

    // Tarayıcı açmadan kayıtlı oturumu yükler; oturum yoksa null döner.
    public static async Task<UserCredential?> TryLoadCredentialAsync()
    {
        var token = await LoadTokenAsync();
        return token is null ? null : new UserCredential(CreateFlow(), UserId, token);
    }

    // İnternet yoksa izin Google tarafında kalır; yerel token yine de silinir.
    public static async Task<bool> RevokeAndClearAsync()
    {
        var revoked = false;
        try
        {
            if (await LoadTokenAsync() is { } token)
            {
                using var flow = CreateFlow();
                using var timeout = new CancellationTokenSource(RevokeTimeout);
                await flow.RevokeTokenAsync(UserId, token.RefreshToken ?? token.AccessToken, timeout.Token);
                revoked = true;
            }
        }
        catch (Exception ex) when (ex is TokenResponseException or HttpRequestException or OperationCanceledException or IOException)
        {
        }

        await ClearTokenAsync();
        return revoked;
    }

    public static Task ClearTokenAsync() => TokenStore().ClearAsync();

    private static FileDataStore TokenStore() => new(AppPaths.TokenDirectory, fullPath: true);

    private static async Task<TokenResponse?> LoadTokenAsync()
    {
        try
        {
            return await TokenStore().GetAsync<TokenResponse>(UserId);
        }
        catch (Newtonsoft.Json.JsonException)
        {
            return null;
        }
    }

    private static GoogleAuthorizationCodeFlow CreateFlow() => new(new GoogleAuthorizationCodeFlow.Initializer
    {
        ClientSecrets = LoadClientSecrets(),
        Scopes = [DriveService.Scope.DriveFile],
        DataStore = TokenStore(),
    });

    private static ClientSecrets LoadClientSecrets()
    {
        if (!File.Exists(AppPaths.ClientSecretFile))
            throw new UserMessageException(Strings.SecretMissing);

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
            throw new UserMessageException(Strings.SecretInvalid);
        }

        return secrets;
    }
}
