using System.Reflection;
using System.Text;
using System.Text.Json;

namespace ChromieLauncher.Core;

public sealed record AddonUpdate(AddonPackage Package, List<ArchiveFile> Files, string InstalledVersion, bool Manual);
public sealed record AddonUpdateCheck(List<AddonUpdate> Updates, List<string> Errors, int CheckedPackages);

/// <summary>Read-only discovery against the catalog's known sources. Never executes downloaded code.</summary>
public sealed class AddonUpdateService(HttpClient http)
{
    private const string Archive = "wowgaming/addon-archive";
    private sealed record Source(string Repository, string Folder, string[] Include);
    private sealed record TreeFile(string Path, string Sha, long Size, string Mode);

    public async Task<AddonUpdateCheck> CheckAsync(Catalog catalog, ManagedState managed, IReadOnlyList<InstalledAddon> installed,
        CancellationToken token = default)
    {
        var updates = new List<AddonUpdate>();
        var errors = new List<string>();
        var checkedPackages = 0;
        var sources = LoadSources();
        var trees = new Dictionary<string, List<TreeFile>>();
        var ancestry = new Dictionary<string, bool>();
        var folders = installed.ToDictionary(a => a.Folder, StringComparer.OrdinalIgnoreCase);
        // An incomplete pack is not silently adopted or repaired by Update All.
        var packages = catalog.Packages.Where(p => p.Folders.Count > 0 && p.Folders.All(folders.ContainsKey)).ToList();
        foreach (var group in packages.GroupBy(p => p.Repository))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                CatalogService.ValidateRepository(group.Key);
                if (group.Key != Archive && !sources.Any(s => s.Repository == group.Key)) continue;
                using var latest = await GetJson($"https://api.github.com/repos/{group.Key}/commits?per_page=1", token);
                var revision = latest.RootElement[0].GetProperty("sha").GetString()!;
                ValidateHash(revision);
                var tree = await Tree(group.Key, revision, trees, token);
                foreach (var package in group)
                {
                    try
                    {
                        var state = managed.Packages.FirstOrDefault(p => p.Id.Equals(package.Id, StringComparison.OrdinalIgnoreCase));
                        if (state?.Commit == revision) { checkedPackages++; continue; }
                        var source = sources.SingleOrDefault(s => s.Repository == package.Repository && package.Folders.SequenceEqual(new[] { s.Folder }));
                        if (package.Repository != Archive && source is null) continue;
                        var files = SelectFiles(package, source, revision, tree);
                        if (state is not null)
                        {
                            ValidateHash(state.Commit);
                            var key = package.Repository + "/" + state.Commit + "/" + revision;
                            if (!ancestry.TryGetValue(key, out var ahead))
                            {
                                using var comparison = await GetJson($"https://api.github.com/repos/{package.Repository}/compare/{state.Commit}...{revision}", token);
                                ahead = comparison.RootElement.GetProperty("status").GetString() == "ahead";
                                ancestry[key] = ahead;
                            }
                            if (!ahead) { checkedPackages++; continue; }
                            var previous = SelectFiles(package, source, state.Commit, await Tree(package.Repository, state.Commit, trees, token));
                            if (SameFiles(previous, files)) { checkedPackages++; continue; }
                        }
                        else if (SameFiles(catalog.Files.Where(f => package.Folders.Contains(f.Path.Split('/')[0], StringComparer.OrdinalIgnoreCase)).ToList(), files)
                            && package.Folders.All(f => !IsNewerVersion(package.Version, folders[f].Version)))
                        { checkedPackages++; continue; }
                        var metadata = new List<Dictionary<string, string>>();
                        foreach (var folder in package.Folders)
                        {
                            var toc = files.SingleOrDefault(f => f.Path.Equals(folder + "/" + folder + ".toc", StringComparison.OrdinalIgnoreCase))
                                ?? throw new InvalidDataException("Missing compatible TOC: " + folder);
                            var bytes = await GetBytes(RawUrl(toc.Repository, revision, toc.SourcePath), 1024 * 1024, token);
                            if (bytes.LongLength != toc.Size || !AddonService.VerifyBlob(bytes, toc.Sha))
                                throw new InvalidDataException("TOC integrity check failed: " + folder);
                            var text = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
                            TocReader.ValidateCompatibility(text);
                            foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()))
                            {
                                if (line.Length == 0 || line.StartsWith('#')) continue;
                                var path = folder + "/" + line.Replace('\\', '/');
                                _ = SafePath.Child(Path.GetTempPath(), path);
                                if (!files.Any(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
                                    throw new InvalidDataException("TOC references a file outside the supported package layout: " + path);
                            }
                            metadata.Add(TocReader.Parse(text));
                        }
                        var versions = metadata.Select(m => m.GetValueOrDefault("Version", "Unknown")).ToList();
                        var oldVersion = string.Join(", ", package.Folders.Select(f => folders[f].Version).Distinct());
                        var version = string.Join(", ", versions.Distinct());
                        if (state is null)
                        {
                            // A manual install has no trusted revision. Only strictly newer numeric TOC versions qualify.
                            if (!package.Folders.Select((f, i) => IsNewerVersion(versions[i], folders[f].Version)).All(v => v))
                            { checkedPackages++; continue; }
                        }
                        else
                        {
                            // A numeric version decrease is not a safe update, even on a descendant commit.
                            if (package.Folders.Select((f, i) => IsNewerVersion(folders[f].Version, versions[i])).Any(v => v))
                                throw new InvalidDataException("Remote TOC version is older than the installed version.");
                        }
                        var dependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var meta in metadata)
                        foreach (var dependency in meta.GetValueOrDefault("Dependencies", meta.GetValueOrDefault("RequiredDeps", "")).Split([',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
                        {
                            if (package.Folders.Contains(dependency, StringComparer.OrdinalIgnoreCase) || dependency.StartsWith("Blizzard_", StringComparison.OrdinalIgnoreCase)) continue;
                            var owner = catalog.Packages.FirstOrDefault(p => p.Folders.Contains(dependency, StringComparer.OrdinalIgnoreCase));
                            if (owner is not null) dependencies.Add(owner.Id);
                            else if (!folders.ContainsKey(dependency)) throw new InvalidDataException("Unknown required dependency: " + dependency);
                        }
                        var candidate = new AddonPackage
                        {
                            Id = package.Id, Name = package.Name, Category = package.Category, Description = package.Description,
                            Version = version, Repository = package.Repository, Commit = revision,
                            Folders = package.Folders.ToList(), Dependencies = dependencies.ToList()
                        };
                        updates.Add(new(candidate, files, oldVersion, state is null));
                        checkedPackages++;
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    { errors.Add(package.Name + ": " + error.Message); }
                }
            }
            catch (Exception error) when (error is not OperationCanceledException)
            { errors.Add(group.Key + ": " + error.Message); }
        }
        return new(updates, errors, checkedPackages);
    }

    public static bool IsNewerVersion(string candidate, string installed)
    {
        static Version? Parse(string value)
        {
            value = value.Trim().TrimStart('v', 'V');
            return Version.TryParse(value, out var parsed)
                ? new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build), Math.Max(0, parsed.Revision)) : null;
        }
        var next = Parse(candidate); var current = Parse(installed);
        return next is not null && current is not null && next > current;
    }

    public static void Apply(Catalog catalog, AddonUpdate update)
    {
        var index = catalog.Packages.FindIndex(p => p.Id == update.Package.Id);
        if (index < 0) throw new InvalidDataException("Update is not in the known catalog.");
        catalog.Packages[index] = update.Package;
        catalog.Files.RemoveAll(f => update.Package.Folders.Contains(f.Path.Split('/')[0], StringComparer.OrdinalIgnoreCase));
        catalog.Files.AddRange(update.Files);
    }

    private static List<Source> LoadSources()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ChromieLauncher.Core.Data.community-addons.json")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.EnumerateArray().Select(s => new Source(s.GetProperty("Repository").GetString()!,
            s.GetProperty("Folder").GetString()!, s.GetProperty("Include").EnumerateArray().Select(i => i.GetString()!).ToArray())).ToList();
    }

    private static List<ArchiveFile> SelectFiles(AddonPackage package, Source? source, string revision, List<TreeFile> tree)
    {
        var result = new List<ArchiveFile>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in tree)
        {
            var included = source is null ? package.Folders.Contains(file.Path.Split('/')[0], StringComparer.OrdinalIgnoreCase)
                : source.Include.Any(i => i.EndsWith('/') ? file.Path.StartsWith(i, StringComparison.Ordinal) : file.Path == i)
                    || (!file.Path.Contains('/') && Path.GetExtension(file.Path).ToLowerInvariant() is ".lua" or ".xml" or ".toc");
            if (!included) continue;
            if (AddonService.ForbiddenExtensions.Contains(Path.GetExtension(file.Path))) continue;
            if (file.Mode != "100644" || file.Size < 0 || file.Size > 32 * 1024 * 1024)
                throw new InvalidDataException("Unsupported source file: " + file.Path);
            var destination = source is null ? file.Path : source.Folder + "/" + file.Path;
            _ = SafePath.Child(Path.GetTempPath(), destination);
            ValidateHash(file.Sha);
            if (!paths.Add(destination)) throw new InvalidDataException("Duplicate source path: " + destination);
            result.Add(new() { Path = destination, SourcePath = file.Path, Repository = package.Repository, Commit = revision, Sha = file.Sha, Size = file.Size });
        }
        if (result.Count == 0 || result.Sum(f => f.Size) > 512L * 1024 * 1024) throw new InvalidDataException("Invalid update size.");
        return result;
    }

    private static bool SameFiles(List<ArchiveFile> first, List<ArchiveFile> second) =>
        first.Count == second.Count && first.OrderBy(f => f.Path, StringComparer.Ordinal).Select(f => (f.Path, f.Sha, f.Size))
            .SequenceEqual(second.OrderBy(f => f.Path, StringComparer.Ordinal).Select(f => (f.Path, f.Sha, f.Size)));

    private async Task<List<TreeFile>> Tree(string repository, string revision, Dictionary<string, List<TreeFile>> cache, CancellationToken token)
    {
        var key = repository + "/" + revision;
        if (cache.TryGetValue(key, out var cached)) return cached;
        using var tree = await GetJson($"https://api.github.com/repos/{repository}/git/trees/{revision}?recursive=1", token);
        if (tree.RootElement.GetProperty("truncated").GetBoolean()) throw new InvalidDataException("GitHub returned a truncated source tree.");
        var files = tree.RootElement.GetProperty("tree").EnumerateArray().Where(f => f.GetProperty("type").GetString() != "tree")
            .Select(f => new TreeFile(f.GetProperty("path").GetString()!, f.GetProperty("sha").GetString()!,
                f.TryGetProperty("size", out var size) ? size.GetInt64() : -1, f.GetProperty("mode").GetString()!)).ToList();
        cache[key] = files;
        return files;
    }

    private async Task<JsonDocument> GetJson(string url, CancellationToken token) => JsonDocument.Parse(await GetBytes(url, 32 * 1024 * 1024, token));

    private async Task<byte[]> GetBytes(string url, int limit, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("ChromieLauncher-AddonUpdates/1.0");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        using var input = await response.Content.ReadAsStreamAsync(token);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(buffer, token)) > 0)
        {
            if (output.Length + read > limit) throw new InvalidDataException("Source response is too large.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static void ValidateHash(string value)
    {
        if (value.Length != 40 || !value.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid Git revision or blob hash.");
    }

    private static string RawUrl(string repository, string revision, string path) =>
        $"https://raw.githubusercontent.com/{repository}/{revision}/" + string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
}
