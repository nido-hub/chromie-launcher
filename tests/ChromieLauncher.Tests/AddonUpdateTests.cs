using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ChromieLauncher.Core;
using Xunit;

namespace ChromieLauncher.Tests;

public sealed class AddonUpdateTests : IDisposable
{
    private const string Old = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Next = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private readonly Catalog _catalog = new() { Commit = Old };
    private readonly ManagedState _state = new();
    private readonly List<InstalledAddon> _installed = [];
    private readonly Dictionary<string, Dictionary<string, byte[]>> _files = [];
    private readonly List<string> _requests = [];
    private readonly HttpClient _http;
    private readonly AddonUpdateService _service;
    private readonly AddonPackage _package;
    private string _comparison = "ahead";
    private string _mode = "100644";
    private bool _truncated;
    private bool _offline;
    private bool _tamperToc;

    public AddonUpdateTests()
    {
        _package = new() { Id = "Example", Name = "Example", Version = "1.0", Folders = ["Example"] };
        _catalog.Packages.Add(_package);
        _state.Packages.Add(new() { Id = "Example", Commit = Old, Folders = ["Example"] });
        _installed.Add(new() { Folder = "Example", Name = "Example", Version = "1.0", Managed = true, PackageId = "Example" });
        SetFiles(Old, "1.0"); SetFiles(Next, "1.1");
        _http = new(new Handler(Respond));
        _service = new(_http);
    }

    [Fact]
    public async Task ManagedAddonOffersNewRevisionWithPinnedFilesWithoutMutatingCatalog()
    {
        var result = await Check();
        Assert.Empty(result.Errors); Assert.Equal(1, result.CheckedPackages);
        var update = Assert.Single(result.Updates);
        Assert.Equal("1.1", update.Package.Version); Assert.Equal(Next, update.Package.Commit); Assert.False(update.Manual);
        Assert.All(update.Files, f => { Assert.Equal(Next, f.Commit); Assert.Equal(_package.Repository, f.Repository); });
        Assert.Equal("1.0", _catalog.Packages[0].Version); Assert.Empty(_catalog.Files);
        AddonUpdateService.Apply(_catalog, update);
        Assert.Same(update.Package, _catalog.Packages[0]); Assert.Equal(update.Files.Count, _catalog.Files.Count);
    }

    [Fact]
    public async Task ManagedCodeChangeWithoutTocVersionBumpIsDetected()
    {
        SetFiles(Next, "1.0"); _files[Next]["Example/Core.lua"] = Encoding.UTF8.GetBytes("-- changed implementation");
        Assert.Single((await Check()).Updates);
    }

    [Fact]
    public async Task UnrelatedRepositoryChangesDoNotOfferAnUpdate()
    {
        SetFiles(Next, "1.0"); _files[Next]["Other/Other.toc"] = Encoding.UTF8.GetBytes("## Interface: 30300");
        var result = await Check();
        Assert.Empty(result.Updates); Assert.Empty(result.Errors);
        Assert.DoesNotContain(_requests, r => r.StartsWith("https://raw.githubusercontent.com/"));
    }

    [Fact]
    public async Task AlreadyInstalledRevisionDoesNotOfferAnUpdate()
    {
        _state.Packages[0].Commit = Next;
        Assert.Empty((await Check()).Updates);
        Assert.DoesNotContain(_requests, r => r.Contains("/compare/"));
    }

    [Theory]
    [InlineData("behind")]
    [InlineData("diverged")]
    [InlineData("identical")]
    public async Task OlderOrRewrittenHistoryNeverOffered(string status)
    {
        _comparison = status;
        Assert.Empty((await Check()).Updates);
    }

    [Fact]
    public async Task ManagedNumericDowngradeIsRefused()
    {
        SetFiles(Next, "0.9");
        var result = await Check(); Assert.Empty(result.Updates); Assert.Single(result.Errors);
    }

    [Fact]
    public async Task ManualAddonWithClearlyOlderVersionCanBeUpdatedExplicitly()
    {
        _state.Packages.Clear();
        var result = await Check(); var update = Assert.Single(result.Updates);
        Assert.True(update.Manual); Assert.Equal("1.0", update.InstalledVersion);
    }

