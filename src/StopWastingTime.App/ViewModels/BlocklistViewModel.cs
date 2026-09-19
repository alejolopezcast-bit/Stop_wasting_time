using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StopWastingTime.Core.Blocking;
using StopWastingTime.Core.Data;
using StopWastingTime.Core.Models;

namespace StopWastingTime.App.ViewModels;

/// <summary>The blocklist screen: which apps get closed and which sites stop resolving.</summary>
public partial class BlocklistViewModel(BlockRuleRepository rules, IProcessScanner scanner) : ObservableObject
{
    [ObservableProperty]
    private string _newAppName = string.Empty;

    [ObservableProperty]
    private string _newSiteDomain = string.Empty;

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private RunningProcessViewModel? _selectedProcess;

    public ObservableCollection<BlockRuleViewModel> Apps { get; } = [];

    public ObservableCollection<BlockRuleViewModel> Sites { get; } = [];

    /// <summary>What is running right now, so an app can be added without typing its executable name.</summary>
    public ObservableCollection<RunningProcessViewModel> DetectedProcesses { get; } = [];

    public async Task LoadAsync()
    {
        var all = await rules.GetAllAsync();

        Apps.Clear();
        Sites.Clear();

        foreach (var rule in all)
        {
            var item = new BlockRuleViewModel(rule, rules);

            if (rule.Kind == BlockKind.Process)
            {
                Apps.Add(item);
            }
            else
            {
                Sites.Add(item);
            }
        }

        RefreshProcesses();
    }

    /// <summary>
    /// Lists the programs running right now, minus the ones that are protected and the ones already on
    /// the list, so the picker only offers things worth blocking.
    /// </summary>
    [RelayCommand]
    public void RefreshProcesses()
    {
        var alreadyListed = Apps
            .Select(app => ProcessNames.Normalize(app.Value))
            .ToHashSet(StringComparer.Ordinal);

        var candidates = scanner.Snapshot()
            .Select(process => ProcessNames.Normalize(process.Name))
            .Where(name => name.Length > 0)
            .Where(name => !ProcessNames.Protected.Contains(name))
            .Where(name => !alreadyListed.Contains(name))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => new RunningProcessViewModel(name))
            .ToList();

        DetectedProcesses.Clear();
        foreach (var candidate in candidates)
        {
            DetectedProcesses.Add(candidate);
        }
    }

    [RelayCommand]
    private async Task AddSelectedProcessAsync()
    {
        if (SelectedProcess is null)
        {
            return;
        }

        await AddAppCoreAsync(SelectedProcess.Name);
        SelectedProcess = null;
    }

    [RelayCommand]
    private async Task AddAppAsync()
    {
        await AddAppCoreAsync(NewAppName);
        NewAppName = string.Empty;
    }

    [RelayCommand]
    private async Task AddSiteAsync()
    {
        Error = null;

        if (!DomainNormalizer.TryNormalize(NewSiteDomain, out var domain))
        {
            Error = "Escribí un dominio, por ejemplo instagram.com";
            return;
        }

        if (Sites.Any(site => string.Equals(site.Value, domain, StringComparison.Ordinal)))
        {
            Error = $"{domain} ya está en la lista.";
            return;
        }

        var stored = await rules.AddAsync(new BlockRule
        {
            Kind = BlockKind.Website,
            Value = domain,
            DisplayName = domain,
            IsEnabled = true
        });

        Sites.Add(new BlockRuleViewModel(stored, rules));
        NewSiteDomain = string.Empty;
    }

    [RelayCommand]
    private async Task RemoveAsync(BlockRuleViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        await rules.DeleteAsync(item.Id);

        Apps.Remove(item);
        Sites.Remove(item);
        RefreshProcesses();
    }

    private async Task AddAppCoreAsync(string input)
    {
        Error = null;

        var value = ProcessNames.Normalize(input);

        if (value.Length == 0)
        {
            Error = "Escribí el nombre del programa, por ejemplo steam";
            return;
        }

        if (ProcessNames.IsProtected(value))
        {
            Error = $"{value} es parte de Windows o de esta app, así que no se puede bloquear.";
            return;
        }

        if (Apps.Any(app => string.Equals(app.Value, value, StringComparison.Ordinal)))
        {
            Error = $"{value} ya está en la lista.";
            return;
        }

        var stored = await rules.AddAsync(new BlockRule
        {
            Kind = BlockKind.Process,
            Value = value,
            DisplayName = Capitalize(value),
            IsEnabled = true
        });

        Apps.Add(new BlockRuleViewModel(stored, rules));
        RefreshProcesses();
    }

    private static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
}

/// <summary>One row of the blocklist. Flipping the switch saves straight away.</summary>
public partial class BlockRuleViewModel(BlockRule rule, BlockRuleRepository repository) : ObservableObject
{
    [ObservableProperty]
    private bool _isEnabled = rule.IsEnabled;

    public long Id => rule.Id;

    public string Value => rule.Value;

    public string DisplayName => rule.DisplayName;

    public string Subtitle => rule.Kind == BlockKind.Process
        ? $"{rule.Value}.exe"
        : rule.Value;

    partial void OnIsEnabledChanged(bool value)
    {
        rule.IsEnabled = value;

        // Fire and forget: the list stays responsive, and a failed write is visible next time the
        // screen loads rather than blocking a toggle.
        _ = repository.SetEnabledAsync(rule.Id, value);
    }
}

/// <summary>A program currently running, offered by the picker.</summary>
public sealed record RunningProcessViewModel(string Name)
{
    public string DisplayName => $"{Name}.exe";
}
