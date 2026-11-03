using System.Net;
using System.Net.Sockets;
using Google;
using Google.Apis.Auth.OAuth2.Responses;

namespace Nimbus;

internal sealed class UserMessageException(string message) : Exception(message);

internal static class ErrorMessages
{
    public static string For(Exception ex) => ex switch
    {
        UserMessageException => ex.Message,
        GoogleApiException api => ForApiError(api),
        TokenResponseException => Strings.SessionRefreshFailed,
        HttpRequestException or SocketException or TaskCanceledException or IOException => Strings.NoInternet,
        _ => string.Format(Strings.UnexpectedError, ex.GetType().Name),
    };

    private static string ForApiError(GoogleApiException ex)
    {
        var reason = ex.Error?.Errors?.FirstOrDefault()?.Reason;
        return reason switch
        {
            "storageQuotaExceeded" => Strings.DriveQuotaFull,
            "accessNotConfigured" => Strings.DriveNotEnabled,
            "insufficientPermissions" or "insufficientScopes" => Strings.DriveNoPermission,
            "userRateLimitExceeded" or "rateLimitExceeded" => Strings.DriveRateLimited,
            _ when ex.HttpStatusCode == HttpStatusCode.BadRequest => Strings.DriveConvertFailed,
            _ when (int)ex.HttpStatusCode >= 500 => Strings.DriveUnavailable,
            _ => string.Format(Strings.DriveDeniedRequest, (int)ex.HttpStatusCode),
        };
    }
}
