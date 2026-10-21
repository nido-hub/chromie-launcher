using System.Security.Cryptography;
using System.Text.Json;
using ChromieLauncher.Core;

if (args.Length == 1 && args[0] == "verify-patches")
{
    // Downloads only to an isolated development cache. Never installs in a game.
    var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "opencode", "ChromieLauncher.PatchVerification", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
    var mega = new MegaClient(http);
    foreach (var patch in ClientPatchCatalog.All)
    {
        var path = Path.Combine(directory, patch.Id + ".mpq");
        await using var output = File.Create(path);
        var result = await mega.DownloadAsync(patch.ShareUrl, patch.Size, output);
        if (result.Name != patch.SourceName || (patch.Sha256.Length > 0 && result.Sha256 != patch.Sha256))
            throw new InvalidDataException("The patch no longer matches its catalog entry.");
        Console.WriteLine($"{patch.Id}: {result.Name}, {result.Size} bytes, authenticated SHA-256 {result.Sha256}");
    }
    Console.WriteLine("Verified downloads: " + directory + ". No game files modified.");
    return 0;
}

if (args.Length == 3 && args[0] == "keygen")
{
    // Never overwrite an existing publisher key.
    if (File.Exists(args[1]) || File.Exists(args[2])) throw new IOException("Une des clés existe déjà.");
    using var rsa = RSA.Create(3072);
    File.WriteAllText(args[1], rsa.ExportPkcs8PrivateKeyPem());
    File.WriteAllText(args[2], rsa.ExportSubjectPublicKeyInfoPem());
    Console.WriteLine("Clés créées. Garde la clé privée hors du dépôt ; seule la clé publique doit être distribuée.");
    return 0;
}

if (args.Length == 2 && args[0] == "verify-key")
{
    using var resource = typeof(UpdateService).Assembly.GetManifestResourceStream("ChromieLauncher.Core.Data.update-public-key.pem")!;
    using var reader = new StreamReader(resource);
    using var publisher = RSA.Create(); publisher.ImportFromPem(reader.ReadToEnd());
    using var signer = RSA.Create(); signer.ImportFromPem(File.ReadAllText(args[1]));
    var challenge = RandomNumberGenerator.GetBytes(32);
    var signature = signer.SignData(challenge, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    if (!publisher.VerifyData(challenge, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
        throw new CryptographicException("The signing key does not match the embedded publisher public key.");
    Console.WriteLine("Signing key matches the embedded publisher public key.");
    return 0;
}

if (args.Length == 2 && args[0] == "verify")
{
    using var resource = typeof(UpdateService).Assembly.GetManifestResourceStream("ChromieLauncher.Core.Data.update-public-key.pem")!;
    using var reader = new StreamReader(resource);
    var envelope = JsonSerializer.Deserialize(File.ReadAllText(args[1]), AppJsonContext.Default.SignedUpdate)
        ?? throw new InvalidDataException("Invalid update manifest.");
    var release = UpdateService.VerifySigned(envelope, reader.ReadToEnd());
    Console.WriteLine("Verified update manifest: " + release.Version);
    return 0;
}

if (args.Length >= 6 && args[0] == "sign")
{
    var zip = args[1]; var url = args[2]; var version = args[3]; var key = args[4]; var output = args[5];
    if (!Version.TryParse(version, out _) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https")
        throw new ArgumentException("Version numérique et URL HTTPS requises.");
    using var stream = File.OpenRead(zip);
    var release = new UpdateRelease
    {
        Version = version, Url = url, Sha256 = Convert.ToHexString(SHA256.HashData(stream)),
        Notes = args.Length > 6 ? args[6] : "Nouvelle version de Chromie Launcher."
    };
    var payload = JsonSerializer.SerializeToUtf8Bytes(release, AppJsonContext.Default.UpdateRelease);
    using var rsa = RSA.Create(); rsa.ImportFromPem(File.ReadAllText(key));
    var signed = new SignedUpdate
    {
        Payload = Convert.ToBase64String(payload),
        Signature = Convert.ToBase64String(rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
    };
    Storage.AtomicWrite(Path.GetFullPath(output), JsonSerializer.Serialize(signed, AppJsonContext.Default.SignedUpdate));
    Console.WriteLine("Manifeste signé : " + output);
    return 0;
}

Console.WriteLine("keygen <private.pem> <public.pem>\nverify-key <private.pem>\nsign <release.zip> <https-download-url> <version> <private.pem> <update.json> [notes]\nverify <update.json>\nverify-patches (downloads and authenticates both variants in an isolated development cache; never installs in a game)");
return 1;
