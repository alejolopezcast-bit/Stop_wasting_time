using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StopWastingTime.App.Localization;
using StopWastingTime.Core.Sessions;

namespace StopWastingTime.App.ViewModels;

/// <summary>The main window: navigation between the screens, and the state the window itself needs.</summary>
public partial class ShellViewModel : ObservableObject
{
    private readonly FocusSessionService _sessions;

    [ObservableProperty]
    private ObservableObject _currentPage;

    [ObservableProperty]
    private string _trayTooltip = "Stop Wasting Time";

    /// <summary>The countdown shown in the caption bar, whichever screen started the session.</summary>
    [ObservableProperty]
    private string _remainingText = "00:00";

    public ShellViewModel(
        FocusViewModel focus,
        UltraFocusViewModel ultra,
        BlocklistViewModel blocklist,
        StatsViewModel stats,
        FocusSessionService sessions,
        Localizer localizer)
    {
        Focus = focus;
        Ultra = ultra;
        Blocklist = blocklist;
        Stats = stats;
        _sessions = sessions;
        _currentPage = focus;

        NavItems =
        [
            new NavItem("Nav_Focus", "IconFocus", focus, localizer) { IsSelected = true },
            new NavItem("Nav_Ultra", "IconUltra", ultra, localizer),
            new NavItem("Nav_Blocklist", "IconBlock", blocklist, localizer),
            new NavItem("Nav_Stats", "IconStats", stats, localizer)
        ];

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
            TrayTooltip = localizer.Format("Tray_Tooltip", progress.RemainingText);
        };

        localizer.LanguageChanged += async (_, _) => await ApplyLanguageAsync();
    }

    public FocusViewModel Focus { get; }

    public UltraFocusViewModel Ultra { get; }

    public BlocklistViewModel Blocklist { get; }

    public StatsViewModel Stats { get; }

    /// <summary>The navigation rail, as data: one entry per screen.</summary>
    public ObservableCollection<NavItem> NavItems { get; }

    public bool IsSessionRunning => _sessions.IsRunning;

    /// <summary>While this is true the app refuses to close: that is the point of a strict session.</summary>
    public bool IsStrictSessionRunning => _sessions.Current?.IsStrict == true;

    /// <summary>An ultra session paints the caption amber rather than blue.</summary>
    public bool IsUltraSessionRunning => _sessions.Current?.Label == UltraFocusViewModel.SessionLabel;

    [RelayCommand]
    private async Task NavigateAsync(NavItem? item)
    {
        if (item is null || item.Page == CurrentPage)
        {
            return;
        }

        // Each screen refreshes as it comes into view, so it never shows a stale number.
        switch (item.Page)
        {
            case BlocklistViewModel blocklist:
                await blocklist.LoadAsync();
                break;
            case UltraFocusViewModel ultra:
                await ultra.RefreshBlocklistSummaryAsync();
                break;
            case StatsViewModel stats:
                await stats.RefreshAsync();
                break;
            case FocusViewModel focus:
                await focus.RefreshTodayAsync();
                break;
        }

        CurrentPage = item.Page;
    }

    /// <summary>Loads everything the window shows on open.</summary>
    public async Task InitializeAsync()
    {
        await Focus.RefreshTodayAsync();
        await Blocklist.LoadAsync();
        await Ultra.RefreshBlocklistSummaryAsync();
        await Stats.RefreshAsync();
    }

    /// <summary>
    /// Redraws what each screen had already written out in the old language. Text in the views follows
    /// the picker by itself; this covers the sentences the view models put together.
    /// </summary>
    private async Task ApplyLanguageAsync()
    {
        foreach (var item in NavItems)
        {
            item.RefreshLabel();
        }

        await Focus.ApplyLanguageAsync();
        await Ultra.ApplyLanguageAsync();
        Blocklist.ApplyLanguage();
        await Stats.ApplyLanguageAsync();
    }

    private void RaiseSessionState()
    {
        OnPropertyChanged(nameof(IsSessionRunning));
        OnPropertyChanged(nameof(IsStrictSessionRunning));
        OnPropertyChanged(nameof(IsUltraSessionRunning));
    }

    partial void OnCurrentPageChanged(ObservableObject value)
    {
        foreach (var item in NavItems)
        {
            item.IsSelected = item.Page == value;
        }
    }
}

/// <summary>One entry of the navigation rail.</summary>
public partial class NavItem(string labelKey, string iconKey, ObservableObject page, Localizer localizer) : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public string Label => localizer[labelKey];

    /// <summary>Name of the geometry in the icon dictionary, so the view model holds no WPF types.</summary>
    public string IconKey { get; } = iconKey;

    public ObservableObject Page { get; } = page;

    public void RefreshLabel() => OnPropertyChanged(nameof(Label));
}
