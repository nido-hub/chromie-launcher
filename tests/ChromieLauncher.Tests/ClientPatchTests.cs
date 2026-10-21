using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ChromieLauncher.Core;
using Xunit;

namespace ChromieLauncher.Tests;

public sealed class ClientPatchTests : IDisposable
{
    private readonly string _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "opencode", "ChromieLauncher.PatchTests", Guid.NewGuid().ToString("N"));
    private string Game => Path.Combine(_root, "game");
    private string Target => Path.Combine(Game, "Data", "patch-y.mpq");
    private readonly Storage _storage;

    public ClientPatchTests()
    {
        Directory.CreateDirectory(Path.Combine(Game, "Data"));
        File.WriteAllText(Path.Combine(Game, "Wow.exe"), "test placeholder, never executed");
        _storage = new(Path.Combine(_root, "data"));
    }

    [Fact]
    public void PatchCatalogContainsTwoDistinctPinnedVariants()
    {
        Assert.Equal(2, ClientPatchCatalog.All.Count);
        Assert.Equal("patch-y.mpq", ClientPatchCatalog.FileName);
        Assert.Single(ClientPatchCatalog.All, p => p.Hd);
        Assert.Single(ClientPatchCatalog.All, p => !p.Hd);
        foreach (var patch in ClientPatchCatalog.All)
        {
            Assert.Equal(64, patch.Sha256.Length); Assert.All(patch.Sha256, c => Assert.True(Uri.IsHexDigit(c)));
            Assert.InRange(patch.Size, 26_000_000, 30_000_000);
            Assert.Equal(32, MegaReference.Parse(patch.ShareUrl).Key.Length);
        }
        Assert.NotEqual(ClientPatchCatalog.All[0].Sha256, ClientPatchCatalog.All[1].Sha256);
    }

    [Theory]
    [InlineData("http://mega.nz/file/abcd1234#abc")]
    [InlineData("https://example.com/file/abcd1234#abc")]
    [InlineData("https://mega.nz/folder/abcd1234#abc")]
    [InlineData("https://mega.nz/file/abcd1234#abc")]
    public void InvalidMegaLinksAreRejected(string link) => Assert.Throws<InvalidDataException>(() => MegaReference.Parse(link));

    [Theory]
    [InlineData(1)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(131072)]
    [InlineData(131073)]
    [InlineData(5242897)]
    public async Task MegaCryptoAuthenticatesPartialBlocksAndVariableChunkBoundaries(int length)
    {
        var plain = Enumerable.Range(0, length).Select(i => (byte)(i * 17 + 9)).ToArray();
        var fixture = EncryptReference(plain);
        using var output = new MemoryStream();
        var sha = await MegaCrypto.DecryptAsync(fixture.Encrypted, fixture.Key, output);
        Assert.Equal(plain, output.ToArray());
        Assert.Equal(Convert.ToHexString(SHA256.HashData(plain)).ToLowerInvariant(), sha);
        Assert.Equal("patch-y.mpq", MegaCrypto.AttributeName(fixture.Attributes, fixture.Key));
    }

    [Fact]
    public async Task MegaCryptoRejectsCorruptedContent()
    {
        var fixture = EncryptReference(Mpq()); fixture.Encrypted[^1] ^= 1;
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<CryptographicException>(() => MegaCrypto.DecryptAsync(fixture.Encrypted, fixture.Key, output));
    }

    [Fact]
    public async Task MegaClientDownloadsAndDecryptsPublicFileWithoutSendingItsKey()
    {
        var plain = Mpq(); var fixture = EncryptReference(plain); var requests = 0;
        using var http = new HttpClient(new Handler(async request =>
        {
            requests++;
            if (request.Method == HttpMethod.Post)
            {
                var body = await request.Content!.ReadAsStringAsync();
                Assert.Contains("abcd1234", body); Assert.DoesNotContain(Encode(fixture.Key), body);
                return Json($"[{{\"s\":{plain.Length},\"g\":\"https://gfs1.userstorage.mega.co.nz/file\",\"at\":\"{fixture.Attributes}\"}}]");
            }
            Assert.Equal("gfs1.userstorage.mega.co.nz", request.RequestUri!.Host);
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(fixture.Encrypted) };
        }));
        using var output = new MemoryStream();
        var result = await new MegaClient(http).DownloadAsync("https://mega.nz/file/abcd1234#" + Encode(fixture.Key), plain.Length, output);
        Assert.Equal(2, requests); Assert.Equal("patch-y.mpq", result.Name); Assert.Equal(plain, output.ToArray());
    }

    [Fact]
    public async Task MegaQuotaErrorsAreActionableAndNeverDownloadContent()
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(Json("[-17]"))));
        using var output = new MemoryStream();
        var error = await Assert.ThrowsAsync<IOException>(() => new MegaClient(http).DownloadAsync(ClientPatchCatalog.All[0].ShareUrl, 128, output));
        Assert.Contains("quota", error.Message); Assert.Equal(0, output.Length);
    }

    [Theory]
    [InlineData("https://example.com/payload")]
    [InlineData("http://gfs1.userstorage.mega.co.nz/payload")]
    [InlineData("https://mega.co.nz.evil.example/payload")]
    public async Task MegaRejectsUntrustedDownloadUrls(string url)
    {
        var fixture = EncryptReference(Mpq());
        using var http = new HttpClient(new Handler(_ => Task.FromResult(Json($"[{{\"s\":128,\"g\":\"{url}\",\"at\":\"{fixture.Attributes}\"}}]"))));
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => new MegaClient(http).DownloadAsync("https://mega.nz/file/abcd1234#" + Encode(fixture.Key), 128, output));
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public async Task InstallUsesDataAndPreservesOtherGameFiles()
    {
        var plain = Mpq(); var patch = Definition(plain);
        var other = Path.Combine(Game, "Data", "common.MPQ"); File.WriteAllText(other, "original game data");
        var wtf = Path.Combine(Game, "WTF", "settings.lua"); Directory.CreateDirectory(Path.GetDirectoryName(wtf)!); File.WriteAllText(wtf, "keep settings");
        await Service(plain).InstallAsync(Game, patch);
        Assert.Equal(plain, File.ReadAllBytes(Target));
        Assert.Equal("original game data", File.ReadAllText(other)); Assert.Equal("keep settings", File.ReadAllText(wtf));
        Assert.False(Directory.Exists(Path.Combine(Game, "Interface", "AddOns", "patch-y.mpq")));
    }

    [Fact]
    public async Task SwitchingVariantsPreservesThePreviousMpq()
    {
        File.WriteAllText(Target, "previous patch");
        var plain = Mpq(); await Service(plain).InstallAsync(Game, Definition(plain) with { Hd = true, SourceName = "patch-y.mpq" });
        var backup = Assert.Single(Directory.GetFiles(ClientPatchService.BackupDirectory(Game), "backup.mpq", SearchOption.AllDirectories));
        Assert.Equal("previous patch", File.ReadAllText(backup)); Assert.Equal(plain, File.ReadAllBytes(Target));
    }

    [Fact]
    public async Task HashMismatchNeverReplacesExistingPatch()
    {
        File.WriteAllText(Target, "original"); var plain = Mpq();
        await Assert.ThrowsAsync<CryptographicException>(() => Service(plain).InstallAsync(Game, Definition(plain) with { Sha256 = new string('0', 64) }));
        Assert.Equal("original", File.ReadAllText(Target));
    }

    [Fact]
    public async Task NonMpqDownloadIsRejectedEvenIfItsHashMatches()
    {
        File.WriteAllText(Target, "original"); var bytes = new byte[128];
        await Assert.ThrowsAsync<InvalidDataException>(() => Service(bytes).InstallAsync(Game, Definition(bytes)));
        Assert.Equal("original", File.ReadAllText(Target));
    }

    [Fact]
    public async Task WrongVariantMetadataIsRejected()
    {
        File.WriteAllText(Target, "original"); var plain = Mpq();
        await Assert.ThrowsAsync<InvalidDataException>(() => Service(plain).InstallAsync(Game, Definition(plain) with { SourceName = "other.mpq" }));
        Assert.Equal("original", File.ReadAllText(Target));
    }

    [Fact]
    public async Task CancellationDoesNotReplaceAnExistingPatch()
    {
        File.WriteAllText(Target, "original"); var plain = Mpq();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(plain).InstallAsync(Game, Definition(plain), token: cancellation.Token));
        Assert.Equal("original", File.ReadAllText(Target));
    }

    [Fact]
    public async Task SharedGameLockPreventsConcurrentAddonAndPatchOperations()
    {
        var plain = Mpq();
        using var operation = new FileStream(Path.Combine(_storage.Profile(Game), "operation.lock"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await Assert.ThrowsAsync<IOException>(() => Service(plain).InstallAsync(Game, Definition(plain)));
        Assert.False(File.Exists(Target));
    }

    [Fact]
    public async Task GameMustBeClosedBeforeThePatchIsApplied()
    {
        var plain = Mpq(); var checks = 0;
        var service = new ClientPatchService(_storage, new FakeDownload(plain), () => { if (++checks == 2) throw new IOException("game started"); });
        File.WriteAllText(Target, "original");
        await Assert.ThrowsAsync<IOException>(() => service.InstallAsync(Game, Definition(plain)));
        Assert.Equal("original", File.ReadAllText(Target));
    }

    [Fact]
    public async Task InterruptedReplacementIsRecoveredWithoutDiscardingAnyFiles()
    {
        var work = Path.Combine(ClientPatchService.BackupDirectory(Game), "interrupted"); Directory.CreateDirectory(work);
        File.WriteAllText(Path.Combine(work, "backup.mpq"), "original patch"); File.WriteAllText(Target, "interrupted replacement");
        Storage.AtomicWrite(Path.Combine(work, "journal.json"), JsonSerializer.Serialize(new PatchJournal { HadOriginal = true, VariantId = "old" }, AppJsonContext.Default.PatchJournal));
        var plain = Mpq();
        await Assert.ThrowsAsync<CryptographicException>(() => Service(plain).InstallAsync(Game, Definition(plain) with { Sha256 = new string('0', 64) }));
        Assert.Equal("original patch", File.ReadAllText(Target));
        Assert.Equal("interrupted replacement", File.ReadAllText(Assert.Single(Directory.GetFiles(work, "displaced-*.mpq"))));
        Assert.True(File.Exists(Path.Combine(work, "rolled-back")));
    }

    [Fact]
    public async Task InspectDistinguishesMissingAndUnrecognizedPatches()
    {
        Assert.False((await Service(Mpq()).InspectAsync(Game)).Exists);
        File.WriteAllText(Target, "manual unknown patch");
        var status = await Service(Mpq()).InspectAsync(Game);
        Assert.True(status.Exists); Assert.Null(status.VariantId);
    }

    private static byte[] Mpq() { var bytes = new byte[128]; "MPQ\u001a"u8.CopyTo(bytes); return bytes; }
    private static ClientPatch Definition(byte[] bytes) => new("test", "Patch Y test", false, ClientPatchCatalog.All[0].ShareUrl, "patch-y.mpq", bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    private ClientPatchService Service(byte[] bytes) => new(_storage, new FakeDownload(bytes), () => { });
    private sealed class FakeDownload(byte[] bytes) : IMegaDownloader
    {
        public async Task<MegaDownload> DownloadAsync(string shareUrl, long expectedSize, Stream destination, IProgress<TransferProgress>? progress = null, CancellationToken token = default)
        {
            await destination.WriteAsync(bytes, token);
            return new("patch-y.mpq", bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }
    }

    // Independent block-by-block reference encryption; production uses batched AES operations.
    private static (byte[] Encrypted, byte[] Key, string Attributes) EncryptReference(byte[] plain)
    {
        using var aes = Aes.Create(); aes.Key = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();
        var nonce = Enumerable.Range(33, 8).Select(i => (byte)i).ToArray();
        var cipher = new byte[plain.Length]; var fileMac = new byte[16]; ulong counter = 0; var start = 0;
        for (var chunk = 1; start < plain.Length; chunk++)
        {
            var end = Math.Min(start + Math.Min(chunk, 8) * 131072, plain.Length);
            var mac = nonce.Concat(nonce).ToArray();
            for (var position = start; position < end; position += 16)
            {
                var block = new byte[16]; nonce.CopyTo(block, 0); BinaryPrimitives.WriteUInt64BigEndian(block.AsSpan(8), counter++);
                var stream = aes.EncryptEcb(block, PaddingMode.None);
                for (var i = 0; i < Math.Min(16, end - position); i++) { cipher[position + i] = (byte)(plain[position + i] ^ stream[i]); mac[i] ^= plain[position + i]; }
                mac = aes.EncryptEcb(mac, PaddingMode.None);
            }
            for (var i = 0; i < 16; i++) fileMac[i] ^= mac[i];
            fileMac = aes.EncryptEcb(fileMac, PaddingMode.None); start = end;
        }
        var key = new byte[32]; nonce.CopyTo(key, 16);
        for (var i = 0; i < 4; i++) { key[24 + i] = (byte)(fileMac[i] ^ fileMac[4 + i]); key[28 + i] = (byte)(fileMac[8 + i] ^ fileMac[12 + i]); }
        for (var i = 0; i < 16; i++) key[i] = (byte)(aes.Key[i] ^ key[16 + i]);
        var attributes = Encoding.UTF8.GetBytes("MEGA{\"n\":\"patch-y.mpq\"}"); Array.Resize(ref attributes, (attributes.Length + 15) / 16 * 16);
        return (cipher, key, Encode(aes.EncryptCbc(attributes, new byte[16], PaddingMode.None)));
    }

    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handle(request);
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