    [Theory]
    [InlineData("1.1")]
    [InlineData("9.0")]
    [InlineData("Unknown")]
    [InlineData("r123")]
    [InlineData("1.0-custom")]
    public async Task ManualUnknownEqualOrNewerVersionsAreNeverAdopted(string installed)
    {
        _state.Packages.Clear(); _installed[0].Version = installed;
        Assert.Empty((await Check()).Updates);
    }

    [Theory]
    [InlineData("1.2", "1.1", true)]
    [InlineData("v1.2.0", "1.1.9", true)]
    [InlineData("1.0.0", "1.0", false)]
    [InlineData("1.0-beta", "1.0", false)]
    [InlineData("r99", "r98", false)]
    [InlineData("1.0", "Unknown", false)]
    public void NumericVersionsAreComparedConservatively(string next, string current, bool expected)
        => Assert.Equal(expected, AddonUpdateService.IsNewerVersion(next, current));

    [Fact]
    public async Task IncompleteOrUnknownAddonsDoNotTriggerNetworkRequests()
    {
        _package.Folders.Add("MissingModule");
        _installed.Add(new() { Folder = "NotInCatalog", Version = "1.0" });
        Assert.Empty((await Check()).Updates); Assert.Empty(_requests);
    }

    [Fact]
    public async Task ApiErrorsAreReportedInsteadOfClaimingUpToDate()
    {
        _offline = true;
        var result = await Check(); Assert.Empty(result.Updates); Assert.Single(result.Errors); Assert.Equal(0, result.CheckedPackages);
    }

    [Fact]
    public async Task TruncatedTreesAreRefused()
    {
        _truncated = true;
        var result = await Check(); Assert.Empty(result.Updates); Assert.Single(result.Errors);
    }

    [Theory]
    [InlineData("120000")]
    [InlineData("160000")]
    [InlineData("100755")]
    public async Task SymlinksSubmodulesAndExecutableModesAreRefused(string mode)
    {
        _mode = mode;
        var result = await Check(); Assert.Empty(result.Updates); Assert.Single(result.Errors);
    }

    [Fact]
    public async Task TraversalPathsAreRefused()
    {
        _files[Next]["Example/../escape.lua"] = Encoding.UTF8.GetBytes("not allowed");
        var result = await Check(); Assert.Empty(result.Updates); Assert.Single(result.Errors);
    }

    [Fact]
    public async Task IncompatibleTocIsRefused()
    {
        _files[Next]["Example/Example.toc"] = Encoding.UTF8.GetBytes("## Interface: 110200\n## Version: 1.1");
        var result = await Check(); Assert.Empty(result.Updates); Assert.Single(result.Errors);
    }

    [Fact]
    public async Task CorruptTocIsRefused()
    {
        _tamperToc = true;
        var result = await Check(); Assert.Empty(result.Updates); Assert.Single(result.Errors);
    }

    [Fact]
    public async Task NewAndRemovedRuntimeFilesAreIncludedInTheProposal()
    {
        _files[Next].Remove("Example/Core.lua"); _files[Next]["Example/New.lua"] = Encoding.UTF8.GetBytes("-- new");
        var update = Assert.Single((await Check()).Updates);
        Assert.DoesNotContain(update.Files, f => f.Path.EndsWith("Core.lua"));
        Assert.Contains(update.Files, f => f.Path.EndsWith("New.lua"));
    }

    [Fact]
    public async Task UnsupportedPackageLayoutDoesNotOfferAnIncompleteUpdate()
    {
        _files[Next]["Example/Example.toc"] = Encoding.UTF8.GetBytes("## Interface: 30300\n## Version: 1.1\nMissing.lua");
        var result = await Check(); Assert.Empty(result.Updates); Assert.Single(result.Errors);
    }

    [Fact]
    public async Task UnknownRequiredDependencyBlocksUpdate()
    {
        _files[Next]["Example/Example.toc"] = Encoding.UTF8.GetBytes("## Interface: 30300\n## Version: 1.1\n## Dependencies: Missing");
        var result = await Check(); Assert.Empty(result.Updates); Assert.Single(result.Errors);
    }

