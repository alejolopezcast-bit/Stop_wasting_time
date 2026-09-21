using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using StopWastingTime.App.Infrastructure;
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
    private string _blocklistSummary = "Se bloquea toda la lista.";

    [ObservableProperty]
    private string? _warning;

    public UltraFocusViewModel(
        FocusSessionService sessions,
        BlockRuleRepository rules,
        IDialogService dialogs,
        ILogger<UltraFocusViewModel> logger)
    {
        _sessions = sessions;
        _rules = rules;
        _dialogs = dialogs;
        _logger = logger;

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
    public IReadOnlyList<DurationPreset> Presets { get; } =
        [new DurationPreset(30), new DurationPreset(60), new DurationPreset(90), new DurationPreset(120)];

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
            Warning = $"Elegí entre {FocusSessionService.MinimumMinutes} y {FocusSessionService.MaximumMinutes} minutos.";
            return;
        }

        // The last chance to change your mind, because after this there is none.
        var confirmed = _dialogs.Confirm(
            "Enfoque ultra",
            $"Vas a bloquear todo durante {SelectedMinutes} minutos.{Environment.NewLine}{Environment.NewLine}" +
            "No vas a poder cancelar la sesión ni cerrar la app hasta que termine, y se bloquea toda la " +
            $"lista, incluso lo que tenés desactivado.{Environment.NewLine}{Environment.NewLine}¿Seguimos?");

        if (!confirmed)
        {
            _logger.LogInformation("Ultra session cancelled at the confirmation.");
            return;
        }

        Warning = null;

        await _sessions.StartAsync(SelectedMinutes, isStrict: true, SessionLabel, blockEverything: true);

        IsRunning = true;
        BlockedDistractions = 0;
    }

    /// <summary>Counts what an ultra session would take away, so the screen can say it out loud.</summary>
    public async Task RefreshBlocklistSummaryAsync()
    {
        var all = await _rules.GetAllAsync();

        var apps = all.Count(rule => rule.Kind == BlockKind.Process);
        var sites = all.Count(rule => rule.Kind == BlockKind.Website);
        var disabled = all.Count(rule => !rule.IsEnabled);

        var summary = $"Se bloquean {apps} {(apps == 1 ? "app" : "apps")} y {sites} {(sites == 1 ? "sitio" : "sitios")}";

        BlocklistSummary = disabled switch
        {
            0 => $"{summary}: toda tu lista.",
            1 => $"{summary}: toda tu lista, incluido el que tenés desactivado.",
            _ => $"{summary}: toda tu lista, incluidos los {disabled} que tenés desactivados."
        };
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
