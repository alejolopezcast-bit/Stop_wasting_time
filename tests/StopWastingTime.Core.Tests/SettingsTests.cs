using System.Globalization;
using StopWastingTime.Core.Settings;

namespace StopWastingTime.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"swt-settings-{Guid.NewGuid():N}");

    private string SettingsPath => Path.Combine(_directory, "settings.json");

    [Fact]
    public void With_no_file_yet_the_defaults_come_back()
    {
        var settings = new SettingsStore(SettingsPath).Load();

        Assert.Null(settings.Language);
    }

    [Fact]
    public void A_saved_language_is_there_on_the_next_start()
    {
        new SettingsStore(SettingsPath).Save(new AppSettings { Language = "en" });

        Assert.Equal("en", new SettingsStore(SettingsPath).Load().Language);
    }

    [Fact]
    public void Saving_creates_the_folder_if_it_is_missing()
    {
        new SettingsStore(SettingsPath).Save(new AppSettings { Language = "es" });

        Assert.True(File.Exists(SettingsPath));
        Assert.False(File.Exists(SettingsPath + ".tmp"));
    }

    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("")]
    [InlineData("null")]
    public void A_mangled_file_falls_back_to_the_defaults_instead_of_stopping_the_app(string content)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, content);

        Assert.Null(new SettingsStore(SettingsPath).Load().Language);
    }

    [Fact]
    public void Settings_written_by_a_newer_version_still_load()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, """{ "Language": "es", "SomethingFromTheFuture": 42 }""");

        Assert.Equal("es", new SettingsStore(SettingsPath).Load().Language);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}

public class AppLanguagesTests
{
    [Fact]
    public void A_language_picked_in_the_app_beats_the_one_windows_speaks()
    {
        Assert.Equal("en", AppLanguages.Resolve("en", CultureInfo.GetCultureInfo("es-AR")));
    }

    [Theory]
    [InlineData("es-AR", "es")]
    [InlineData("es-ES", "es")]
    [InlineData("en-GB", "en")]
    [InlineData("en-US", "en")]
    public void Without_a_choice_the_language_of_windows_is_used(string windows, string expected)
    {
        Assert.Equal(expected, AppLanguages.Resolve(null, CultureInfo.GetCultureInfo(windows)));
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("ja-JP")]
    public void A_windows_language_without_a_translation_gets_english(string windows)
    {
        Assert.Equal(AppLanguages.English, AppLanguages.Resolve(null, CultureInfo.GetCultureInfo(windows)));
    }

    [Fact]
    public void A_saved_language_that_is_no_longer_offered_is_ignored()
    {
        Assert.Equal("es", AppLanguages.Resolve("klingon", CultureInfo.GetCultureInfo("es-AR")));
    }

    [Theory]
    [InlineData("EN", "en")]
    [InlineData(" es ", "es")]
    [InlineData("es-MX", "es")]
    [InlineData("en_GB", "en")]
    public void Codes_are_read_the_way_people_write_them(string code, string expected)
    {
        Assert.True(AppLanguages.TryNormalize(code, out var language));
        Assert.Equal(expected, language);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("pt")]
    public void Anything_else_is_not_a_language_the_app_has(string? code)
    {
        Assert.False(AppLanguages.TryNormalize(code, out _));
    }
}