    [Fact]
    public async Task CommunityRootFilesMapToCorrectFolderAndExcludeDevelopmentFiles()
    {
        _package.Id = "LootSummary"; _package.Name = "LootRolls"; _package.Folders = ["LootSummary"];
        _package.Repository = "nido-hub/loot-rolls";
        _state.Packages[0].Id = "LootSummary"; _state.Packages[0].Folders = ["LootSummary"];
        _installed[0].Folder = "LootSummary";
        foreach (var revision in new[] { Old, Next })
        {
            var version = revision == Old ? "1.0" : "1.1";
            _files[revision] = new() { ["LootSummary.toc"] = Encoding.UTF8.GetBytes("## Interface: 30300\n## Version: " + version),
                ["Core.lua"] = Encoding.UTF8.GetBytes("-- addon"), ["Tests/test.lua"] = Encoding.UTF8.GetBytes("-- never ship"),
                [".github/workflows/build.yml"] = Encoding.UTF8.GetBytes("-- never ship"), ["tool.exe"] = [0, 1, 2] };
        }
        var update = Assert.Single((await Check()).Updates);
        Assert.Equal(2, update.Files.Count);
        Assert.All(update.Files, f => Assert.StartsWith("LootSummary/", f.Path));
        Assert.Contains(update.Files, f => f.SourcePath == "Core.lua");
    }

    [Fact]
    public async Task SourceChecksAreDeduplicatedForSeveralInstalledPackages()
    {
        _catalog.Packages.Add(new() { Id = "Other", Name = "Other", Folders = ["Other"] });
        _state.Packages.Add(new() { Id = "Other", Commit = Old, Folders = ["Other"] });
        _installed.Add(new() { Folder = "Other", Version = "1.0" });
        foreach (var revision in new[] { Old, Next })
            _files[revision]["Other/Other.toc"] = Encoding.UTF8.GetBytes("## Interface: 30300\n## Version: 1.0");
        var result = await Check(); Assert.Single(result.Updates); Assert.Equal(2, result.CheckedPackages);
        Assert.Single(_requests, r => r.Contains("/commits?"));
        Assert.Single(_requests, r => r.Contains("/compare/"));
        Assert.Equal(2, _requests.Count(r => r.Contains("/git/trees/")));
    }

    [Fact]
    public async Task CancellationPropagatesWithoutOfferingPartialUpdates()
    {
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.CheckAsync(_catalog, _state, _installed, cancel.Token));
    }

    private Task<AddonUpdateCheck> Check() => _service.CheckAsync(_catalog, _state, _installed);
    private void SetFiles(string revision, string version) => _files[revision] = new()
    {
        ["Example/Example.toc"] = Encoding.UTF8.GetBytes("## Interface: 30300\n## Version: " + version),
        ["Example/Core.lua"] = Encoding.UTF8.GetBytes("-- addon")
    };
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA1.HashData(Encoding.ASCII.GetBytes("blob " + bytes.Length + "\0").Concat(bytes).ToArray())).ToLowerInvariant();
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    private HttpResponseMessage Respond(HttpRequestMessage request)
    {
        var uri = request.RequestUri!; _requests.Add(uri.AbsoluteUri);
        if (_offline) return new(HttpStatusCode.Forbidden);
        if (uri.AbsolutePath.EndsWith("/commits")) return Json(new[] { new { sha = Next } });
        if (uri.AbsolutePath.Contains("/compare/")) return Json(new { status = _comparison });
        var revision = uri.AbsolutePath.Contains(Next) ? Next : Old;
        if (uri.AbsolutePath.Contains("/git/trees/"))
            return Json(new { truncated = _truncated, tree = _files[revision].Select(f => new { path = f.Key, sha = Hash(f.Value), size = f.Value.Length, mode = _mode, type = "blob" }).ToArray() });
        if (uri.Host == "raw.githubusercontent.com")
        {
            var path = Uri.UnescapeDataString(uri.AbsolutePath.Split('/' + revision + '/')[^1]);
            var bytes = _tamperToc ? Encoding.UTF8.GetBytes("tampered") : _files[revision][path];
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        }
        throw new InvalidOperationException("Unexpected request: " + uri);
    }
    public void Dispose() => _http.Dispose();
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(respond(request)); }
    }
}
