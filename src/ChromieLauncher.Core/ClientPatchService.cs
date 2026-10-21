using System.Security.Cryptography;
using System.Text.Json;

namespace ChromieLauncher.Core;

public sealed class PatchJournal
{
    public bool HadOriginal { get; set; }
    public string VariantId { get; set; } = "";
}

public sealed class ClientPatchService(Storage storage, IMegaDownloader downloader, Action? checkGameClosed = null)
{
    public static string DataDirectory(string gamePath)
    {
        var game = AddonService.ValidateGame(gamePath);
        var data = SafePath.Child(game, "Data");
        if (!Directory.Exists(data)) throw new IOException("The selected game's Data folder is missing.");
        return data;
    }

    public static string BackupDirectory(string gamePath) => SafePath.Child(AddonService.ValidateGame(gamePath), "Interface/.chromie-launcher/patch-transactions");

    public async Task<PatchStatus> InspectAsync(string gamePath, CancellationToken token = default)
    {
        var target = SafePath.Child(DataDirectory(gamePath), ClientPatchCatalog.FileName);
        if (!File.Exists(target)) return new(false, null);
        var size = new FileInfo(target).Length;
        var candidates = ClientPatchCatalog.All.Where(p => p.Size == size).ToList();
        if (candidates.Count == 0) return new(true, null);
        await using var stream = File.OpenRead(target);
        var sha = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
        return new(true, candidates.FirstOrDefault(p => sha.Equals(p.Sha256, StringComparison.OrdinalIgnoreCase))?.Id);
    }

    public async Task InstallAsync(string gamePath, ClientPatch patch, IProgress<string>? progress = null, CancellationToken token = default)
    {
        gamePath = AddonService.ValidateGame(gamePath);
        var data = DataDirectory(gamePath);
        if (patch.Sha256.Length != 64 || !patch.Sha256.All(Uri.IsHexDigit) || patch.Size < 32 || patch.Size > MegaClient.MaximumFileSize)
            throw new InvalidDataException("The patch must have a pinned SHA-256 and a valid size.");
        (checkGameClosed ?? AddonService.EnsureGameClosed)();
        using var operationLock = Lock(gamePath);
        Recover(gamePath);
        var work = SafePath.Child(BackupDirectory(gamePath), DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var staged = SafePath.Child(work, "incoming.mpq");
        try
        {
            MegaDownload result;
            await using (var output = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                result = await downloader.DownloadAsync(patch.ShareUrl, patch.Size, output,
                    new InlineProgress<TransferProgress>(p => progress?.Report(LauncherText.Format("Downloading {0} · {1:F2} / {2:F2} MiB", LauncherText.Get(patch.Name), p.Completed / 1048576d, p.Total / 1048576d))), token);
                await output.FlushAsync(token);
                output.Flush(true);
            }
            progress?.Report(LauncherText.Get("Verifying MEGA authentication, SHA-256 and MPQ format…"));
            if (result.Name != patch.SourceName || result.Size != patch.Size || new FileInfo(staged).Length != patch.Size)
                throw new InvalidDataException("The downloaded patch no longer matches the selected variant.");
            await using (var input = File.OpenRead(staged))
            {
                var header = new byte[4];
                await input.ReadExactlyAsync(header, token);
                if (!header.AsSpan().SequenceEqual("MPQ\u001a"u8)) throw new InvalidDataException("This download is not a supported MPQ patch.");
                input.Position = 0;
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, token));
                if (!hash.Equals(patch.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new CryptographicException("Patch SHA-256 verification failed. Your existing patch has not been changed.");
            }
            token.ThrowIfCancellationRequested();
            (checkGameClosed ?? AddonService.EnsureGameClosed)();
            progress?.Report(LauncherText.Get("Backing up and installing Data\\patch-y.mpq…"));
            var target = SafePath.Child(data, ClientPatchCatalog.FileName);
            var journal = new PatchJournal { HadOriginal = File.Exists(target), VariantId = patch.Id };
            Storage.AtomicWrite(Path.Combine(work, "journal.json"), JsonSerializer.Serialize(journal, AppJsonContext.Default.PatchJournal));
            if (journal.HadOriginal) File.Move(target, SafePath.Child(work, "backup.mpq"));
            File.Move(staged, target);
            Storage.AtomicWrite(Path.Combine(work, "committed"), DateTime.UtcNow.ToString("O"));
            progress?.Report(LauncherText.Format(journal.HadOriginal ? "{0} installed. Previous patch preserved in backups." : "{0} installed in Data\\patch-y.mpq.", LauncherText.Get(patch.Name)));
        }
        catch
        {
            if (File.Exists(Path.Combine(work, "journal.json"))) Recover(gamePath);
            else Directory.Delete(work, true);
            throw;
        }
    }

    private FileStream Lock(string gamePath)
    {
        try { return new FileStream(Path.Combine(storage.Profile(gamePath), "operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new IOException("Another operation on this game is already in progress."); }
    }

    private static void Recover(string gamePath)
    {
        var root = BackupDirectory(gamePath);
        if (!Directory.Exists(root)) return;
        var target = SafePath.Child(DataDirectory(gamePath), ClientPatchCatalog.FileName);
        foreach (var work in Directory.EnumerateDirectories(root))
        {
            SafePath.RejectLinks(work);
            var path = SafePath.Child(work, "journal.json");
            if (!File.Exists(path) || File.Exists(SafePath.Child(work, "committed")) || File.Exists(SafePath.Child(work, "rolled-back"))) continue;
            var journal = JsonSerializer.Deserialize(File.ReadAllText(path), AppJsonContext.Default.PatchJournal) ?? throw new InvalidDataException("Invalid patch transaction journal.");
            var backup = SafePath.Child(work, "backup.mpq");
            var restore = File.Exists(backup);
            if (restore || (!journal.HadOriginal && !File.Exists(SafePath.Child(work, "incoming.mpq"))))
            {
                // Never discard a file that may have been changed manually after an interruption.
                if (File.Exists(target)) File.Move(target, SafePath.Child(work, "displaced-" + Guid.NewGuid().ToString("N") + ".mpq"));
                if (restore) File.Move(backup, target);
            }
            Storage.AtomicWrite(Path.Combine(work, "rolled-back"), DateTime.UtcNow.ToString("O"));
        }
    }

    private sealed class InlineProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}
