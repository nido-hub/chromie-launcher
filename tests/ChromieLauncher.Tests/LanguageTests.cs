using System.Globalization;
using System.Text.Json;
using ChromieLauncher.Core;
using Xunit;

namespace ChromieLauncher.Tests;

[Collection("Launcher language")]
public sealed class LanguageTests : IDisposable
{
    private readonly string _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "opencode", "ChromieLauncher.Tests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("en", "Settings", "Save settings", "Cancel")]
    [InlineData("fr", "Paramètres", "Enregistrer les paramètres", "Annuler")]
    [InlineData("it", "Impostazioni", "Salva impostazioni", "Annulla")]
    public void SupportedLanguagesTranslateControls(string language, string settings, string save, string cancel)
    {
        Assert.Equal(settings, LauncherText.Get("Settings", language));
        Assert.Equal(save, LauncherText.Get("Save settings", language));
        Assert.Equal(cancel, LauncherText.Get("Cancel", language));
        Assert.Equal("Chromie Wishlist", LauncherText.Get("Chromie Wishlist", language));
        Assert.Equal("An external technical diagnostic", LauncherText.Get("An external technical diagnostic", language));
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("it")]
    public void OneClickLauncherUpdateControlsAreTranslated(string language)
    {
        foreach (var text in new[]
        {
            "Check for launcher updates", "Update and restart", "Advanced update settings",
            "Click the version number to update and restart the launcher. Your addons and game settings are preserved.",
            "The launcher will download and verify the update, then restart automatically. Your addons and game settings will not be modified."
        }) Assert.NotEqual(text, LauncherText.Get(text, language));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("de")]
    [InlineData("fr-FR")]
    public void InvalidLanguageFallsBackToEnglish(string? language)
    {
        Assert.Equal("en", LauncherText.NormalizeLanguage(language));
        if (language is not null) Assert.Equal("Settings", LauncherText.Get("Settings", language));
    }

    [Fact]
    public void ExistingSettingsDefaultToEnglishWithoutLosingPreferences()
    {
        var settings = JsonSerializer.Deserialize("""{"GamePath":"C:\\Games\\WoW","CheckUpdatesAtStartup":false,"UpdateFeedUrl":"https://example.org/update.json"}""", AppJsonContext.Default.Settings)!;
        Assert.Equal("en", settings.Language);
        Assert.Equal("C:\\Games\\WoW", settings.GamePath);
        Assert.False(settings.CheckUpdatesAtStartup);
        Assert.Equal("https://example.org/update.json", settings.UpdateFeedUrl);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    [InlineData("it")]
    public void LanguageSurvivesSavingAndReloading(string language)
    {
        var storage = new Storage(_root);
        storage.SaveSettings(new Settings { Language = language, GamePath = "C:\\Games\\WoW", CheckUpdatesAtStartup = false });
        var restored = new Storage(_root).LoadSettings();
        Assert.Equal(language, restored.Language);
        Assert.Equal("C:\\Games\\WoW", restored.GamePath);
        Assert.False(restored.CheckUpdatesAtStartup);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    [InlineData("it")]
    public void TranslatedMessagesPreserveFormatArguments(string language)
    {
        foreach (var template in new[]
        {
            "{0} folders · Blizzard modules are hidden",
            "{0} pack · {1} pinned sources",
            "{0} packs · {1} pinned sources",
            "Downloading {0} · {1:F2} / {2:F2} MiB",
            "Install {0}?\n\nDownload: {1:F2} MiB from MEGA\nDestination: {2}\n\n",
            "Install {0}?\n\nSource: {1}\nVersion: {2}\nPacks and dependencies: {3}\n\n"
        })
        {
            var localized = LauncherText.Get(template, language);
            if (language != "en") Assert.NotEqual(template, localized);
            var formatted = string.Format(CultureInfo.InvariantCulture, localized, "Example", 2.5, 3.5, "Dependency");
            Assert.Contains("Example", formatted);
            Assert.DoesNotContain("{", formatted);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Fact]
    public async Task SwitchingLanguageAppliesAcrossAsyncCallbacks()
    {
        var original = LauncherText.Language;
        try
        {
            foreach (var (language, expected, number) in new[] { ("en", "Settings", "2.50"), ("fr", "Paramètres", "2,50"), ("it", "Impostazioni", "2,50") })
            {
                await Task.Run(() => LauncherText.SetLanguage(language));
                Assert.Equal(expected, LauncherText.Get("Settings"));
                Assert.Contains(number, LauncherText.Format("Downloading {0} · {1:F2} / {2:F2} MiB", "Example", 2.5, 3.5));
                Assert.Equal(expected, await Task.Run(() => LauncherText.Get("Settings")));
            }
        }
        finally { LauncherText.SetLanguage(original); }
    }
}

[CollectionDefinition("Launcher language", DisableParallelization = true)]
public sealed class LanguageCollection;
