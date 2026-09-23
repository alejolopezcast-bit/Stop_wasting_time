using CommunityToolkit.Mvvm.ComponentModel;

namespace StopWastingTime.App.Localization;

/// <summary>
/// A message a screen shows now and then, such as a warning or the result of the last session. It keeps
/// the way to write the sentence rather than the sentence itself, so switching languages rewrites the
/// message instead of leaving it behind in the old one.
/// </summary>
public sealed class LocalizedText : ObservableObject
{
    private readonly Localizer _localizer;
    private Func<Localizer, string>? _compose;

    public LocalizedText(Localizer localizer)
    {
        _localizer = localizer;

        // Owned by view models that live as long as the app, so the subscription never needs undoing.
        localizer.LanguageChanged += (_, _) => OnPropertyChanged(nameof(Text));
    }

    /// <summary>Null when there is nothing to say, which the views treat as empty.</summary>
    public string? Text => _compose?.Invoke(_localizer);

    public void Set(Func<Localizer, string> compose)
    {
        _compose = compose;
        OnPropertyChanged(nameof(Text));
    }

    public void Clear()
    {
        if (_compose is null)
        {
            return;
        }

        _compose = null;
        OnPropertyChanged(nameof(Text));
    }
}
