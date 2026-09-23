using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StopWastingTime.App.Localization;
using StopWastingTime.Core.Blocking;
using StopWastingTime.Core.Data;
using StopWastingTime.Core.Models;
using StopWastingTime.Core.Sessions;
using StopWastingTime.Core.Stats;

namespace StopWastingTime.App.ViewModels;

/// <summary>The focus screen: pick a duration, start, and watch the countdown.</summary>
public partial class FocusViewModel : ObservableObject
{
    private readonly FocusSessionService _sessions;
    private readonly StatsService _stats;
    private readonly BlockRuleRepository _rules;
    private readonly HostsFileBlocker _hostsBlocker;
    private readonly Localizer _localizer;

    [ObservableProperty]
    private int _selectedMinutes = DurationPreset.DefaultMinutes;

    [ObservableProperty]
    private bool _isStrict;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _remainingText = "25:00";

    [ObservableProperty]
    private double _progressFraction;

    [ObservableProperty]
    private int _blockedDistractions;

    // The side panel: what today looks like so far, and what a session would take away.
    [ObservableProperty]
    private string _todaySessionsText = string.Empty;

    [ObservableProperty]
    private string _todayFocusedText = string.Empty;

    [ObservableProperty]
    private string _streakText = string.Empty;

    [ObservableProperty]
    private string _blockedAppsText = string.Empty;

    [ObservableProperty]
    private string _blockedSitesText = string.Empty;

    public FocusViewModel(
        FocusSessionService sessions,
        StatsService stats,
        BlockRuleRepository rules,
        HostsFileBlocker hostsBlocker,
        Localizer localizer)
    {
        _sessions = sessions;
        _stats = stats;
        _rules = rules;
        _hostsBlocker = hostsBlocker;
        _localizer = localizer;

        Warning = new LocalizedText(localizer);
        LastResult = new LocalizedText(localizer);

        Presets =
        [
            new DurationPreset(25, localizer),
            new DurationPreset(45, localizer),
            new DurationPreset(60, localizer),
            new DurationPreset(90, localizer)
        ];

        RenderToday(completed: 0, focused: TimeSpan.Zero, streak: 0, apps: 0, sites: 0);

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

        _sessions.SessionEnded += async (_, session) =>
        {
            if (!IsRunning)
            {
                return;
            }

            IsRunning = false;
            ProgressFraction = 0;
            RemainingText = FormatMinutes(SelectedMinutes);

            var planned = session.PlannedMinutes;
            var elapsed = (int)session.Elapsed.TotalMinutes;

            if (session.Status == SessionStatus.Completed)
            {
                LastResult.Set(text => text.Plural("Focus_Completed", planned));
            }
            else
            {
                LastResult.Set(text => text.Plural("Focus_Aborted", elapsed));
            }

            await RefreshTodayAsync();
        };
    }

    /// <summary>The usual pomodoro-ish durations, one click away.</summary>
    public IReadOnlyList<DurationPreset> Presets { get; }

    /// <summary>Something that needs attention, such as a duration out of range.</summary>
    public LocalizedText Warning { get; }

    /// <summary>How the last session went.</summary>
    public LocalizedText LastResult { get; }

    public bool CanEditSettings => !IsRunning;

    /// <summary>A strict session cannot be cancelled, so the button has to say so.</summary>
    public bool CanCancel => IsRunning && !IsStrict;

    /// <summary>Shown in place of the cancel button, to make the commitment visible.</summary>
    public bool IsStrictRunning => IsRunning && IsStrict;

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

        Warning.Clear();
        LastResult.Clear();

        await _sessions.StartAsync(SelectedMinutes, IsStrict);

        IsRunning = true;
        BlockedDistractions = 0;

        // The apps are blocked either way; only the site blocking needs administrator rights.
        if (_hostsBlocker.LastProblem is { } problem)
        {
            Warning.Set(text => text[HostsProblemKey(problem)]);
        }
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        if (!CanCancel)
        {
            return;
        }

        await _sessions.AbortAsync();
    }

    public async Task RefreshTodayAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var totals = await _stats.GetTotalsAsync(StatsPeriod.Day, today);

        var rules = await _rules.GetEnabledAsync();

        RenderToday(
            totals.CompletedSessions,
            totals.FocusedTime,
            totals.CurrentStreak,
            apps: rules.Count(rule => rule.Kind == BlockKind.Process),
            sites: rules.Count(rule => rule.Kind == BlockKind.Website));
    }

    /// <summary>Everything this screen had written out goes again, in the language just picked.</summary>
    public async Task ApplyLanguageAsync()
    {
        foreach (var preset in Presets)
        {
            preset.RefreshLabel();
        }

        await RefreshTodayAsync();
    }

    /// <summary>The words that go with a hosts file failure.</summary>
    private static string HostsProblemKey(HostsFileProblem problem) => problem switch
    {
        HostsFileProblem.AccessDenied => "Hosts_AccessDenied",
        _ => "Hosts_WriteFailed"
    };

    private void RenderToday(int completed, TimeSpan focused, int streak, int apps, int sites)
    {
        TodaySessionsText = completed.ToString(_localizer.Culture);
        TodayFocusedText = _localizer.Duration(focused);
        StreakText = streak.ToString(_localizer.Culture);
        BlockedAppsText = _localizer.Plural("Count_Apps", apps);
        BlockedSitesText = _localizer.Plural("Count_Sites", sites);
    }

    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditSettings));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(IsStrictRunning));
    }

    partial void OnIsStrictChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(IsStrictRunning));
    }

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

/// <summary>A one click session length.</summary>
public partial class DurationPreset : ObservableObject
{
    /// <summary>The preset that starts out chosen.</summary>
    public const int DefaultMinutes = 25;

    private readonly Localizer _localizer;

    [ObservableProperty]
    private bool _isSelected;

    public DurationPreset(int minutes, Localizer localizer)
    {
        _localizer = localizer;
        Minutes = minutes;
        IsSelected = minutes == DefaultMinutes;
    }

    public int Minutes { get; }

    public string Label => _localizer.Format("Duration_Minutes", Minutes);

    public void RefreshLabel() => OnPropertyChanged(nameof(Label));
}
