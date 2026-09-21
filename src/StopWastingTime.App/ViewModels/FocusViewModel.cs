using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    [ObservableProperty]
    private string? _warning;

    [ObservableProperty]
    private string? _lastResult;

    // The side panel: what today looks like so far, and what a session would take away.
    [ObservableProperty]
    private string _todaySessionsText = "0";

    [ObservableProperty]
    private string _todayFocusedText = "0 min";

    [ObservableProperty]
    private string _streakText = "0";

    [ObservableProperty]
    private string _blockedAppsText = "0 apps";

    [ObservableProperty]
    private string _blockedSitesText = "0 sitios";

    public FocusViewModel(
        FocusSessionService sessions,
        StatsService stats,
        BlockRuleRepository rules,
        HostsFileBlocker hostsBlocker)
    {
        _sessions = sessions;
        _stats = stats;
        _rules = rules;
        _hostsBlocker = hostsBlocker;

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
            LastResult = session.Status == SessionStatus.Completed
                ? $"Sesión completada: {session.PlannedMinutes} minutos de concentración."
                : $"Sesión abandonada a los {(int)session.Elapsed.TotalMinutes} minutos.";

            await RefreshTodayAsync();
        };
    }

    /// <summary>The usual pomodoro-ish durations, one click away.</summary>
    public IReadOnlyList<DurationPreset> Presets { get; } =
        [new DurationPreset(25), new DurationPreset(45), new DurationPreset(60), new DurationPreset(90)];

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
            Warning = $"Elegí entre {FocusSessionService.MinimumMinutes} y {FocusSessionService.MaximumMinutes} minutos.";
            return;
        }

        Warning = null;
        LastResult = null;

        await _sessions.StartAsync(SelectedMinutes, IsStrict);

        IsRunning = true;
        BlockedDistractions = 0;

        // The apps are blocked either way; only the site blocking needs administrator rights.
        Warning = _hostsBlocker.LastError;
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

        TodaySessionsText = totals.CompletedSessions.ToString(System.Globalization.CultureInfo.CurrentCulture);
        TodayFocusedText = FormatDuration(totals.FocusedTime);
        StreakText = totals.CurrentStreak.ToString(System.Globalization.CultureInfo.CurrentCulture);

        var rules = await _rules.GetEnabledAsync();
        var apps = rules.Count(rule => rule.Kind == BlockKind.Process);
        var sites = rules.Count(rule => rule.Kind == BlockKind.Website);

        BlockedAppsText = $"{apps} {(apps == 1 ? "app" : "apps")}";
        BlockedSitesText = $"{sites} {(sites == 1 ? "sitio" : "sitios")}";
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

    public static string FormatDuration(TimeSpan duration) => duration.TotalHours >= 1
        ? $"{(int)duration.TotalHours} h {duration.Minutes} min"
        : $"{(int)duration.TotalMinutes} min";
}

/// <summary>A one click session length.</summary>
public partial class DurationPreset : ObservableObject
{
    /// <summary>The preset that starts out chosen.</summary>
    public const int DefaultMinutes = 25;

    [ObservableProperty]
    private bool _isSelected;

    public DurationPreset(int minutes)
    {
        Minutes = minutes;
        IsSelected = minutes == DefaultMinutes;
    }

    public int Minutes { get; }

    public string Label => $"{Minutes} min";
}
