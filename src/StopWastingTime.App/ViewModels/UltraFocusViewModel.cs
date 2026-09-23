using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using StopWastingTime.App.Infrastructure;
using StopWastingTime.App.Localization;
using StopWastingTime.Core.Data;
using StopWastingTime.Core.Models;
using StopWastingTime.Core.Sessions;

namespace StopWastingTime.App.ViewModels;

/// <summary>
/// Ultra focus: you set the time and that is that. No cancel button, the app cannot be closed, and the
/// whole blocklist is enforced, switches included. The only way out is the clock, which is the entire
/// point of the mode, so the commitment is spelled out before it starts.
/// </summary>
public partial class UltraFocusViewModel : ObservableObject
{
    /// <summary>The session the ultra screen starts is labelled, so the history can tell them apart.</summary>
    public const string SessionLabel = "Ultra";

    private readonly FocusSessionService _sessions;
    private readonly BlockRuleRepository _rules;
    private readonly IDialogService _dialogs;
    private readonly ILogger<UltraFocusViewModel> _logger;
    private readonly Localizer _localizer;

    [ObservableProperty]
    private int _selectedMinutes = 60;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _remainingText = "60:00";

    [ObservableProperty]
    private double _progressFraction;

    [ObservableProperty]
    private int _blockedDistractions;

    [ObservableProperty]
    private string _blocklistSummary;

    public UltraFocusViewModel(
        FocusSessionService sessions,
        BlockRuleRepository rules,
        IDialogService dialogs,
        Localizer localizer,
        ILogger<UltraFocusViewModel> logger)
    {
        _sessions = sessions;
        _rules = rules;
        _dialogs = dialogs;
        _localizer = localizer;
        _logger = logger;

        Warning = new LocalizedText(localizer);
        _blocklistSummary = localizer["Ultra_Summary_Default"];

        Presets =
        [
            new DurationPreset(30, localizer),
            new DurationPreset(60, localizer),
            new DurationPreset(90, localizer),
            new DurationPreset(120, localizer)
        ];

        _sessions.Progressed += (_, progress) =>
        {
            if (!IsRunning)
            {
                return;
            }

            RemainingText = progress.RemainingText;
            ProgressFraction = progress.Fraction;
            BlockedDistractions = progress.BlockedDistractions;
        };

        _sessions.SessionEnded += (_, _) =>
        {
            if (!IsRunning)
            {
                return;
            }

            IsRunning = false;
            ProgressFraction = 0;
            RemainingText = FormatMinutes(SelectedMinutes);
        };

        // The presets default to the ordinary screen's choice, which is not one of these.
        foreach (var preset in Presets)
        {
            preset.IsSelected = preset.Minutes == SelectedMinutes;
        }
    }

    /// <summary>Longer than the ordinary presets: ultra is for a morning, not for a pomodoro.</summary>
    public IReadOnlyList<DurationPreset> Presets { get; }

    /// <summary>Something that needs attention, such as a duration out of range.</summary>
    public LocalizedText Warning { get; }

    public bool CanEditSettings => !IsRunning;

    [RelayCommand]
    private void SelectPreset(DurationPreset? preset)
    {
        if (IsRunning || preset is null)
        {
            return;
        }

        SelectedMinutes = preset.Minutes;
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        _logger.LogInformation("Ultra session requested: {Minutes} minutes.", SelectedMinutes);

        if (IsRunning)
        {
            return;
        }

        if (SelectedMinutes is < FocusSessionService.MinimumMinutes or > FocusSessionService.MaximumMinutes)
        {
            Warning.Set(text => text.Format(
                "Validation_MinutesRange", FocusSessionService.MinimumMinutes, FocusSessionService.MaximumMinutes));
            return;
        }

        // The last chance to change your mind, because after this there is none.
        var confirmed = _dialogs.Confirm(
            _localizer["Ultra_Title"],
            string.Join(
                Environment.NewLine + Environment.NewLine,
                _localizer.Plural("Ultra_Confirm_Duration", SelectedMinutes),
                _localizer["Ultra_Confirm_Terms"],
                _localizer["Ultra_Confirm_Question"]));

        if (!confirmed)
        {
            _logger.LogInformation("Ultra session cancelled at the confirmation.");
            return;
        }

        Warning.Clear();

        await _sessions.StartAsync(SelectedMinutes, isStrict: true, SessionLabel, blockEverything: true);

        IsRunning = true;
        BlockedDistractions = 0;
    }

    /// <summary>Counts what an ultra session would take away, so the screen can say it out loud.</summary>
    public async Task RefreshBlocklistSummaryAsync()
    {
        var all = await _rules.GetAllAsync();

        var apps = _localizer.Plural("Count_Apps", all.Count(rule => rule.Kind == BlockKind.Process));
        var sites = _localizer.Plural("Count_Sites", all.Count(rule => rule.Kind == BlockKind.Website));
        var disabled = all.Count(rule => !rule.IsEnabled);

        BlocklistSummary = disabled switch
        {
            0 => _localizer.Format("Ultra_Summary_All", apps, sites),
            1 => _localizer.Format("Ultra_Summary_OneDisabled", apps, sites),
            _ => _localizer.Format("Ultra_Summary_ManyDisabled", apps, sites, disabled)
        };
    }

    /// <summary>Everything this screen had written out goes again, in the language just picked.</summary>
    public async Task ApplyLanguageAsync()
    {
        foreach (var preset in Presets)
        {
            preset.RefreshLabel();
        }

        await RefreshBlocklistSummaryAsync();
    }

    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(CanEditSettings));

    partial void OnSelectedMinutesChanged(int value)
    {
        if (!IsRunning)
        {
            RemainingText = FormatMinutes(value);
        }

        foreach (var preset in Presets)
        {
            preset.IsSelected = preset.Minutes == value;
        }
    }

    private static string FormatMinutes(int minutes) => minutes >= 60
        ? $"{minutes / 60}:{minutes % 60:00}:00"
        : $"{minutes:00}:00";
}
