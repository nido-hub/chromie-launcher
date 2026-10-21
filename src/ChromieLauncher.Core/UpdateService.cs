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
    public const string DefaultFeedUrl = "https://github.com/nido-hub/chromie-launcher/releases/latest/download/update.json";
    public static bool IsConfigured => !string.IsNullOrWhiteSpace(ReadPublicKey());
    public static string ResolveFeedUrl(string? configuredUrl) => string.IsNullOrWhiteSpace(configuredUrl) ? DefaultFeedUrl : configuredUrl.Trim();
    private const long MaximumPackageSize = 350L * 1024 * 1024;

    public async Task<(SignedUpdate Envelope, UpdateRelease Release)?> CheckAsync(string feedUrl, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(feedUrl)) return null;
        RequireHttps(feedUrl);
        var key = PublicKey();
        using var response = await http.GetAsync(feedUrl, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        var bytes = await ReadLimited(response.Content, 128 * 1024, token);
        var envelope = JsonSerializer.Deserialize(bytes, AppJsonContext.Default.SignedUpdate) ?? throw new InvalidDataException("Invalid update feed.");
        var release = VerifySigned(envelope, key);
        return Version.Parse(release.Version) > CurrentVersion ? (envelope, release) : null;
    }

    public async Task<string> PrepareAsync(SignedUpdate envelope, IProgress<string>? progress = null, CancellationToken token = default)
    {
        var release = VerifySigned(envelope, PublicKey());
        if (Version.Parse(release.Version) <= CurrentVersion) throw new InvalidDataException("An older version cannot be installed.");
        var directory = Path.Combine(storage.Root, "updates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        progress?.Report(LauncherText.Format("Downloading update {0}…", release.Version));
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
                if (size > MaximumPackageSize) throw new InvalidDataException("Update package is too large.");
                await output.WriteAsync(buffer.AsMemory(0, read), token);
            }
        }
        VerifyZipHash(zipPath, release.Sha256);
        // Validate archive layout before asking the user to close the working application.
        ExtractPackage(zipPath, Path.Combine(directory, "validated"));
        Storage.AtomicWrite(Path.Combine(directory, "release.json"), JsonSerializer.Serialize(envelope, AppJsonContext.Default.SignedUpdate));
        progress?.Report(LauncherText.Get("Update verified and ready to install."));
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
        using var helperProcess = Process.Start(start) ?? throw new IOException("Could not start the update replacement phase.");
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
                ?? throw new InvalidDataException("Invalid update job.");
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
                    throw new IOException("The launcher did not close within the expected time.");
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
        if (envelope.Payload.Length > 100000 || envelope.Signature.Length > 2048) throw new InvalidDataException("Update manifest is too large.");
        var payload = Convert.FromBase64String(envelope.Payload);
        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKey);
        if (!rsa.VerifyData(payload, Convert.FromBase64String(envelope.Signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            throw new CryptographicException("Invalid update signature. Installation refused.");
        var release = JsonSerializer.Deserialize(payload, AppJsonContext.Default.UpdateRelease) ?? throw new InvalidDataException("Invalid update manifest.");
        if (!Version.TryParse(release.Version, out _) || release.Sha256.Length != 64 || !release.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Invalid update version or checksum.");
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
                throw new InvalidDataException("Update archive is not allowed.");
            var path = SafePath.Child(destination, entry.FullName);
            if (!seen.Add(path)) throw new InvalidDataException("Duplicate path in update archive.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            entry.ExtractToFile(path, false);
        }
        if (!File.Exists(Path.Combine(destination, "ChromieLauncher.exe")))
            throw new InvalidDataException("The archive must contain ChromieLauncher.exe at its root.");
    }

    private static UpdateRelease ReadRelease(string directory)
    {
        var envelope = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(directory, "release.json")), AppJsonContext.Default.SignedUpdate)
            ?? throw new InvalidDataException("Update manifest is missing.");
        var release = VerifySigned(envelope, PublicKey());
        if (Version.Parse(release.Version) <= CurrentVersion) throw new InvalidDataException("Downgrading to an older version is not allowed.");
        return release;
    }

    private static string PublicKey()
    {
        var key = ReadPublicKey();
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Launcher updates are not available yet: the publisher's signing key must be embedded at build time.");
        return key;
    }

    private static string ReadPublicKey()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ChromieLauncher.Core.Data.update-public-key.pem")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void ValidateTarget(string target)
    {
        SafePath.RejectLinks(target);
        if (!File.Exists(Path.Combine(target, "ChromieLauncher.exe")) || File.Exists(Path.Combine(target, "Wow.exe")) || Directory.Exists(Path.Combine(target, "Interface")))
            throw new IOException("The launcher must be placed in its own folder, separate from the game root.");
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
            throw new CryptographicException("The update archive is corrupted. Installation refused.");
    }

    private static void RequireHttps(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidDataException("An HTTPS URL is required.");
    }

    private static async Task<byte[]> ReadLimited(HttpContent content, int limit, CancellationToken token)
    {
        using var input = await content.ReadAsStreamAsync(token);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await input.ReadAsync(buffer, token)) > 0)
        {
            if (output.Length + read > limit) throw new InvalidDataException("Server response is too large.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
}
