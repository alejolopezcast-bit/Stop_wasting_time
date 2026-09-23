using System.IO;
using System.Reflection;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StopWastingTime.App.Infrastructure;
using StopWastingTime.App.Localization;
using StopWastingTime.App.ViewModels;
using StopWastingTime.App.Views;
using StopWastingTime.Core;
using StopWastingTime.Core.Blocking;
using StopWastingTime.Core.Data;
using StopWastingTime.Core.Models;
using StopWastingTime.Core.Sessions;
using StopWastingTime.Core.Settings;
using StopWastingTime.Core.Stats;

namespace StopWastingTime.App;

/// <summary>
/// Application entry point. It settles the language, refuses to run twice, wires up services, prepares
/// the database, cleans up after any previous crash, and only then opens the launcher window.
/// </summary>
public partial class App : Application
{
    private readonly SingleInstanceGuard _singleInstance = new();
    private readonly SettingsStore _settings = new();

    private IHost? _host;
    private string? _remainingText;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // First of all, because it decides the words of everything that follows, including the one
        // message that can appear before anything else is set up.
        Localizer.Instance.Initialize(_settings.Load().Language);

        if (!_singleInstance.TryAcquire())
        {
            MessageBox.Show(
                Localizer.Instance["App_AlreadyRunning"],
                "Stop Wasting Time",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            Shutdown();
            return;
        }

        AppPaths.EnsureDataDirectory();
        _host = BuildHost();

        var logger = _host.Services.GetRequiredService<ILogger<App>>();
        AttachGlobalExceptionHandlers(logger);
        RememberLanguageChoice(logger);

        try
        {
            var report = await StartAsync().ConfigureAwait(true);
            logger.LogInformation(
                "Started. Elevated: {Elevated}. Database: {Database}. Rules: {AppRules} apps, {SiteRules} sites.",
                report.IsElevated,
                report.DatabasePath,
                report.AppRules,
                report.SiteRules);

            var launcher = new LauncherWindow(
                report,
                Localizer.Instance,
                () => _host.Services.GetRequiredService<MainWindow>());
            MainWindow = launcher;
            launcher.Show();
        }
        catch (Exception exception)
        {
            logger.LogCritical(exception, "The app could not start.");
            ShowFatalError(exception);
            Shutdown();
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            try
            {
                // Leaving the machine blocked after the app closes would be the worst possible bug.
                await _host.Services.GetRequiredService<BlockingCoordinator>().ReleaseAsync().ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                _host.Services.GetRequiredService<ILogger<App>>()
                    .LogError(exception, "Could not release the blocking on exit.");
            }

            _host.Dispose();
        }

        _singleInstance.Dispose();
        base.OnExit(e);
    }

    /// <summary>Prepares everything the app needs before the first window appears.</summary>
    private async Task<StartupReport> StartAsync()
    {
        var services = _host!.Services;

        await services.GetRequiredService<DatabaseInitializer>().InitializeAsync().ConfigureAwait(true);

        // Closes sessions a crash left open and takes any leftover block off the hosts file.
        await services.GetRequiredService<FocusSessionService>().RecoverAsync().ConfigureAwait(true);

        WireBlockNotifications(services);

        var rules = await services.GetRequiredService<BlockRuleRepository>().GetAllAsync().ConfigureAwait(true);

        return new StartupReport(
            Version: Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0",
            IsElevated: ElevationHelper.IsElevated(),
            DatabasePath: AppPaths.DatabaseFile,
            LogPath: AppPaths.LogFile,
            ExecutablePath: ResolveExecutablePath(),
            AppRules: rules.Count(rule => rule.Kind == BlockKind.Process),
            SiteRules: rules.Count(rule => rule.Kind == BlockKind.Website));
    }

    /// <summary>
    /// A blocked app is closed from a background sweep, so the notice has to be marshalled onto the UI
    /// thread before a window can be shown.
    /// </summary>
    private void WireBlockNotifications(IServiceProvider services)
    {
        var sessions = services.GetRequiredService<FocusSessionService>();
        sessions.Progressed += (_, progress) => _remainingText = progress.RemainingText;

        // Built here, on the UI thread, and kept alive for the life of the app: it owns the one timer
        // that advances whichever session is running.
        services.GetRequiredService<SessionTicker>();

        var localizer = services.GetRequiredService<Localizer>();
        services.GetRequiredService<BlockingCoordinator>().Blocked += (_, blocked) =>
            Dispatcher.Invoke(() => BlockToastWindow.ShowFor(blocked.DisplayName, _remainingText, localizer));
    }

