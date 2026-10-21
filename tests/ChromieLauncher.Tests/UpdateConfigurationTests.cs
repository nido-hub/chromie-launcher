using System.Security.Cryptography;
using ChromieLauncher.Core;
using Xunit;

namespace ChromieLauncher.Tests;

public sealed class UpdateConfigurationTests
{
    [Fact]
    public void BundledPublisherKeyIsPublicAndAtLeast3072Bits()
    {
        using var stream = typeof(UpdateService).Assembly.GetManifestResourceStream("ChromieLauncher.Core.Data.update-public-key.pem")!;
        using var reader = new StreamReader(stream);
        var key = reader.ReadToEnd();
        Assert.Contains("BEGIN PUBLIC KEY", key);
        Assert.DoesNotContain("PRIVATE KEY", key);
        using var rsa = RSA.Create(); rsa.ImportFromPem(key);
        Assert.True(rsa.KeySize >= 3072);
        Assert.True(UpdateService.IsConfigured);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void DefaultFeedWorksForNewAndExistingEmptySettings(string? feed)
        => Assert.Equal("https://github.com/nido-hub/chromie-launcher/releases/latest/download/update.json", UpdateService.ResolveFeedUrl(feed));

    [Fact]
    public void ExplicitCustomFeedIsPreserved()
        => Assert.Equal("https://example.org/update.json", UpdateService.ResolveFeedUrl(" https://example.org/update.json "));
}
