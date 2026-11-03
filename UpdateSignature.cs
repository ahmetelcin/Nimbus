using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace Nimbus;

internal enum UpdateVerification
{
    Valid,
    HashMismatch,
    BadSignature,
}

// .NET'te yerleşik Ed25519 yoktur; imza exe'nin kendisini değil SHA-256 özetini kapsar,
// böylece 170 MB'lık dosya belleğe alınmaz ve özet imzaya bağlanır.
internal static class UpdateSignature
{
    private const string Domain = "nimbus-update-v1\n";

    public static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    public static byte[] MessageFor(string hash) => Encoding.UTF8.GetBytes(Domain + hash);

    public static UpdateVerification Verify(string exePath, string hashFileText, string signatureText, string publicKey)
    {
        var hash = HashFile(exePath);
        var expected = hashFileText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (!string.Equals(hash, expected, StringComparison.OrdinalIgnoreCase))
            return UpdateVerification.HashMismatch;

        try
        {
            var signer = new Ed25519Signer();
            signer.Init(false, new Ed25519PublicKeyParameters(Convert.FromBase64String(publicKey)));
            var message = MessageFor(hash);
            signer.BlockUpdate(message, 0, message.Length);
            return signer.VerifySignature(Convert.FromBase64String(signatureText.Trim()))
                ? UpdateVerification.Valid
                : UpdateVerification.BadSignature;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return UpdateVerification.BadSignature;
        }
    }
}
