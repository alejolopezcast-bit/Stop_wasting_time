using System.Text.RegularExpressions;
using System.Xml.Linq;
using StopWastingTime.Core.Settings;

namespace StopWastingTime.Core.Tests;

/// <summary>
/// The translations live in the app project, which these tests cannot reference, so they are read as the
/// XML files they are. A key missing from a language shows up on screen as the raw key, and a
/// translation that loses a placeholder either drops a number or throws when it is formatted. Both are
/// cheaper to catch here than on someone's screen halfway through a session.
/// </summary>
public partial class LocalizationTests
{
    private static readonly string Repository = FindRepository();

    private static readonly string AppDirectory = Path.Combine(Repository, "src", "StopWastingTime.App");

    private static readonly string ResourcesDirectory = Path.Combine(AppDirectory, "Resources");

    private static readonly IReadOnlyDictionary<string, string> English =
        ReadStrings(Path.Combine(ResourcesDirectory, "Strings.resx"));

    /// <summary>Every translation next to the English file, so a new language is covered the day it lands.</summary>
    public static TheoryData<string> Translations => new(TranslationFiles());

    [Fact]
    public void Every_language_the_app_offers_has_a_translation()
    {
        var translated = TranslationFiles().ToList();

        foreach (var language in AppLanguages.Supported.Where(code => code != AppLanguages.English))
        {
            Assert.Contains($"Strings.{language}.resx", translated);
        }
    }

    [Theory]
    [MemberData(nameof(Translations))]
    public void A_translation_has_exactly_the_keys_english_has(string file)
    {
        var translation = ReadStrings(Path.Combine(ResourcesDirectory, file));

        Assert.Empty(English.Keys.Except(translation.Keys));
        Assert.Empty(translation.Keys.Except(English.Keys));
    }

    [Theory]
    [MemberData(nameof(Translations))]
    public void A_translation_keeps_every_placeholder(string file)
    {
        var translation = ReadStrings(Path.Combine(ResourcesDirectory, file));

        var mismatched = English
            .Where(pair => translation.ContainsKey(pair.Key))
            .Where(pair => !Placeholders(pair.Value).SetEquals(Placeholders(translation[pair.Key])))
            .Select(pair => pair.Key)
            .ToList();

        Assert.Empty(mismatched);
    }

    [Theory]
    [MemberData(nameof(Translations))]
    public void No_translation_is_left_empty(string file)
    {
        var translation = ReadStrings(Path.Combine(ResourcesDirectory, file));

        Assert.Empty(translation.Where(pair => string.IsNullOrWhiteSpace(pair.Value)).Select(pair => pair.Key));
    }

    [Fact]
    public void Plural_sentences_come_in_pairs()
    {
        var unpaired = English.Keys
            .Where(key => key.EndsWith("_One", StringComparison.Ordinal) || key.EndsWith("_Other", StringComparison.Ordinal))
            .Select(key => key[..key.LastIndexOf('_')])
            .Distinct()
            .Where(stem => !English.ContainsKey(stem + "_One") || !English.ContainsKey(stem + "_Other"))
            .ToList();

        Assert.Empty(unpaired);
    }

    /// <summary>
    /// A key the app asks for but nobody wrote. Keys are recognised by their shape in the source, so the
    /// check needs no list of its own to keep up to date.
    /// </summary>
    [Fact]
    public void Every_key_the_app_asks_for_exists()
    {
        var requested = RequestedKeys();

        // A guard against the scan silently finding nothing, which would make this test pass forever.
        Assert.True(requested.Count > 100, $"Only {requested.Count} keys found in the app's source.");

        Assert.Empty(requested.Where(key => !Exists(key)).Order());
    }

    [Fact]
    public void No_key_is_left_behind_unused()
    {
        var requested = RequestedKeys();

        var unused = English.Keys
            .Where(key => !requested.Contains(key) && !requested.Contains(PluralStem(key)))
            .Order()
            .ToList();

        Assert.Empty(unused);
    }

    private static IEnumerable<string> TranslationFiles() =>
        Directory.GetFiles(ResourcesDirectory, "Strings.*.resx").Select(Path.GetFileName).OfType<string>();

    private static bool Exists(string key) =>
        English.ContainsKey(key) || (English.ContainsKey(key + "_One") && English.ContainsKey(key + "_Other"));

    private static string PluralStem(string key) =>
        key.EndsWith("_One", StringComparison.Ordinal) || key.EndsWith("_Other", StringComparison.Ordinal)
            ? key[..key.LastIndexOf('_')]
            : key;

    /// <summary>Every <c>{loc:Tr Key}</c> in the views and every key-shaped string literal in the code.</summary>
    private static HashSet<string> RequestedKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in SourceFiles("*.xaml"))
        {
            foreach (Match match in XamlKey().Matches(File.ReadAllText(file)))
            {
                keys.Add(match.Groups["key"].Value);
            }
        }

        foreach (var file in SourceFiles("*.cs"))
        {
            foreach (Match match in CodeKey().Matches(File.ReadAllText(file)))
            {
                keys.Add(match.Groups["key"].Value);
            }
        }

        return keys;
    }

    private static IEnumerable<string> SourceFiles(string pattern)
    {
        var separator = Path.DirectorySeparatorChar;

        return Directory.GetFiles(AppDirectory, pattern, SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{separator}obj{separator}", StringComparison.Ordinal))
            .Where(file => !file.Contains($"{separator}bin{separator}", StringComparison.Ordinal));
    }

    private static HashSet<int> Placeholders(string text) =>
        Placeholder().Matches(text).Select(match => int.Parse(match.Groups["index"].Value)).ToHashSet();

    private static Dictionary<string, string> ReadStrings(string path) =>
        XDocument.Load(path).Root!
            .Elements("data")
            .ToDictionary(
                element => (string)element.Attribute("name")!,
                element => (string?)element.Element("value") ?? string.Empty,
                StringComparer.Ordinal);

    /// <summary>Walks up from the test binaries to the folder holding the solution.</summary>
    private static string FindRepository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "StopWastingTime.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Could not find the repository above " + AppContext.BaseDirectory);
    }

    [GeneratedRegex(@"\{loc:Tr (?<key>\w+)\}")]
    private static partial Regex XamlKey();

    // Keys read like Focus_Title or Stats_Period_Day: a capitalised word, an underscore, then more.
    [GeneratedRegex(@"""(?<key>[A-Z][a-z][A-Za-z0-9]*_[A-Za-z0-9_]+)""")]
    private static partial Regex CodeKey();

    [GeneratedRegex(@"\{(?<index>\d+)(?:[,:][^}]*)?\}")]
    private static partial Regex Placeholder();
}
