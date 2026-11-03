using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.RegularExpressions;
using Nimbus;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

// Creates the update signing key and signs release builds.
// The private key is never printed and never stored in a git repository.

var defaultKey = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nimbus", "update-signing.key");

try
{
    return args.ElementAtOrDefault(0) switch
    {
        "keygen" => KeyGen(Option("--key") ?? defaultKey, Option("--source")),
        "sign" when args.Length >= 2 => Sign(args[1], Option("--key") ?? defaultKey, args.Contains("--no-key-check")),
        _ => Usage(),
    };
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

string? Option(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static int Usage()
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  NimbusSign keygen [--key <path>] [--source <UpdateKey.cs>]");
    Console.Error.WriteLine("  NimbusSign sign <Nimbus.exe> [--key <path>] [--no-key-check]");
    return 2;
}

static int KeyGen(string keyPath, string? sourcePath)
{
    keyPath = Path.GetFullPath(keyPath);
    if (IsInsideGitRepository(keyPath))
    {
        Console.Error.WriteLine($"Error: {keyPath} is inside a git repository. Choose a location outside it.");
        return 1;
    }

    if (File.Exists(keyPath))
    {
        Console.Error.WriteLine($"Error: {keyPath} already exists. It wasn't changed.");
        return 1;
    }

    var privateKey = new Ed25519PrivateKeyParameters(new SecureRandom());
    var publicKey = Convert.ToBase64String(privateKey.GeneratePublicKey().GetEncoded());

    Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);
    using (var file = new FileStream(keyPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
    using (var writer = new StreamWriter(file))
        writer.Write(Convert.ToBase64String(privateKey.GetEncoded()));
    RestrictToCurrentUser(keyPath);

    if (sourcePath is not null)
    {
        var source = File.ReadAllText(sourcePath);
        File.WriteAllText(sourcePath, Regex.Replace(source, "PublicKey = \"[^\"]*\"", $"PublicKey = \"{publicKey}\""));
        Console.WriteLine($"Updated: {sourcePath}");
    }

    Console.WriteLine($"Private key: {keyPath}");
    Console.WriteLine($"Public key: {publicKey}");
    Console.WriteLine("Back up the private key somewhere safe. If you lose it, installed copies can't update themselves.");
    return 0;
}

static int Sign(string exePath, string keyPath, bool skipKeyCheck)
{
    if (!File.Exists(exePath))
    {
        Console.Error.WriteLine($"Error: {exePath} doesn't exist.");
        return 1;
    }

    var privateKey = new Ed25519PrivateKeyParameters(Convert.FromBase64String(File.ReadAllText(keyPath).Trim()));
    var publicKey = Convert.ToBase64String(privateKey.GeneratePublicKey().GetEncoded());
    if (!skipKeyCheck && publicKey != UpdateKey.PublicKey)
    {
        Console.Error.WriteLine("Error: this key doesn't match the public key built into Nimbus. Installed copies would reject the update.");
        return 1;
    }

    var hash = UpdateSignature.HashFile(exePath);
    var signer = new Ed25519Signer();
    signer.Init(true, privateKey);
    var message = UpdateSignature.MessageFor(hash);
    signer.BlockUpdate(message, 0, message.Length);
    var signature = Convert.ToBase64String(signer.GenerateSignature());

    var hashText = $"{hash}  {Path.GetFileName(exePath)}";
    if (UpdateSignature.Verify(exePath, hashText, signature, publicKey) != UpdateVerification.Valid)
    {
        Console.Error.WriteLine("Error: the new signature didn't verify. Nothing was written.");
        return 1;
    }

    File.WriteAllText(exePath + ".sha256", hashText + "\n");
    File.WriteAllText(exePath + ".sig", signature + "\n");
    Console.WriteLine($"Signed: {exePath}");
    Console.WriteLine($"SHA-256: {hash}");
    return 0;
}

static bool IsInsideGitRepository(string path)
{
    for (var folder = Path.GetDirectoryName(path); folder is not null; folder = Path.GetDirectoryName(folder))
    {
        if (Directory.Exists(Path.Combine(folder, ".git")))
            return true;
    }

    return false;
}

static void RestrictToCurrentUser(string path)
{
    var user = WindowsIdentity.GetCurrent().User!;
    var security = new FileSecurity();
    security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
    security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow));
    new FileInfo(path).SetAccessControl(security);
}
