using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace ChromieLauncher.Core;

/// <summary>The same application binary handles the short, hidden replacement phase.
/// Release metadata is signed; a feed URL alone never grants permission to execute code.</summary>
public sealed class UpdateService(Storage storage, HttpClient http)
{
    public static Version CurrentVersion => typeof(UpdateService).Assembly.GetName().Version ?? new(0, 1, 0);
    public static string DisplayVersion => $"{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";
    private const long MaximumPackageSize = 350L * 1024 * 1024;

    public async Task<(SignedUpdate Envelope, UpdateRelease Release)?> CheckAsync(string feedUrl, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(feedUrl)) return null;
        RequireHttps(feedUrl);
        var key = PublicKey();
        using var response = await http.GetAsync(feedUrl, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        var bytes = await ReadLimited(response.Content, 128 * 1024, token);
        var envelope = JsonSerializer.Deserialize(bytes, AppJsonContext.Default.SignedUpdate) ?? throw new InvalidDataException("Flux de mise à jour invalide.");
        var release = VerifySigned(envelope, key);
        return Version.Parse(release.Version) > CurrentVersion ? (envelope, release) : null;
    }

    public async Task<string> PrepareAsync(SignedUpdate envelope, IProgress<string>? progress = null, CancellationToken token = default)
    {
        var release = VerifySigned(envelope, PublicKey());
        if (Version.Parse(release.Version) <= CurrentVersion) throw new InvalidDataException("Une ancienne version ne peut pas être installée.");
        var directory = Path.Combine(storage.Root, "updates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        progress?.Report("Téléchargement de la mise à jour " + release.Version + "…");
        using var response = await http.GetAsync(release.Url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        var zipPath = Path.Combine(directory, "package.zip");
        using (var input = await response.Content.ReadAsStreamAsync(token))
        await using (var output = File.Create(zipPath))
        {
            var buffer = new byte[81920];
            long size = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, token)) > 0)
            {
                size += read;
                if (size > MaximumPackageSize) throw new InvalidDataException("Mise à jour trop volumineuse.");
                await output.WriteAsync(buffer.AsMemory(0, read), token);
            }
        }
        VerifyZipHash(zipPath, release.Sha256);
        // Validate archive layout before asking the user to close the working application.
        ExtractPackage(zipPath, Path.Combine(directory, "validated"));
        Storage.AtomicWrite(Path.Combine(directory, "release.json"), JsonSerializer.Serialize(envelope, AppJsonContext.Default.SignedUpdate));
        progress?.Report("Mise à jour vérifiée et prête à installer.");
        return directory;
    }

    public void LaunchReplacement(string directory)
    {
        var target = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\', '/');
        ValidateTarget(target);
        var release = ReadRelease(directory);
        VerifyZipHash(Path.Combine(directory, "package.zip"), release.Sha256);
        // Copy the current executable and its native dependencies to run outside the install directory.
        var helper = Path.Combine(directory, "helper");
        CopyDirectory(target, helper);
        using var current = Process.GetCurrentProcess();
        var job = new UpdateJob { TargetDirectory = target, ProcessId = Environment.ProcessId, ProcessStartTicks = current.StartTime.ToUniversalTime().Ticks };
        Storage.AtomicWrite(Path.Combine(directory, "job.json"), JsonSerializer.Serialize(job, AppJsonContext.Default.UpdateJob));
        var start = new ProcessStartInfo(Path.Combine(helper, "ChromieLauncher.exe")) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = helper };
        start.ArgumentList.Add("--apply-update");
        start.ArgumentList.Add(directory);
        using var helperProcess = Process.Start(start) ?? throw new IOException("Impossible de lancer la phase de remplacement.");
    }

    public static int Apply(string directory)
    {
        var copied = new List<string>();
        var oldFiles = new List<string>();
        string? target = null;
        var backup = Path.Combine(directory, "backup");
        try
        {
            SafePath.RejectLinks(directory);
            var job = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(directory, "job.json")), AppJsonContext.Default.UpdateJob)
                ?? throw new InvalidDataException("Tâche de mise à jour invalide.");
            target = Path.GetFullPath(job.TargetDirectory).TrimEnd('\\', '/');
            ValidateTarget(target);
            var release = ReadRelease(directory);
            var zip = Path.Combine(directory, "package.zip");
            VerifyZipHash(zip, release.Sha256);
            var stage = Path.Combine(directory, "stage");
            ExtractPackage(zip, stage);
            try
            {
                using var parent = Process.GetProcessById(job.ProcessId);
                if (parent.StartTime.ToUniversalTime().Ticks == job.ProcessStartTicks && !parent.WaitForExit(60000))
                    throw new IOException("Le launcher ne s’est pas fermé dans le délai prévu.");
            }
            catch (ArgumentException) { /* Parent already exited. */ }

            foreach (var source in Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(stage, source);
                var destination = SafePath.Child(target, relative);
                if (File.Exists(destination))
                {
                    var saved = SafePath.Child(backup, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                    File.Copy(destination, saved, false);
                    oldFiles.Add(relative);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                copied.Add(relative);
                File.Copy(source, destination, true);
            }
            Storage.AtomicWrite(Path.Combine(directory, "applied"), release.Version);
            var restart = new ProcessStartInfo(Path.Combine(target, "ChromieLauncher.exe")) { UseShellExecute = true, WorkingDirectory = target };
            restart.ArgumentList.Add("--updated");
            Process.Start(restart)?.Dispose();
            return 0;
        }
        catch (Exception error)
        {
            try
            {
                if (target is not null)
                {
                    foreach (var relative in copied.AsEnumerable().Reverse())
                    {
                        var destination = SafePath.Child(target, relative);
                        if (oldFiles.Contains(relative)) File.Copy(SafePath.Child(backup, relative), destination, true);
                        else if (File.Exists(destination)) File.Delete(destination);
                    }
                    if (copied.Count > 0)
                        Process.Start(new ProcessStartInfo(Path.Combine(target, "ChromieLauncher.exe")) { UseShellExecute = true, WorkingDirectory = target })?.Dispose();
                }
            }
            catch (Exception rollbackError) { error = new AggregateException(error, rollbackError); }
            File.WriteAllText(Path.Combine(directory, "update-error.log"), error.ToString());
            return 1;
        }
    }

    public static UpdateRelease VerifySigned(SignedUpdate envelope, string publicKey)
    {
        if (envelope.Payload.Length > 100000 || envelope.Signature.Length > 2048) throw new InvalidDataException("Manifeste trop volumineux.");
        var payload = Convert.FromBase64String(envelope.Payload);
        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKey);
        if (!rsa.VerifyData(payload, Convert.FromBase64String(envelope.Signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            throw new CryptographicException("Signature de mise à jour invalide. Installation refusée.");
        var release = JsonSerializer.Deserialize(payload, AppJsonContext.Default.UpdateRelease) ?? throw new InvalidDataException("Manifeste invalide.");
        if (!Version.TryParse(release.Version, out _) || release.Sha256.Length != 64 || !release.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Version ou empreinte invalide.");
        RequireHttps(release.Url);
        return release;
    }

    public static void ExtractPackage(string zipPath, string destination)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        long total = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith('/')) continue;
            total += entry.Length;
            if (total > 1024L * 1024 * 1024 || zip.Entries.Count > 5000 || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException("Archive de mise à jour non autorisée.");
            var path = SafePath.Child(destination, entry.FullName);
            if (!seen.Add(path)) throw new InvalidDataException("Chemin dupliqué dans l’archive.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            entry.ExtractToFile(path, false);
        }
        if (!File.Exists(Path.Combine(destination, "ChromieLauncher.exe")))
            throw new InvalidDataException("L’archive ne contient pas ChromieLauncher.exe à sa racine.");
    }

    private static UpdateRelease ReadRelease(string directory)
    {
        var envelope = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(directory, "release.json")), AppJsonContext.Default.SignedUpdate)
            ?? throw new InvalidDataException("Manifeste absent.");
        var release = VerifySigned(envelope, PublicKey());
        if (Version.Parse(release.Version) <= CurrentVersion) throw new InvalidDataException("Retour à une ancienne version refusé.");
        return release;
    }

    private static string PublicKey()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ChromieLauncher.Core.Data.update-public-key.pem")!;
        using var reader = new StreamReader(stream);
        var key = reader.ReadToEnd();
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Les mises à jour du launcher ne sont pas encore publiées : la clé de signature de l’éditeur doit être intégrée à la compilation.");
        return key;
    }

    private static void ValidateTarget(string target)
    {
        SafePath.RejectLinks(target);
        if (!File.Exists(Path.Combine(target, "ChromieLauncher.exe")) || File.Exists(Path.Combine(target, "Wow.exe")) || Directory.Exists(Path.Combine(target, "Interface")))
            throw new IOException("Le launcher doit être placé dans son propre dossier, distinct de la racine du jeu.");
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            SafePath.RejectLinks(file);
            var destination = SafePath.Child(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, false);
        }
    }

    private static void VerifyZipHash(string path, string expected)
    {
        using var stream = File.OpenRead(path);
        if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new CryptographicException("L’archive de mise à jour est corrompue. Installation refusée.");
    }

    private static void RequireHttps(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidDataException("Une adresse HTTPS est obligatoire.");
    }

    private static async Task<byte[]> ReadLimited(HttpContent content, int limit, CancellationToken token)
    {
        using var input = await content.ReadAsStreamAsync(token);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await input.ReadAsync(buffer, token)) > 0)
        {
            if (output.Length + read > limit) throw new InvalidDataException("Réponse trop volumineuse.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
}
