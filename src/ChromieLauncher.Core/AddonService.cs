using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ChromieLauncher.Core;

public sealed class AddonService(Storage storage, HttpClient http, Catalog catalog, Action? checkGameClosed = null)
{
    private static readonly HashSet<string> ForbiddenExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".dll", ".bat", ".cmd", ".ps1", ".msi", ".com", ".scr", ".lnk", ".vbs" };

    public static string ValidateGame(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new IOException("Sélectionne le dossier du jeu dans les paramètres.");
        path = Path.GetFullPath(path).TrimEnd('\\', '/');
        SafePath.RejectLinks(path);
        if (!File.Exists(Path.Combine(path, "Wow.exe"))) throw new IOException("Le dossier doit contenir Wow.exe.");
        _ = SafePath.Child(path, "Interface/AddOns");
        return path;
    }

    public static void EnsureGameClosed()
    {
        foreach (var process in Process.GetProcessesByName("Wow"))
        {
            using (process)
                if (!process.HasExited) throw new IOException("Ferme World of Warcraft avant de modifier les addons.");
        }
    }

    public ManagedState GetState(string gamePath)
    {
        var path = Path.Combine(storage.Profile(gamePath), "managed.json");
        return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), AppJsonContext.Default.ManagedState)
            ?? throw new InvalidDataException("État local invalide.") : new();
    }

    public List<InstalledAddon> Scan(string gamePath)
    {
        gamePath = ValidateGame(gamePath);
        var addons = SafePath.Child(gamePath, "Interface/AddOns");
        if (!Directory.Exists(addons)) return [];
        var state = GetState(gamePath);
        var result = new List<InstalledAddon>();
        foreach (var directory in Directory.EnumerateDirectories(addons))
        {
            var name = Path.GetFileName(directory);
            if (name.StartsWith("Blizzard_", StringComparison.OrdinalIgnoreCase) || name == "APIDocumentation" || name.StartsWith('.')) continue;
            SafePath.RejectLinks(directory);
            var toc = Directory.EnumerateFiles(directory, "*.toc").FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals(name, StringComparison.OrdinalIgnoreCase));
            if (toc is null) continue;
            SafePath.RejectLinks(toc);
            var meta = TocReader.Parse(File.ReadAllText(toc));
            var managed = state.Packages.FirstOrDefault(p => p.Folders.Contains(name, StringComparer.OrdinalIgnoreCase));
            result.Add(new InstalledAddon
            {
                Folder = name, Name = TocReader.Clean(meta.GetValueOrDefault("Title", name)),
                Version = meta.GetValueOrDefault("Version", "Inconnue"), Interface = meta.GetValueOrDefault("Interface", "Inconnue"),
                Dependencies = meta.GetValueOrDefault("Dependencies", meta.GetValueOrDefault("RequiredDeps", "")),
                Managed = managed is not null, PackageId = managed?.Id ?? ""
            });
        }
        return result.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public async Task InstallAsync(string gamePath, AddonPackage package, IProgress<string>? progress = null, CancellationToken token = default)
    {
        gamePath = ValidateGame(gamePath);
        (checkGameClosed ?? EnsureGameClosed)();
        using var operationLock = Lock(gamePath);
        Recover(gamePath);
        var state = GetState(gamePath);
        var packages = CatalogService.Resolve(catalog, package.Id);
        // Never downgrade an already installed dependency implicitly. Manual dependencies must be adopted explicitly.
        packages = packages.Where(p => p.Id == package.Id || !p.Folders.All(f => Directory.Exists(SafePath.Child(gamePath, "Interface/AddOns/" + f)))).ToList();
        var folders = packages.SelectMany(p => p.Folders).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var folder in folders) SafePath.FolderName(folder);
        var files = catalog.Files.Where(f => folders.Contains(f.Path.Split('/')[0], StringComparer.OrdinalIgnoreCase)).ToList();
        if (files.Count == 0 || files.Sum(f => f.Size) > 512L * 1024 * 1024) throw new InvalidDataException("Taille du paquet invalide.");
        foreach (var file in files)
        {
            if (file.Size < 0 || file.Size > 32 * 1024 * 1024 || ForbiddenExtensions.Contains(Path.GetExtension(file.Path)))
                throw new InvalidDataException("Fichier non autorisé dans un addon : " + file.Path);
        }
        var work = NewTransaction(gamePath);
        var stage = Path.Combine(work, "stage");
        Directory.CreateDirectory(stage);
        try
        {
            var done = 0;
            await Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = 6, CancellationToken = token }, async (file, ct) =>
            {
                var destination = SafePath.Child(stage, file.Path);
                var bytes = await DownloadBlob(file, ct);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await File.WriteAllBytesAsync(destination, bytes, ct);
                progress?.Report($"Téléchargement de {package.Name} · {Interlocked.Increment(ref done)}/{files.Count} fichiers");
            });
            foreach (var folder in folders)
            {
                var toc = SafePath.Child(stage, folder + "/" + folder + ".toc");
                // Some historical TOCs differ only in case; Windows resolves those names.
                TocReader.ValidateCompatibility(await File.ReadAllTextAsync(toc, token));
                var metadata = TocReader.Parse(await File.ReadAllTextAsync(toc, token));
                foreach (var dependency in metadata.GetValueOrDefault("Dependencies", metadata.GetValueOrDefault("RequiredDeps", "")).Split([',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
                    if (!folders.Contains(dependency, StringComparer.OrdinalIgnoreCase) && !Directory.Exists(SafePath.Child(gamePath, "Interface/AddOns/" + dependency)))
                        throw new InvalidDataException("Dépendance requise absente : " + dependency);
            }
            token.ThrowIfCancellationRequested();
            (checkGameClosed ?? EnsureGameClosed)();
            progress?.Report("Sauvegarde et installation…");
            foreach (var p in packages)
            {
                state.Packages.RemoveAll(existing => existing.Id.Equals(p.Id, StringComparison.OrdinalIgnoreCase));
                state.Packages.Add(new() { Id = p.Id, Commit = catalog.Commit, Folders = p.Folders.ToList() });
            }
            Commit(gamePath, work, folders, state, install: true);
            progress?.Report(package.Name + " installé. Sauvegarde conservée.");
        }
        catch
        {
            if (File.Exists(Path.Combine(work, "journal.json"))) Recover(gamePath);
            else Directory.Delete(work, true);
            throw;
        }
    }

    public void Uninstall(string gamePath, string folder)
    {
        gamePath = ValidateGame(gamePath);
        SafePath.FolderName(folder);
        (checkGameClosed ?? EnsureGameClosed)();
        using var operationLock = Lock(gamePath);
        Recover(gamePath);
        var state = GetState(gamePath);
        var package = state.Packages.FirstOrDefault(p => p.Folders.Contains(folder, StringComparer.OrdinalIgnoreCase));
        var folders = package?.Folders.ToList() ?? [folder];
        var installed = Scan(gamePath);
        var dependents = installed.Where(a => !folders.Contains(a.Folder, StringComparer.OrdinalIgnoreCase) &&
            a.Dependencies.Split([',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries).Any(d => folders.Contains(d, StringComparer.OrdinalIgnoreCase))).Select(a => a.Name).ToList();
        if (dependents.Count > 0) throw new IOException("Désinstallation bloquée : dépendances utilisées par " + string.Join(", ", dependents));
        if (package is not null) state.Packages.Remove(package);
        var work = NewTransaction(gamePath);
        try { Commit(gamePath, work, folders, state, install: false); }
        catch { Recover(gamePath); throw; }
    }

    public static string BackupDirectory(string gamePath) => SafePath.Child(ValidateGame(gamePath), "Interface/.chromie-launcher/transactions");

    private FileStream Lock(string gamePath)
    {
        try { return new FileStream(Path.Combine(storage.Profile(gamePath), "operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new IOException("Une autre opération sur ce jeu est déjà en cours."); }
    }

    private static string NewTransaction(string gamePath)
    {
        var work = SafePath.Child(BackupDirectory(gamePath), DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        return work;
    }

    private void Commit(string gamePath, string work, List<string> folders, ManagedState state, bool install)
    {
        foreach (var folder in folders) SafePath.FolderName(folder);
        var statePath = Path.Combine(storage.Profile(gamePath), "managed.json");
        var journal = new MutationJournal
        {
            PreviousState = File.Exists(statePath) ? File.ReadAllText(statePath) : "{\"Packages\":[]}",
            Folders = folders.Select(f => new MutationFolder { Name = f, Existed = Directory.Exists(SafePath.Child(gamePath, "Interface/AddOns/" + f)) }).ToList()
        };
        Directory.CreateDirectory(SafePath.Child(gamePath, "Interface/AddOns"));
        Storage.AtomicWrite(Path.Combine(work, "journal.json"), JsonSerializer.Serialize(journal, AppJsonContext.Default.MutationJournal));
        foreach (var item in journal.Folders)
        {
            var target = SafePath.Child(gamePath, "Interface/AddOns/" + item.Name);
            var backup = SafePath.Child(work, "backup/" + item.Name);
            if (item.Existed)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                Directory.Move(target, backup);
            }
            if (install) Directory.Move(SafePath.Child(work, "stage/" + item.Name), target);
        }
        Storage.AtomicWrite(statePath, JsonSerializer.Serialize(state, AppJsonContext.Default.ManagedState));
        Storage.AtomicWrite(Path.Combine(work, "committed"), DateTime.UtcNow.ToString("O"));
    }

    private void Recover(string gamePath)
    {
        var transactions = BackupDirectory(gamePath);
        if (!Directory.Exists(transactions)) return;
        foreach (var work in Directory.EnumerateDirectories(transactions))
        {
            SafePath.RejectLinks(work);
            var journalPath = Path.Combine(work, "journal.json");
            if (!File.Exists(journalPath) || File.Exists(Path.Combine(work, "committed"))) continue;
            var journal = JsonSerializer.Deserialize(File.ReadAllText(journalPath), AppJsonContext.Default.MutationJournal) ?? throw new InvalidDataException("Journal invalide.");
            foreach (var item in journal.Folders)
            {
                SafePath.FolderName(item.Name);
                var target = SafePath.Child(gamePath, "Interface/AddOns/" + item.Name);
                var backup = SafePath.Child(work, "backup/" + item.Name);
                if (Directory.Exists(backup))
                {
                    if (Directory.Exists(target)) Directory.Delete(target, true);
                    Directory.Move(backup, target);
                }
                else if (!item.Existed && !Directory.Exists(SafePath.Child(work, "stage/" + item.Name)) && Directory.Exists(target)) Directory.Delete(target, true);
            }
            Storage.AtomicWrite(Path.Combine(storage.Profile(gamePath), "managed.json"), journal.PreviousState);
            File.Move(journalPath, Path.Combine(work, "rolled-back.json"));
        }
    }

    private async Task<byte[]> DownloadBlob(ArchiveFile file, CancellationToken token)
    {
        if (file.Sha.Length != 40 || !file.Sha.All(Uri.IsHexDigit)) throw new InvalidDataException("Empreinte Git invalide.");
        var cache = Path.Combine(storage.Root, "cache", "blobs", file.Sha);
        if (File.Exists(cache))
        {
            var cached = await File.ReadAllBytesAsync(cache, token);
            if (VerifyBlob(cached, file.Sha) && cached.LongLength == file.Size) return cached;
        }
        if (catalog.Commit.Length != 40 || !catalog.Commit.All(Uri.IsHexDigit)) throw new InvalidDataException("Révision de catalogue invalide.");
        var urlPath = string.Join('/', file.Path.Split('/').Select(Uri.EscapeDataString));
        using var response = await http.GetAsync($"https://raw.githubusercontent.com/wowgaming/addon-archive/{catalog.Commit}/{urlPath}", HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        using var input = await response.Content.ReadAsStreamAsync(token);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(buffer, token)) > 0)
        {
            if (output.Length + read > file.Size) throw new InvalidDataException("Fichier téléchargé trop volumineux.");
            output.Write(buffer, 0, read);
        }
        var bytes = output.ToArray();
        if (bytes.LongLength != file.Size || !VerifyBlob(bytes, file.Sha)) throw new InvalidDataException("Empreinte du téléchargement incorrecte : " + file.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
        var temp = cache + "." + Guid.NewGuid().ToString("N");
        await File.WriteAllBytesAsync(temp, bytes, token);
        File.Move(temp, cache, true);
        return bytes;
    }

    public static bool VerifyBlob(byte[] bytes, string sha)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        hash.AppendData(Encoding.ASCII.GetBytes("blob " + bytes.Length + "\0"));
        hash.AppendData(bytes);
        return Convert.ToHexString(hash.GetHashAndReset()).Equals(sha, StringComparison.OrdinalIgnoreCase);
    }
}
