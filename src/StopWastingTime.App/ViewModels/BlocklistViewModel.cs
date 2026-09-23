using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StopWastingTime.App.Infrastructure;
using StopWastingTime.App.Localization;
using StopWastingTime.Core.Blocking;
using StopWastingTime.Core.Data;
using StopWastingTime.Core.Models;

namespace StopWastingTime.App.ViewModels;

/// <summary>The blocklist screen: which apps get closed and which sites stop resolving.</summary>
public partial class BlocklistViewModel(
    BlockRuleRepository rules,
    IProcessScanner scanner,
    AppIconProvider icons,
    Localizer localizer) : ObservableObject
{
    [ObservableProperty]
    private string _newAppName = string.Empty;

    [ObservableProperty]
    private string _newSiteDomain = string.Empty;

    [ObservableProperty]
    private RunningProcessViewModel? _selectedProcess;

    /// <summary>Why the last thing typed could not be added.</summary>
    public LocalizedText Error { get; } = new(localizer);

    public ObservableCollection<BlockRuleViewModel> Apps { get; } = [];

    public ObservableCollection<BlockRuleViewModel> Sites { get; } = [];

    /// <summary>What is running right now, so an app can be added without typing its executable name.</summary>
    public ObservableCollection<RunningProcessViewModel> DetectedProcesses { get; } = [];

    public string AppsCountText => Describe(Apps, "Blocklist_AppsCount");

    public string SitesCountText => Describe(Sites, "Blocklist_SitesCount");

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
            var item = new BlockRuleViewModel(rule, rules, localizer);

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
        Error.Clear();

        if (!DomainNormalizer.TryNormalize(NewSiteDomain, out var domain))
        {
            Error.Set(text => text["Blocklist_Error_DomainRequired"]);
            return;
        }

        if (Sites.Any(site => string.Equals(site.Value, domain, StringComparison.Ordinal)))
        {
            Error.Set(text => text.Format("Blocklist_Error_AlreadyListed", domain));
            return;
        }

        var stored = await rules.AddAsync(new BlockRule
        {
            Kind = BlockKind.Website,
            Value = domain,
            DisplayName = domain,
            IsEnabled = true
        });

        Sites.Add(new BlockRuleViewModel(stored, rules, localizer));
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
        Error.Clear();

        var value = ProcessNames.Normalize(input);

        if (value.Length == 0)
        {
            Error.Set(text => text["Blocklist_Error_NameRequired"]);
            return;
        }

        if (ProcessNames.IsProtected(value))
        {
            Error.Set(text => text.Format("Blocklist_Error_Protected", value));
            return;
        }

        if (Apps.Any(app => string.Equals(app.Value, value, StringComparison.Ordinal)))
        {
            Error.Set(text => text.Format("Blocklist_Error_AlreadyListed", value));
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

        Apps.Add(new BlockRuleViewModel(stored, rules, localizer) { Icon = icon });
        RefreshProcesses();
        RaiseCounts();
    }

    /// <summary>The counts and the rows' spoken names are sentences; they go again in the new language.</summary>
    public void ApplyLanguage()
    {
        RaiseCounts();

        foreach (var row in Apps.Concat(Sites))
        {
            row.RefreshTexts();
        }
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

    /// <summary>"2 of 3 apps switched on": the plural follows the total, not the ones switched on.</summary>
    private string Describe(ICollection<BlockRuleViewModel> items, string key) =>
        localizer.Plural(key, items.Count, items.Count(item => item.IsEnabled));

    private static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
}

/// <summary>One row of the blocklist. Flipping the switch saves straight away.</summary>
public partial class BlockRuleViewModel(BlockRule rule, BlockRuleRepository repository, Localizer localizer)
    : ObservableObject
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

    /// <summary>What a screen reader says for the delete button, which is otherwise only an icon.</summary>
    public string RemoveName => localizer.Format("Blocklist_Remove_Name", rule.DisplayName);

    public void RefreshTexts() => OnPropertyChanged(nameof(RemoveName));

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
