using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;

internal sealed class KeyFileException(string message) : Exception(message);

// The private key on disk: AES-256-GCM, with the AES key derived from the passphrase by Argon2id.
internal static class KeyFile
{
    private const string FormatName = "nimbus-signing-key-v1";
    private const int MemoryKiB = 256 * 1024;
    private const int Iterations = 3;
    private const int Parallelism = 4;
    private const int MinMemoryKiB = 64 * 1024;
    private const int MaxMemoryKiB = 4 * 1024 * 1024;
    private const int MaxIterations = 100;
    private const int MaxParallelism = 64;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;

    private sealed record Contents(string Format, int MemoryKiB, int Iterations, int Parallelism, string Salt, string Nonce, string Ciphertext, string Tag);

    public static void Write(string path, byte[] privateKey, string passphrase)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[privateKey.Length];
        var tag = new byte[TagSize];

        var aesKey = DeriveKey(passphrase, salt, MemoryKiB, Iterations, Parallelism);
        try
        {
            using var aes = new AesGcm(aesKey, TagSize);
            aes.Encrypt(nonce, privateKey, ciphertext, tag, AssociatedData(MemoryKiB, Iterations, Parallelism, salt));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(aesKey);
        }

        var contents = new Contents(
            FormatName, MemoryKiB, Iterations, Parallelism,
            Convert.ToBase64String(salt), Convert.ToBase64String(nonce),
            Convert.ToBase64String(ciphertext), Convert.ToBase64String(tag));

        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(file, contents);
    }

    public static byte[] Read(string path, string passphrase)
    {
        Contents contents;
        byte[] salt, nonce, ciphertext, tag;
        try
        {
            contents = JsonSerializer.Deserialize<Contents>(File.ReadAllText(path))
                ?? throw new JsonException();
            if (contents.Format != FormatName)
                throw new JsonException();
            salt = Convert.FromBase64String(contents.Salt);
            nonce = Convert.FromBase64String(contents.Nonce);
            ciphertext = Convert.FromBase64String(contents.Ciphertext);
            tag = Convert.FromBase64String(contents.Tag);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentNullException)
        {
            throw new KeyFileException($"{path} isn't a passphrase-protected key. Delete it and run keygen again.");
        }

        // A weakened file must not make brute-forcing cheaper, and a huge value must not exhaust memory.
        if (contents.MemoryKiB is < MinMemoryKiB or > MaxMemoryKiB
            || contents.Iterations is < 1 or > MaxIterations
            || contents.Parallelism is < 1 or > MaxParallelism)
            throw new KeyFileException($"{path} has invalid key settings.");

        var aesKey = DeriveKey(passphrase, salt, contents.MemoryKiB, contents.Iterations, contents.Parallelism);
        try
        {
            using var aes = new AesGcm(aesKey, TagSize);
            var privateKey = new byte[ciphertext.Length];
            aes.Decrypt(nonce, ciphertext, tag, privateKey, AssociatedData(contents.MemoryKiB, contents.Iterations, contents.Parallelism, salt));
            return privateKey;
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            throw new KeyFileException("Wrong passphrase, or the key file was changed.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(aesKey);
        }
    }

    private static byte[] AssociatedData(int memoryKiB, int iterations, int parallelism, byte[] salt) =>
        Encoding.UTF8.GetBytes($"{FormatName}|{memoryKiB}|{iterations}|{parallelism}|{Convert.ToBase64String(salt)}");

    private static byte[] DeriveKey(string passphrase, byte[] salt, int memoryKiB, int iterations, int parallelism)
    {
        var parameters = new Argon2Parameters.Builder(Argon2Parameters.Argon2id)
            .WithVersion(Argon2Parameters.Version13)
            .WithSalt(salt)
            .WithMemoryAsKB(memoryKiB)
            .WithIterations(iterations)
            .WithParallelism(parallelism)
            .Build();

        var generator = new Argon2BytesGenerator();
        generator.Init(parameters);

        // The same passphrase must give the same bytes whichever way its letters were typed.
        var passphraseBytes = Encoding.UTF8.GetBytes(passphrase.Normalize(NormalizationForm.FormC));
        var key = new byte[KeySize];
        try
        {
            generator.GenerateBytes(passphraseBytes, key);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passphraseBytes);
        }

        return key;
    }
}
