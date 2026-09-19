using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StopWastingTime.Core.Data;
using StopWastingTime.Core.Models;
using StopWastingTime.Core.Stats;

namespace StopWastingTime.App.ViewModels;

/// <summary>
/// The statistics screen: how many times you actually concentrated, per day, week, month and year.
/// The chart geometry is worked out here rather than in XAML, so the views stay declarative and the
/// numbers stay testable.
/// </summary>
public partial class StatsViewModel(StatsService stats, SessionRepository sessions, BlockHitRepository hits)
    : ObservableObject
{
    /// <summary>Tallest a bar can get, in device independent pixels.</summary>
    private const double MaximumBarHeight = 150;

    /// <summary>Days shown when looking at a single day, so one bar never sits alone.</summary>
    private const int DayViewWindow = 7;

    [ObservableProperty]
    private StatsPeriod _period = StatsPeriod.Week;

    [ObservableProperty]
    private string _rangeText = string.Empty;

    [ObservableProperty]
    private string _completedText = "0";

    [ObservableProperty]
    private string _focusedText = "0 min";

    [ObservableProperty]
    private string _averageText = "0 min";

    [ObservableProperty]
    private string _streakText = "0";

    [ObservableProperty]
    private string _blockedText = "0";

    [ObservableProperty]
    private string _chartTitle = string.Empty;

    [ObservableProperty]
    private bool _isEmpty = true;

    public ObservableCollection<ChartBar> Bars { get; } = [];

    public ObservableCollection<HeatmapCell> Heatmap { get; } = [];

    public ObservableCollection<SessionRow> RecentSessions { get; } = [];

    public ObservableCollection<DistractionRow> TopDistractions { get; } = [];

    /// <summary>The period buttons, each knowing whether it is the one being shown.</summary>
    public IReadOnlyList<PeriodChip> Periods { get; } =
    [
        new PeriodChip(StatsPeriod.Day, "Día"),
        new PeriodChip(StatsPeriod.Week, "Semana"),
        new PeriodChip(StatsPeriod.Month, "Mes"),
        new PeriodChip(StatsPeriod.Year, "Año")
    ];

    [RelayCommand]
    private async Task SelectPeriodAsync(PeriodChip? chip)
    {
        if (chip is null)
        {
            return;
        }

        Period = chip.Period;
        await RefreshAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var totals = await stats.GetTotalsAsync(Period, today);

        RangeText = DescribeRange(totals.From, totals.To);
        CompletedText = totals.CompletedSessions.ToString(CultureInfo.CurrentCulture);
        FocusedText = FocusViewModel.FormatDuration(totals.FocusedTime);
        AverageText = FocusViewModel.FormatDuration(totals.AverageSession);
        StreakText = totals.CurrentStreak.ToString(CultureInfo.CurrentCulture);
        BlockedText = totals.BlockHits.ToString(CultureInfo.CurrentCulture);
        IsEmpty = totals.CompletedSessions == 0 && totals.AbortedSessions == 0;

        await BuildChartAsync(today);
        await BuildHeatmapAsync(today);
        await BuildRecentAsync();
        await BuildTopDistractionsAsync(totals.From, totals.To);
    }

    private async Task BuildChartAsync(DateOnly today)
    {
        Bars.Clear();

        if (Period == StatsPeriod.Year)
        {
            ChartTitle = "Sesiones completadas por mes";
            await BuildMonthlyBarsAsync(today);
            return;
        }

        var (from, to) = Period == StatsPeriod.Day
            ? (today.AddDays(-(DayViewWindow - 1)), today)
            : StatsService.GetRange(Period, today);

        ChartTitle = Period switch
        {
            StatsPeriod.Day => "Sesiones completadas en los últimos 7 días",
            StatsPeriod.Week => "Sesiones completadas por día de la semana",
            _ => "Sesiones completadas por día del mes"
        };

        var summaries = await stats.GetDailySummariesAsync(from, to);
        var peak = Math.Max(1, summaries.Max(day => day.CompletedSessions));

        foreach (var day in summaries)
        {
            Bars.Add(new ChartBar(
                Label: Period == StatsPeriod.Month
                    ? day.Date.Day.ToString(CultureInfo.CurrentCulture)
                    : CultureInfo.CurrentCulture.DateTimeFormat.GetShortestDayName(day.Date.DayOfWeek),
                Value: day.CompletedSessions,
                Height: day.CompletedSessions / (double)peak * MaximumBarHeight,
                IsToday: day.Date == today,
                Tooltip: $"{day.Date:dd/MM}: {day.CompletedSessions} completadas, " +
                         $"{FocusViewModel.FormatDuration(day.FocusedTime)}"));
        }
    }

    private async Task BuildMonthlyBarsAsync(DateOnly today)
    {
        var summaries = await stats.GetDailySummariesAsync(new DateOnly(today.Year, 1, 1), new DateOnly(today.Year, 12, 31));

        var byMonth = summaries
            .GroupBy(day => day.Date.Month)
            .ToDictionary(group => group.Key, group => group.Sum(day => day.CompletedSessions));

        var peak = Math.Max(1, byMonth.Count == 0 ? 1 : byMonth.Values.Max());

        for (var month = 1; month <= 12; month++)
        {
            byMonth.TryGetValue(month, out var completed);

            Bars.Add(new ChartBar(
                Label: CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedMonthName(month),
                Value: completed,
                Height: completed / (double)peak * MaximumBarHeight,
                IsToday: month == today.Month,
                Tooltip: $"{CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(month)}: {completed} completadas"));
        }
    }

    /// <summary>
    /// The last 52 weeks as a grid of days, the way a contribution calendar reads: one glance shows
    /// whether the habit is alive. Cells are emitted row by row because that is how a UniformGrid fills
    /// itself, while the calendar reads down the columns, one week per column.
    /// </summary>
    private async Task BuildHeatmapAsync(DateOnly today)
    {
        Heatmap.Clear();

        var end = today;
        var start = StatsService.StartOfWeek(end.AddDays(-364));
        var summaries = await stats.GetDailySummariesAsync(start, end);
        var byDate = summaries.ToDictionary(day => day.Date);

        var weeks = (int)Math.Ceiling((end.DayNumber - start.DayNumber + 1) / 7.0);

        for (var weekday = 0; weekday < 7; weekday++)
        {
            for (var week = 0; week < weeks; week++)
            {
                var date = start.AddDays((week * 7) + weekday);

                if (date > end)
                {
                    // The tail of the current week has not happened yet: an invisible spacer keeps the
                    // grid aligned.
                    Heatmap.Add(new HeatmapCell(date, 0, string.Empty, IsFiller: true));
                    continue;
                }

                byDate.TryGetValue(date, out var summary);
                var minutes = summary?.FocusedTime.TotalMinutes ?? 0;

                Heatmap.Add(new HeatmapCell(
                    Date: date,
                    Opacity: minutes switch
                    {
                        <= 0 => 0.07,
                        < 25 => 0.3,
                        < 60 => 0.55,
                        < 120 => 0.8,
                        _ => 1.0
                    },
                    Tooltip: $"{date:dd/MM/yyyy}: {FocusViewModel.FormatDuration(TimeSpan.FromMinutes(minutes))}"));
            }
        }
    }

    private async Task BuildRecentAsync()
    {
        RecentSessions.Clear();

        foreach (var session in await sessions.GetRecentAsync(12))
        {
            if (session.Status == SessionStatus.Running)
            {
                continue;
            }

            RecentSessions.Add(new SessionRow(
                When: session.StartedUtc.ToLocalTime().ToString("ddd dd/MM HH:mm", CultureInfo.CurrentCulture),
                Duration: FocusViewModel.FormatDuration(session.Elapsed),
                Status: session.Status == SessionStatus.Completed ? "Completada" : "Abandonada",
                IsCompleted: session.Status == SessionStatus.Completed,
                IsStrict: session.IsStrict));
        }
    }

    private async Task BuildTopDistractionsAsync(DateOnly from, DateOnly to)
    {
        TopDistractions.Clear();

        foreach (var distraction in await hits.GetTopAsync(from, to, 5))
        {
            TopDistractions.Add(new DistractionRow(
                distraction.DisplayName,
                $"{distraction.Hits} {(distraction.Hits == 1 ? "vez" : "veces")}"));
        }
    }

    private static string DescribeRange(DateOnly from, DateOnly to) => from == to
        ? from.ToString("dddd d 'de' MMMM", CultureInfo.CurrentCulture)
        : $"{from:dd/MM/yyyy} — {to:dd/MM/yyyy}";

    partial void OnPeriodChanged(StatsPeriod value)
    {
        foreach (var chip in Periods)
        {
            chip.IsSelected = chip.Period == value;
        }
    }
}

/// <summary>One of the day/week/month/year buttons.</summary>
public partial class PeriodChip(StatsPeriod period, string label) : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected = period == StatsPeriod.Week;

    public StatsPeriod Period { get; } = period;

    public string Label { get; } = label;
}

/// <summary>One bar of the chart, already measured in pixels.</summary>
public sealed record ChartBar(string Label, int Value, double Height, bool IsToday, string Tooltip);

/// <summary>One day of the year heatmap. Filler cells pad the current week so the grid stays square.</summary>
public sealed record HeatmapCell(DateOnly Date, double Opacity, string Tooltip, bool IsFiller = false);

/// <summary>One row of the recent sessions list.</summary>
public sealed record SessionRow(string When, string Duration, string Status, bool IsCompleted, bool IsStrict);

/// <summary>One row of the "what tempted you" list.</summary>
public sealed record DistractionRow(string DisplayName, string HitsText);
