using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StopWastingTime.Core.Sessions;

namespace StopWastingTime.App.ViewModels;

/// <summary>The main window: navigation between the three screens, and the state the window itself needs.</summary>
public partial class ShellViewModel : ObservableObject
{
    private readonly FocusSessionService _sessions;

    [ObservableProperty]
    private ObservableObject _currentPage;

    [ObservableProperty]
    private string _trayTooltip = "Stop Wasting Time";

    /// <summary>The countdown for the badge in the rail, whichever screen started the session.</summary>
    [ObservableProperty]
    private string _remainingText = "00:00";

    public ShellViewModel(
        FocusViewModel focus,
        UltraFocusViewModel ultra,
        BlocklistViewModel blocklist,
        StatsViewModel stats,
        FocusSessionService sessions)
    {
        Focus = focus;
        Ultra = ultra;
        Blocklist = blocklist;
        Stats = stats;
        _sessions = sessions;
        _currentPage = focus;

        _sessions.SessionStarted += (_, _) => RaiseSessionState();

        _sessions.SessionEnded += async (_, _) =>
        {
            RaiseSessionState();
            TrayTooltip = "Stop Wasting Time";
            await Stats.RefreshAsync();
        };

        _sessions.Progressed += (_, progress) =>
        {
            RemainingText = progress.RemainingText;
            TrayTooltip = $"Quedan {progress.RemainingText} de concentración";
        };
    }

    public FocusViewModel Focus { get; }

    public UltraFocusViewModel Ultra { get; }

    public BlocklistViewModel Blocklist { get; }

    public StatsViewModel Stats { get; }

    public bool IsSessionRunning => _sessions.IsRunning;

    /// <summary>While this is true the app refuses to close: that is the point of a strict session.</summary>
    public bool IsStrictSessionRunning => _sessions.Current?.IsStrict == true;

    public bool IsFocusSelected => CurrentPage == Focus;

    public bool IsUltraSelected => CurrentPage == Ultra;

    public bool IsBlocklistSelected => CurrentPage == Blocklist;

    public bool IsStatsSelected => CurrentPage == Stats;

    [RelayCommand]
    private void ShowFocus() => CurrentPage = Focus;

    [RelayCommand]
    private async Task ShowUltraAsync()
    {
        // The summary has to be current: the blocklist may have changed since the app started.
        await Ultra.RefreshBlocklistSummaryAsync();
        CurrentPage = Ultra;
    }

    [RelayCommand]
    private async Task ShowBlocklistAsync()
    {
        await Blocklist.LoadAsync();
        CurrentPage = Blocklist;
    }

    [RelayCommand]
    private async Task ShowStatsAsync()
    {
        await Stats.RefreshAsync();
        CurrentPage = Stats;
    }

    /// <summary>Loads everything the window shows on open.</summary>
    public async Task InitializeAsync()
    {
        await Focus.RefreshTodayAsync();
        await Blocklist.LoadAsync();
        await Ultra.RefreshBlocklistSummaryAsync();
        await Stats.RefreshAsync();
    }

    private void RaiseSessionState()
    {
        OnPropertyChanged(nameof(IsSessionRunning));
        OnPropertyChanged(nameof(IsStrictSessionRunning));
    }

    partial void OnCurrentPageChanged(ObservableObject value)
    {
        OnPropertyChanged(nameof(IsFocusSelected));
        OnPropertyChanged(nameof(IsUltraSelected));
        OnPropertyChanged(nameof(IsBlocklistSelected));
        OnPropertyChanged(nameof(IsStatsSelected));
    }
}
