using System.Globalization;
using System.Resources;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StopWastingTime.Core.Settings;

namespace StopWastingTime.App.Localization;

/// <summary>
/// Every piece of text the interface shows, in the language that was picked. Views reach it through
/// <see cref="TrExtension"/> and update on the spot when the language changes; view models ask it for
/// their sentences and redraw the ones they keep when <see cref="LanguageChanged"/> fires.
/// </summary>
public sealed partial class Localizer : ObservableObject
{
    /// <summary>The name WPF listens for to refresh every binding to the indexer at once.</summary>
    private const string IndexerName = "Item[]";

    private static readonly ResourceManager Strings =
        new("StopWastingTime.App.Resources.Strings", typeof(Localizer).Assembly);

    // Taken before anything is changed, so the person's own regional settings and the language Windows
    // is displayed in are still known after the app has switched cultures.
    private readonly CultureInfo _systemCulture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _systemUiCulture = CultureInfo.CurrentUICulture;

    private CultureInfo _uiCulture = CultureInfo.InvariantCulture;

    private Localizer()
    {
        // One entry per language in AppLanguages.Supported, each named in itself so it can be found by
        // someone who cannot read the language currently on screen.
        Languages =
        [
            new LanguageOption(AppLanguages.Spanish, "Español"),
            new LanguageOption(AppLanguages.English, "English")
        ];

        Apply(AppLanguages.Resolve(null, _systemUiCulture));
    }

    /// <summary>One for the whole app: XAML has no other way to reach it.</summary>
    public static Localizer Instance { get; } = new();

    /// <summary>The choices the language picker offers.</summary>
    public IReadOnlyList<LanguageOption> Languages { get; }

    public LanguageOption Current { get; private set; } = null!;

    /// <summary>
    /// How numbers and dates are written. When Windows already speaks the chosen language its regional
    /// settings are kept, so someone in Argentina still sees their own date order in Spanish.
    /// </summary>
    public CultureInfo Culture { get; private set; } = CultureInfo.InvariantCulture;

    public event EventHandler? LanguageChanged;

    /// <summary>The text for a key. A key without text shows up as itself, which is hard to miss.</summary>
    public string this[string key] => Strings.GetString(key, _uiCulture) ?? key;

    public string Format(string key, params object?[] args) => string.Format(Culture, this[key], args);

    /// <summary>
    /// Picks <c>key_One</c> or <c>key_Other</c> by the count, which is always <c>{0}</c>; anything else
    /// the sentence needs follows as <c>{1}</c>, <c>{2}</c> and so on.
    /// </summary>
    public string Plural(string key, int count, params object?[] rest) =>
        Format($"{key}_{(count == 1 ? "One" : "Other")}", [count, .. rest]);

    public string Duration(TimeSpan duration) => duration.TotalHours >= 1
        ? Format("Duration_HoursMinutes", (int)duration.TotalHours, duration.Minutes)
        : Format("Duration_Minutes", (int)duration.TotalMinutes);

    /// <summary>A live binding to a key, for text that is set up in code rather than in XAML.</summary>
    public static Binding Bind(string key) => new($"[{key}]") { Source = Instance, Mode = BindingMode.OneWay };

    /// <summary>
    /// Switches to the saved language, or to the one Windows suggests when nothing was saved. Called once
    /// at startup, before anything is listening, so it announces nothing.
    /// </summary>
    public void Initialize(string? savedLanguage) => Apply(AppLanguages.Resolve(savedLanguage, _systemUiCulture));

    public void SetLanguage(string code)
    {
        if (!AppLanguages.TryNormalize(code, out var language) || language == Current.Code)
        {
            return;
        }

        Apply(language);

        OnPropertyChanged(IndexerName);
        OnPropertyChanged(nameof(Current));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void SelectLanguage(LanguageOption? language)
    {
        if (language is not null)
        {
            SetLanguage(language.Code);
        }
    }

    private void Apply(string code)
    {
        Current = Languages.First(language => language.Code == code);

        foreach (var language in Languages)
        {
            language.IsSelected = language == Current;
        }

        _uiCulture = CultureInfo.GetCultureInfo(code);
        Culture = string.Equals(_systemCulture.TwoLetterISOLanguageName, code, StringComparison.OrdinalIgnoreCase)
            ? _systemCulture
            : CultureInfo.GetCultureInfo(code);

        // Also the process default, so anything formatted without asking, and any thread started later,
        // agrees with what is on screen.
        CultureInfo.CurrentCulture = Culture;
        CultureInfo.CurrentUICulture = _uiCulture;
        CultureInfo.DefaultThreadCurrentCulture = Culture;
        CultureInfo.DefaultThreadCurrentUICulture = _uiCulture;
    }
}

/// <summary>One of the languages the picker offers.</summary>
public sealed partial class LanguageOption(string code, string nativeName) : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public string Code { get; } = code;

    public string NativeName { get; } = nativeName;

    /// <summary>"ES", "EN": for places with no room for the full name.</summary>
    public string ShortName => Code.ToUpperInvariant();
}