    /// <summary>
    /// Writes the language down the moment it is picked, so the next start opens in it. Failing to save
    /// only costs that, so it is logged and the switch goes ahead.
    /// </summary>
    private void RememberLanguageChoice(ILogger logger)
    {
        Localizer.Instance.LanguageChanged += (_, _) =>
        {
            try
            {
                _settings.Save(_settings.Load() with { Language = Localizer.Instance.Current.Code });
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "Could not save the language choice.");
            }
        };
    }

    /// <summary>
    /// The manifested executable that Windows will elevate. When the app is started through
    /// <c>dotnet StopWastingTime.dll</c> the running process is dotnet, so the executable is found next
    /// to the assembly instead of through the current process.
    /// </summary>
    private static string ResolveExecutablePath()
    {
        var assemblyPath = Assembly.GetEntryAssembly()?.Location;

        if (!string.IsNullOrEmpty(assemblyPath))
        {
            var candidate = Path.ChangeExtension(assemblyPath, ".exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Environment.ProcessPath ?? string.Empty;
    }

    private static IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new FileLoggerProvider(AppPaths.LogFile));
        builder.Logging.SetMinimumLevel(LogLevel.Information);

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(Localizer.Instance);

        // Storage
        builder.Services.AddSingleton(_ => new SqliteConnectionFactory());
        builder.Services.AddSingleton<DatabaseInitializer>();
        builder.Services.AddSingleton<SessionRepository>();
        builder.Services.AddSingleton<BlockRuleRepository>();
        builder.Services.AddSingleton<BlockHitRepository>();
        builder.Services.AddSingleton<StatsService>();

        // Blocking. Both blockers are also resolved on their own, so the UI can ask the hosts blocker
        // whether it ran into trouble.
        builder.Services.AddSingleton<IProcessScanner, SystemProcessScanner>();
        builder.Services.AddSingleton<IHostsFileAccess, SystemHostsFileAccess>();
        builder.Services.AddSingleton<ProcessBlocker>();
        builder.Services.AddSingleton<HostsFileBlocker>();
        builder.Services.AddSingleton<IBlocker>(services => services.GetRequiredService<ProcessBlocker>());
        builder.Services.AddSingleton<IBlocker>(services => services.GetRequiredService<HostsFileBlocker>());
        builder.Services.AddSingleton<BlockingCoordinator>();

        // Sessions and screens
        builder.Services.AddSingleton<FocusSessionService>();
        builder.Services.AddSingleton<SessionTicker>();
        builder.Services.AddSingleton<IDialogService, DialogService>();
        builder.Services.AddSingleton<AppIconProvider>();
        builder.Services.AddSingleton<FocusViewModel>();
        builder.Services.AddSingleton<UltraFocusViewModel>();
        builder.Services.AddSingleton<BlocklistViewModel>();
        builder.Services.AddSingleton<StatsViewModel>();
        builder.Services.AddSingleton<ShellViewModel>();
        builder.Services.AddTransient<MainWindow>();

        return builder.Build();
    }

    /// <summary>
    /// Anything that escapes gets written to the log and shown once, instead of closing the window
    /// without a word. Dispatcher errors are swallowed so a broken view cannot kill a running session.
    /// </summary>
    private void AttachGlobalExceptionHandlers(ILogger logger)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            logger.LogError(args.Exception, "Unhandled exception on the UI thread.");
            ShowFatalError(args.Exception);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                logger.LogCritical(exception, "Unhandled exception outside the UI thread.");
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            logger.LogError(args.Exception, "Unobserved task exception.");
            args.SetObserved();
        };
    }

    private static void ShowFatalError(Exception exception) =>
        MessageBox.Show(
            string.Join(
                Environment.NewLine + Environment.NewLine,
                Localizer.Instance["App_FatalError"],
                exception.Message,
                Localizer.Instance.Format("App_FatalError_Log", AppPaths.LogFile)),
            "Stop Wasting Time",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
}
