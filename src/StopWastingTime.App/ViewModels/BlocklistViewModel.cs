using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StopWastingTime.App.Infrastructure;
using StopWastingTime.Core.Blocking;
using StopWastingTime.Core.Data;
using StopWastingTime.Core.Models;

namespace StopWastingTime.App.ViewModels;

/// <summary>The blocklist screen: which apps get closed and which sites stop resolving.</summary>
public partial class BlocklistViewModel(
    BlockRuleRepository rules,
    IProcessScanner scanner,
    AppIconProvider icons) : ObservableObject
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

    public string AppsCountText => Describe(Apps, "app", "apps");

    public string SitesCountText => Describe(Sites, "sitio", "sitios");

    public bool HasApps => Apps.Count > 0;

    public bool HasSites => Sites.Count > 0;

    public async Task LoadAsync()
    {
        var all = await rules.GetAllAsync();

        // The icon of an app can only be read while it is running, since that is the only time its path
        // is known. Anything not running falls back to its initial.
        var paths = ExecutablePathsByName();

        Apps.Clear();
        Sites.Clear();

        foreach (var rule in all)
        {
            var item = new BlockRuleViewModel(rule, rules);

            if (rule.Kind == BlockKind.Process)
            {
                // The remembered path first; if the app happens to be running and we never wrote one
                // down, this is the moment to learn it.
                var path = rule.IconPath;

                if (string.IsNullOrEmpty(path) && paths.TryGetValue(rule.Value, out var discovered) && discovered is not null)
                {
                    path = discovered;
                    await rules.SetIconPathAsync(rule.Id, discovered);
                }

                item.Icon = icons.GetIcon(path);
                Apps.Add(item);
            }
            else
            {
                Sites.Add(item);
            }
        }

        RefreshProcesses();
        RaiseCounts();
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
            .Select(process => new
            {
                Name = ProcessNames.Normalize(process.Name),
                Process = process
            })
            .Where(candidate => candidate.Name.Length > 0)
            .Where(candidate => !ProcessNames.Protected.Contains(candidate.Name))
            .Where(candidate => !alreadyListed.Contains(candidate.Name))
            .GroupBy(candidate => candidate.Name, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var path = scanner.TryGetExecutablePath(group.First().Process);
                return new RunningProcessViewModel(group.Key)
                {
                    ExecutablePath = path,
                    Icon = icons.GetIcon(path)
                };
            })
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

        await AddAppCoreAsync(SelectedProcess.Name, SelectedProcess.Icon, SelectedProcess.ExecutablePath);
        SelectedProcess = null;
    }

    [RelayCommand]
    private async Task AddAppAsync()
    {
        // Typed by hand: if that program happens to be running, its icon comes along.
        var running = DetectedProcesses.FirstOrDefault(process =>
            string.Equals(process.Name, ProcessNames.Normalize(NewAppName), StringComparison.Ordinal));

        await AddAppCoreAsync(NewAppName, running?.Icon, running?.ExecutablePath);
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
        RaiseCounts();
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
        RaiseCounts();
    }

    private async Task AddAppCoreAsync(string input, ImageSource? icon, string? iconPath)
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
            IsEnabled = true,
            IconPath = iconPath
        });

        Apps.Add(new BlockRuleViewModel(stored, rules) { Icon = icon });
        RefreshProcesses();
        RaiseCounts();
    }

    private void RaiseCounts()
    {
        OnPropertyChanged(nameof(AppsCountText));
        OnPropertyChanged(nameof(SitesCountText));
        OnPropertyChanged(nameof(HasApps));
        OnPropertyChanged(nameof(HasSites));
    }

    private Dictionary<string, string?> ExecutablePathsByName()
    {
        var paths = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var process in scanner.Snapshot())
        {
            var name = ProcessNames.Normalize(process.Name);

            if (name.Length == 0 || paths.ContainsKey(name))
            {
                continue;
            }

            paths[name] = scanner.TryGetExecutablePath(process);
        }

        return paths;
    }

    private static string Describe(ICollection<BlockRuleViewModel> items, string singular, string plural)
    {
        var enabled = items.Count(item => item.IsEnabled);
        return $"{enabled} de {items.Count} {(items.Count == 1 ? singular : plural)} activ{(items.Count == 1 ? "a" : "as")}";
    }

    private static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
}

/// <summary>One row of the blocklist. Flipping the switch saves straight away.</summary>
public partial class BlockRuleViewModel(BlockRule rule, BlockRuleRepository repository) : ObservableObject
{
    [ObservableProperty]
    private bool _isEnabled = rule.IsEnabled;

    /// <summary>The real icon of the program, when it could be read.</summary>
    [ObservableProperty]
    private ImageSource? _icon;

    public long Id => rule.Id;

    public string Value => rule.Value;

    public string DisplayName => rule.DisplayName;

    public string Subtitle => rule.Kind == BlockKind.Process
        ? $"{rule.Value}.exe"
        : rule.Value;

    /// <summary>Stands in for the icon when there is none: the first letter, as a monogram.</summary>
    public string Initial => rule.DisplayName.Length > 0
        ? rule.DisplayName[..1].ToUpperInvariant()
        : "?";

    public bool IsWebsite => rule.Kind == BlockKind.Website;

    partial void OnIsEnabledChanged(bool value)
    {
        rule.IsEnabled = value;

        // Fire and forget: the list stays responsive, and a failed write is visible next time the
        // screen loads rather than blocking a toggle.
        _ = repository.SetEnabledAsync(rule.Id, value);
    }
}

/// <summary>A program currently running, offered by the picker.</summary>
public sealed class RunningProcessViewModel(string name)
{
    public string Name { get; } = name;

    /// <summary>Where it was found, so the rule can remember it.</summary>
    public string? ExecutablePath { get; init; }

    public ImageSource? Icon { get; init; }

    public string DisplayName => $"{Name}.exe";

    public string Initial => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";
}
