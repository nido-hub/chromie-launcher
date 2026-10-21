using System.Security.Cryptography;
using System.Text.Json;
using ChromieLauncher.Core;

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

Console.WriteLine("keygen <private.pem> <public.pem>\nsign <release.zip> <https-download-url> <version> <private.pem> <update.json> [notes]");
return 1;
