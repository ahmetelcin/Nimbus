using System.Net;
using System.Net.Sockets;
using Google;
using Google.Apis.Auth.OAuth2.Responses;

namespace DriveAc;

internal sealed class UserMessageException(string message) : Exception(message);

internal static class ErrorMessages
{
    private const string NoInternet = "Google'a ulaşılamadı. İnternet bağlantınızı kontrol edin.";

    public static string For(Exception ex) => ex switch
    {
        UserMessageException => ex.Message,
        GoogleApiException api => ForApiError(api),
        TokenResponseException => "Google oturumu yenilenemedi. Dosyayı yeniden açıp giriş yapın.",
        HttpRequestException or SocketException or TaskCanceledException or IOException => NoInternet,
        _ => $"Beklenmeyen bir hata oluştu ({ex.GetType().Name}).",
    };

    private static string ForApiError(GoogleApiException ex)
    {
        var reason = ex.Error?.Errors?.FirstOrDefault()?.Reason;
        return reason switch
        {
            "storageQuotaExceeded" => "Google Drive depolama alanınız dolu.",
            "accessNotConfigured" => "Bu Google Cloud projesinde Drive API etkin değil.",
            "insufficientPermissions" or "insufficientScopes" =>
                "Google girişinde Drive izni verilmedi. Çıkış yapıp izni onaylayarak yeniden giriş yapın.",
            "userRateLimitExceeded" or "rateLimitExceeded" => "Google Drive çok fazla istek aldı. Biraz sonra yeniden deneyin.",
            _ when ex.HttpStatusCode == HttpStatusCode.BadRequest =>
                "Google Drive dosyayı dönüştüremedi. Dosya bozuk, boş veya desteklenen sınırların üzerinde olabilir.",
            _ when (int)ex.HttpStatusCode >= 500 => "Google Drive şu anda yanıt vermiyor. Biraz sonra yeniden deneyin.",
            _ => $"Google Drive isteği reddetti (HTTP {(int)ex.HttpStatusCode}).",
        };
    }
}
