using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ChromieLauncher.Core;
using Xunit;

namespace ChromieLauncher.Tests;

public sealed class CoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "opencode", "ChromieLauncher.Tests", Guid.NewGuid().ToString("N"));
    private string Game => Path.Combine(_root, "game");
    private readonly Storage _storage;
    private readonly Dictionary<string, byte[]> _blobs = new();
    private readonly Catalog _catalog = new() { Commit = new string('a', 40) };
    private readonly HttpClient _http;
    private readonly AddonService _service;

    public CoreTests()
    {
        Directory.CreateDirectory(Game);
        File.WriteAllText(Path.Combine(Game, "Wow.exe"), "test placeholder, never executed");
        _storage = new(Path.Combine(_root, "data"));
        _http = new(new FakeHandler(request =>
        {
            var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath.Split('/' + _catalog.Commit + '/')[^1]);
            return _blobs.TryGetValue(path, out var content) ? new(HttpStatusCode.OK) { Content = new ByteArrayContent(content) } : new(HttpStatusCode.NotFound);
        }));
        _service = new(_storage, _http, _catalog, () => { });
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("C:\\escape")]
    [InlineData("a/../../escape")]
    [InlineData("a:file")]
    [InlineData("/absolute")]
    [InlineData("a/./b")]
    [InlineData("a//b")]
    [InlineData("a./b")]
    [InlineData("a /b")]
    [InlineData("CON")]
    [InlineData("a/NUL.txt")]
    [InlineData("COM1.lua")]
    public void UnsafePathsAreRejected(string path) => Assert.Throws<InvalidDataException>(() => SafePath.Child(_root, path));

    [Theory]
    [InlineData("Blizzard_AuctionUI")]
    [InlineData("APIDocumentation")]
    [InlineData("a/b")]
    public void ClientFoldersAreProtected(string folder) => Assert.Throws<InvalidDataException>(() => SafePath.FolderName(folder));

    [Fact]
    public void TocParsesBomColorsAndDependencies()
    {
        var meta = TocReader.Parse("\uFEFF## Interface: 30300\r\n## Title: |cff00ff00Test|r\n## Version: 1.2\n## Dependencies: Lib");
        Assert.Equal("Test", TocReader.Clean(meta["Title"]));
        Assert.Equal("Lib", meta["Dependencies"]);
        TocReader.ValidateCompatibility("## Interface: 30300");
        Assert.Throws<InvalidDataException>(() => TocReader.ValidateCompatibility("## Interface: 30403"));
        Assert.Throws<InvalidDataException>(() => TocReader.ValidateCompatibility("## Interface: 110200"));
    }

    [Fact]
    public void BundledCatalogHasClosedDependenciesAndPinnedBlobs()
    {
        var catalog = CatalogService.Load();
        Assert.True(catalog.Packages.Count >= 100);
        Assert.Equal(40, catalog.Commit.Length);
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in catalog.Packages)
        {
            CatalogService.Resolve(catalog, package.Id);
            foreach (var folder in package.Folders)
            {
                Assert.True(folders.Add(folder), "Duplicate folder " + folder);
                SafePath.FolderName(folder);
                Assert.Contains(catalog.Files, f => f.Path.Equals(folder + "/" + folder + ".toc", StringComparison.OrdinalIgnoreCase));
            }
        }
        foreach (var file in catalog.Files)
        {
            Assert.Equal(40, file.Sha.Length); _ = SafePath.Child(_root, file.Path);
            Assert.DoesNotContain(Path.GetExtension(file.Path).ToLowerInvariant(), new[] { ".exe", ".bat", ".dll", ".ps1", ".cmd" });
        }
    }

    [Fact]
    public async Task InstallAndUninstallPreserveSettingsAndBackupFiles()
    {
        var package = Package("Example");
        var wtf = Path.Combine(Game, "WTF", "settings.lua"); Directory.CreateDirectory(Path.GetDirectoryName(wtf)!); File.WriteAllText(wtf, "keep me");
        await _service.InstallAsync(Game, package);
        var installed = Assert.Single(_service.Scan(Game));
        Assert.True(installed.Managed); Assert.Equal("1.0", installed.Version);
        Assert.Single(_service.GetState(Game).Packages);
        _service.Uninstall(Game, "Example");
        Assert.Empty(_service.Scan(Game)); Assert.Empty(_service.GetState(Game).Packages);
        Assert.Equal("keep me", File.ReadAllText(wtf));
        Assert.Single(Directory.GetFiles(AddonService.BackupDirectory(Game), "Example.toc", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task BulkUpdateReplacesSelectedDependenciesTogetherAndPreservesManualUnselectedDependency()
    {
        var lib = Package("Lib"); var addon = Package("Example", "Lib"); Package("Manual");
        addon.Dependencies.Add("Manual");
        WriteAddon("Lib", "old lib"); WriteAddon("Example", "old example"); WriteAddon("Manual", "manual newer");
        var wtf = Path.Combine(Game, "WTF", "settings.lua"); Directory.CreateDirectory(Path.GetDirectoryName(wtf)!); File.WriteAllText(wtf, "keep");
        await _service.InstallManyAsync(Game, [addon, lib]);
        Assert.Equal(2, _service.GetState(Game).Packages.Count);
        Assert.Equal("manual newer", File.ReadAllText(Toc("Manual"))); Assert.Equal("keep", File.ReadAllText(wtf));
        Assert.Single(Directory.GetDirectories(AddonService.BackupDirectory(Game)));
        var backups = Directory.GetFiles(AddonService.BackupDirectory(Game), "*.toc", SearchOption.AllDirectories);
        Assert.Equal(2, backups.Length);
        Assert.Contains(backups, p => File.ReadAllText(p) == "old lib"); Assert.Contains(backups, p => File.ReadAllText(p) == "old example");
    }

    [Fact]
    public async Task BulkUpdateCorruptDownloadLeavesAllSelectedAddonsUntouched()
    {
        var first = Package("First"); var second = Package("Second");
        WriteAddon("First", "original first"); WriteAddon("Second", "original second");
        _blobs["Second/Second.toc"] = Encoding.UTF8.GetBytes("corrupt");
        await Assert.ThrowsAsync<InvalidDataException>(() => _service.InstallManyAsync(Game, [first, second]));
        Assert.Equal("original first", File.ReadAllText(Toc("First"))); Assert.Equal("original second", File.ReadAllText(Toc("Second")));
        Assert.Empty(_service.GetState(Game).Packages);
    }

    [Fact]
    public async Task BulkUpdateCancellationLeavesAllSelectedAddonsUntouched()
    {
        var first = Package("First"); var second = Package("Second");
        WriteAddon("First", "original first"); WriteAddon("Second", "original second");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.InstallManyAsync(Game, [first, second], token: cancel.Token));
        Assert.Equal("original first", File.ReadAllText(Toc("First"))); Assert.Equal("original second", File.ReadAllText(Toc("Second")));
        Assert.Empty(_service.GetState(Game).Packages);
    }

    [Fact]
    public async Task BulkUpdateCommitFailureRollsBackAlreadyReplacedAddonAndManagedState()
    {
        var first = Package("First"); var second = Package("Second");
        await _service.InstallManyAsync(Game, [first, second]);
        WriteAddon("First", "original first"); WriteAddon("Second", "original second");
        var stateBefore = File.ReadAllText(Path.Combine(_storage.Profile(Game), "managed.json"));
        var checks = 0;
        var service = new AddonService(_storage, _http, _catalog, () =>
        {
            if (++checks != 2) return;
            var work = Directory.GetDirectories(AddonService.BackupDirectory(Game)).Single(p => !File.Exists(Path.Combine(p, "committed")));
            Directory.CreateDirectory(Path.Combine(work, "backup"));
            File.WriteAllText(Path.Combine(work, "backup", "Second"), "force second replacement to fail");
        });
        await Assert.ThrowsAsync<IOException>(() => service.InstallManyAsync(Game, [first, second]));
        Assert.Equal("original first", File.ReadAllText(Toc("First"))); Assert.Equal("original second", File.ReadAllText(Toc("Second")));
        Assert.Equal(stateBefore, File.ReadAllText(Path.Combine(_storage.Profile(Game), "managed.json")));
    }

    [Fact]
    public async Task ReplacementKeepsOriginalBackup()
    {
        var package = Package("Example");
        WriteAddon("Example", "old original");
        await _service.InstallAsync(Game, package);
        var saved = Assert.Single(Directory.GetFiles(AddonService.BackupDirectory(Game), "Example.toc", SearchOption.AllDirectories));
        Assert.Equal("old original", File.ReadAllText(saved));
    }

    [Fact]
    public async Task CorruptedDownloadNeverReplacesExistingAddon()
    {
        var package = Package("Example"); WriteAddon("Example", "original");
        _blobs["Example/Example.toc"] = Encoding.UTF8.GetBytes("tampered");
        await Assert.ThrowsAsync<InvalidDataException>(() => _service.InstallAsync(Game, package));
        Assert.Equal("original", File.ReadAllText(Toc("Example")));
        Assert.Empty(_service.GetState(Game).Packages);
    }

    [Fact]
    public async Task CancellationNeverReplacesExistingAddon()
    {
        var package = Package("Example"); WriteAddon("Example", "original");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.InstallAsync(Game, package, token: cancellation.Token));
        Assert.Equal("original", File.ReadAllText(Toc("Example")));
    }

    [Fact]
    public async Task DependencyInstallsAndCannotBeRemovedWhileRequired()
    {
        Package("Lib"); var addon = Package("Example", "Lib");
        await _service.InstallAsync(Game, addon);
        Assert.Equal(2, _service.Scan(Game).Count);
        Assert.Throws<IOException>(() => _service.Uninstall(Game, "Lib"));
    }

    [Fact]
    public async Task ManualDependencyIsNotImplicitlyDowngraded()
    {
        Package("Lib"); var addon = Package("Example", "Lib");
        WriteAddon("Lib", "## Interface: 30300\n## Version: much newer");
        await _service.InstallAsync(Game, addon);
        Assert.Contains("much newer", File.ReadAllText(Toc("Lib")));
        Assert.False(_service.Scan(Game).Single(a => a.Folder == "Lib").Managed);
    }

    [Fact]
    public async Task IncompatibleTocNeverInstalled()
    {
        var package = Package("Retail", toc: "## Interface: 110200");
        await Assert.ThrowsAsync<InvalidDataException>(() => _service.InstallAsync(Game, package));
        Assert.False(File.Exists(Toc("Retail")));
    }

    [Fact]
    public async Task ExecutableInAddonRejected()
    {
        var package = Package("Example"); AddFile("Example/payload.exe", "not allowed");
        await Assert.ThrowsAsync<InvalidDataException>(() => _service.InstallAsync(Game, package));
        Assert.False(Directory.Exists(Path.GetDirectoryName(Toc("Example"))));
    }

    [Fact]
    public async Task InterruptedMutationIsRolledBackBeforeNextOperation()
    {
        var package = Package("Example");
        var work = Path.Combine(AddonService.BackupDirectory(Game), "interrupted");
        var backup = Path.Combine(work, "backup", "Original"); Directory.CreateDirectory(backup);
        File.WriteAllText(Path.Combine(backup, "Original.toc"), "original before crash");
        WriteAddon("Original", "half installed");
        var journal = new MutationJournal { PreviousState = "{\"Packages\":[]}", Folders = [new() { Name = "Original", Existed = true }] };
        Storage.AtomicWrite(Path.Combine(work, "journal.json"), JsonSerializer.Serialize(journal, AppJsonContext.Default.MutationJournal));
        await _service.InstallAsync(Game, package);
        Assert.Equal("original before crash", File.ReadAllText(Toc("Original")));
        Assert.True(File.Exists(Path.Combine(work, "rolled-back.json")));
    }

    [Fact]
    public void ScanHidesBlizzardModulesAndKeepsUnknownManualAddons()
    {
        WriteAddon("Blizzard_AuctionUI", "## Title: System");
        WriteAddon("Custom", "## Title: My custom addon");
        var addon = Assert.Single(_service.Scan(Game));
        Assert.Equal("My custom addon", addon.Name); Assert.False(addon.Managed); Assert.Equal("Unknown", addon.Version);
    }

    [Fact]
    public void CommunityCatalogUsesCorrectFoldersSourcesAndDependencies()
    {
        var catalog = CatalogService.Load();
        var wishlist = Assert.Single(catalog.Packages, p => p.Id == "ChromieWishlist");
        Assert.Equal("nido-hub/chromie-wishlist", wishlist.Repository);
        Assert.Equal("1.2.0", wishlist.Version);
        Assert.Contains("AtlasLoot", wishlist.Dependencies);
        Assert.Contains(CatalogService.Resolve(catalog, wishlist.Id), p => p.Id == "AtlasLoot");
        var rolls = Assert.Single(catalog.Packages, p => p.Id == "LootSummary");
        Assert.Equal("nido-hub/loot-rolls", rolls.Repository);
        Assert.Equal("1.1.0", rolls.Version);
        Assert.Equal("LootSummary", Assert.Single(rolls.Folders));
        foreach (var package in new[] { wishlist, rolls })
        {
            Assert.Equal("ChromieCraft", package.Category);
            Assert.Equal(40, CatalogService.Revision(catalog, package).Length);
            var files = catalog.Files.Where(f => f.Repository == package.Repository).ToList();
            Assert.NotEmpty(files);
            Assert.All(files, f => Assert.Equal(package.Commit, f.Commit));
            Assert.DoesNotContain(files, f => f.SourcePath.StartsWith("Tests/") || f.SourcePath.StartsWith(".git"));
            Assert.Contains(files, f => f.SourcePath == package.Id + ".toc" && f.Path == package.Id + "/" + package.Id + ".toc");
        }
    }

    [Fact]
    public async Task CommunityDownloadsUsePinnedRepositoryAndMapRootToAddonFolder()
    {
        var package = Package("Example");
        package.Repository = "nido-hub/example"; package.Commit = new string('b', 40);
        var file = Assert.Single(_catalog.Files);
        file.Repository = package.Repository; file.Commit = package.Commit; file.SourcePath = "Example.toc";
        string? requested = null;
        using var http = new HttpClient(new FakeHandler(request =>
        {
            requested = request.RequestUri!.AbsoluteUri;
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(_blobs["Example/Example.toc"]) };
        }));
        var service = new AddonService(_storage, http, _catalog, () => { });
        await service.InstallAsync(Game, package);
        Assert.Equal($"https://raw.githubusercontent.com/nido-hub/example/{package.Commit}/Example.toc", requested);
        Assert.True(File.Exists(Toc("Example")));
        Assert.Equal(package.Commit, Assert.Single(service.GetState(Game).Packages).Commit);
        Assert.NotEqual(_catalog.Commit, Assert.Single(service.GetState(Game).Packages).Commit);
    }

    [Theory]
    [InlineData("../example")]
    [InlineData("https://example.com/repo")]
    [InlineData("owner/repo/extra")]
    [InlineData("owner/..")]
    [InlineData("owner/repo?ref=main")]
    public void InvalidRepositoriesAreRejected(string repository) => Assert.Throws<InvalidDataException>(() => CatalogService.ValidateRepository(repository));

    [Fact]
    public void DefaultLabelsAreEnglish()
    {
        Assert.Equal("Unknown", new AddonPackage().Version);
        Assert.Equal("Unknown", new InstalledAddon().Version);
        Assert.Equal("Manually installed", new InstalledAddon().Status);
        Assert.Equal("Managed by the launcher", new InstalledAddon { Managed = true }.Status);
        Assert.Equal("Select your game folder in Settings.", Assert.Throws<IOException>(() => AddonService.ValidateGame("")).Message);
        var categories = CatalogService.Load().Packages.Select(p => p.Category).Distinct().ToList();
        Assert.Contains("Equipment", categories); Assert.Contains("Bags & economy", categories);
        Assert.DoesNotContain("Équipement", categories); Assert.DoesNotContain("JcJ", categories);
    }

    [Fact]
    public void ApplicationIconIncludesTaskbarAndHighDpiResolutions()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "chromiecraft.ico"));
        using var reader = new BinaryReader(new MemoryStream(bytes));
        Assert.Equal(0, reader.ReadUInt16()); Assert.Equal(1, reader.ReadUInt16());
        var count = reader.ReadUInt16(); Assert.Equal(9, count);
        var sizes = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var width = reader.ReadByte(); var height = reader.ReadByte();
            Assert.Equal(width, height); sizes.Add(width == 0 ? 256 : width);
            reader.ReadBytes(6);
            var length = reader.ReadUInt32(); var offset = reader.ReadUInt32();
            Assert.True(length > 0 && offset + length <= bytes.Length);
        }
        Assert.Equal(new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 }, sizes.Order().ToArray());
    }

    [Fact]
    public void UpdateSignatureIsVerifiedAndTamperingRejected()
    {
        using var key = RSA.Create(2048);
        var payload = Encoding.UTF8.GetBytes("{\"Version\":\"1.2.3\",\"Url\":\"https://example.com/release.zip\",\"Sha256\":\"" + new string('a', 64) + "\"}");
        var signed = new SignedUpdate { Payload = Convert.ToBase64String(payload), Signature = Convert.ToBase64String(key.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) };
        Assert.Equal("1.2.3", UpdateService.VerifySigned(signed, key.ExportSubjectPublicKeyInfoPem()).Version);
        payload[12] ^= 1; signed.Payload = Convert.ToBase64String(payload);
        Assert.Throws<CryptographicException>(() => UpdateService.VerifySigned(signed, key.ExportSubjectPublicKeyInfoPem()));
    }

    [Fact]
    public void UpdateRejectsInsecureDownloadUrlEvenWhenSigned()
    {
        using var key = RSA.Create(2048);
        var payload = Encoding.UTF8.GetBytes("{\"Version\":\"1.2.3\",\"Url\":\"http://example.com/release.zip\",\"Sha256\":\"" + new string('a', 64) + "\"}");
        var signed = new SignedUpdate { Payload = Convert.ToBase64String(payload), Signature = Convert.ToBase64String(key.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) };
        Assert.Throws<InvalidDataException>(() => UpdateService.VerifySigned(signed, key.ExportSubjectPublicKeyInfoPem()));
    }

    [Theory]
    [InlineData("../escaped.exe")]
    [InlineData("C:/escaped.exe")]
    [InlineData("folder/../../escaped.exe")]
    public void UpdateZipRejectsTraversal(string entryName)
    {
        var path = Path.Combine(_root, "unsafe.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create)) { using var writer = new StreamWriter(zip.CreateEntry(entryName).Open()); writer.Write("bad"); }
        Assert.Throws<InvalidDataException>(() => UpdateService.ExtractPackage(path, Path.Combine(_root, "extracted")));
    }

    [Fact]
    public void UpdateZipRequiresApplicationAtRoot()
    {
        var path = Path.Combine(_root, "missing.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create)) { using var writer = new StreamWriter(zip.CreateEntry("other.txt").Open()); writer.Write("text"); }
        Assert.Throws<InvalidDataException>(() => UpdateService.ExtractPackage(path, Path.Combine(_root, "extracted")));
    }

    [Fact]
    public void UpdateZipRejectsDuplicateNames()
    {
        var path = Path.Combine(_root, "duplicate.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create)) { zip.CreateEntry("ChromieLauncher.exe"); zip.CreateEntry("chromielauncher.exe"); }
        Assert.Throws<InvalidDataException>(() => UpdateService.ExtractPackage(path, Path.Combine(_root, "extracted")));
    }

    [Fact]
    public void SettingsRoundTripAndProfilesAreIsolated()
    {
        _storage.SaveSettings(new() { GamePath = Game, CheckUpdatesAtStartup = false });
        Assert.Equal(Game, _storage.LoadSettings().GamePath); Assert.False(_storage.LoadSettings().CheckUpdatesAtStartup);
        Assert.NotEqual(_storage.Profile(Game), _storage.Profile(Path.Combine(_root, "other")));
    }

    private AddonPackage Package(string name, string? dependency = null, string? toc = null)
    {
        var package = new AddonPackage { Id = name, Name = name, Folders = [name], Dependencies = dependency is null ? [] : [dependency] };
        _catalog.Packages.Add(package);
        AddFile(name + "/" + name + ".toc", toc ?? "## Interface: 30300\n## Title: " + name + "\n## Version: 1.0\n" + (dependency is null ? "" : "## Dependencies: " + dependency));
        return package;
    }

    private void AddFile(string path, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text); _blobs[path] = bytes;
        var prefix = Encoding.ASCII.GetBytes("blob " + bytes.Length + "\0");
        var sha = Convert.ToHexString(SHA1.HashData(prefix.Concat(bytes).ToArray())).ToLowerInvariant();
        _catalog.Files.Add(new() { Path = path, Sha = sha, Size = bytes.Length });
    }

    private string Toc(string folder) => Path.Combine(Game, "Interface", "AddOns", folder, folder + ".toc");
    private void WriteAddon(string folder, string text) { var path = Toc(folder); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); }
    public void Dispose() { _http.Dispose(); if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
