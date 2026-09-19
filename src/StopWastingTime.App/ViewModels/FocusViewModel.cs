using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StopWastingTime.Core.Blocking;
using StopWastingTime.Core.Sessions;
using StopWastingTime.Core.Stats;

namespace StopWastingTime.App.ViewModels;

/// <summary>The focus screen: pick a duration, start, and watch the countdown.</summary>
public partial class FocusViewModel : ObservableObject
{
    private readonly FocusSessionService _sessions;
    private readonly StatsService _stats;
    private readonly HostsFileBlocker _hostsBlocker;
    private readonly DispatcherTimer _timer;

    [ObservableProperty]
    private int _selectedMinutes = 25;

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
    private string _todaySummary = "Todavía no hay sesiones hoy.";

    [ObservableProperty]
    private string? _warning;

    [ObservableProperty]
    private string? _lastResult;

    public FocusViewModel(FocusSessionService sessions, StatsService stats, HostsFileBlocker hostsBlocker)
    {
        _sessions = sessions;
        _stats = stats;
        _hostsBlocker = hostsBlocker;

        // Built before the handlers below capture it: one tick a second drives the countdown, and the
        // service works out the real elapsed time from the clock, so a late tick cannot stretch the
        // session.
        _timer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += async (_, _) => await _sessions.TickAsync();

        _sessions.Progressed += (_, progress) =>
        {
            RemainingText = progress.RemainingText;
            ProgressFraction = progress.Fraction;
            BlockedDistractions = progress.BlockedDistractions;
        };

        _sessions.SessionEnded += async (_, session) =>
        {
            IsRunning = false;
            _timer.Stop();
            ProgressFraction = 0;
            RemainingText = FormatMinutes(SelectedMinutes);
            LastResult = session.Status == Core.Models.SessionStatus.Completed
                ? $"Sesión completada: {session.PlannedMinutes} minutos."
                : $"Sesión abandonada a los {session.Elapsed.Minutes} minutos.";

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
        _timer.Start();

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

        TodaySummary = totals.CompletedSessions == 0
            ? "Todavía no hay sesiones completadas hoy."
            : $"Hoy: {totals.CompletedSessions} {(totals.CompletedSessions == 1 ? "sesión" : "sesiones")} " +
              $"y {FormatDuration(totals.FocusedTime)} de concentración.";
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
    [ObservableProperty]
    private bool _isSelected;

    public DurationPreset(int minutes)
    {
        Minutes = minutes;
        IsSelected = minutes == DefaultMinutes;
    }

    /// <summary>The preset that starts out chosen.</summary>
    public const int DefaultMinutes = 25;

    public int Minutes { get; }

    public string Label => $"{Minutes} min";
}
