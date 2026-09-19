using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StopWastingTime.App.Infrastructure;
using StopWastingTime.Core;
using StopWastingTime.Core.Data;
using StopWastingTime.Core.Models;
using StopWastingTime.Core.Stats;

namespace StopWastingTime.App;

/// <summary>
/// Application entry point. It refuses to run twice, wires up services, prepares the database and only
/// then opens a window, so the first thing the user sees already reflects a working install.
/// </summary>
public partial class App : Application
{
    private readonly SingleInstanceGuard _singleInstance = new();

    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!_singleInstance.TryAcquire())
        {
            MessageBox.Show(
                "Stop Wasting Time ya está abierto. Buscá su ventana o el ícono de la bandeja del sistema.",
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

        try
        {
            var report = await StartAsync().ConfigureAwait(true);
            logger.LogInformation(
                "Started. Elevated: {Elevated}. Database: {Database}. Rules: {AppRules} apps, {SiteRules} sites.",
                report.IsElevated,
                report.DatabasePath,
                report.AppRules,
                report.SiteRules);

            var window = new MainWindow { DataContext = report };
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            logger.LogCritical(exception, "The app could not start.");
            ShowFatalError(exception);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        _singleInstance.Dispose();
        base.OnExit(e);
    }

    /// <summary>Prepares everything the app needs before the first window appears.</summary>
    private async Task<StartupReport> StartAsync()
    {
        var services = _host!.Services;

        await services.GetRequiredService<DatabaseInitializer>().InitializeAsync().ConfigureAwait(true);

        var rules = await services.GetRequiredService<BlockRuleRepository>().GetAllAsync().ConfigureAwait(true);

        return new StartupReport(
            Version: Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0",
            IsElevated: ElevationHelper.IsElevated(),
            DatabasePath: AppPaths.DatabaseFile,
            LogPath: AppPaths.LogFile,
            AppRules: rules.Count(rule => rule.Kind == BlockKind.Process),
            SiteRules: rules.Count(rule => rule.Kind == BlockKind.Website));
    }

    private static IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new FileLoggerProvider(AppPaths.LogFile));
        builder.Logging.SetMinimumLevel(LogLevel.Information);

        builder.Services.AddSingleton(_ => new SqliteConnectionFactory());
        builder.Services.AddSingleton<DatabaseInitializer>();
        builder.Services.AddSingleton<SessionRepository>();
        builder.Services.AddSingleton<BlockRuleRepository>();
        builder.Services.AddSingleton<BlockHitRepository>();
        builder.Services.AddSingleton<StatsService>();

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
            $"Stop Wasting Time encontró un error:{Environment.NewLine}{Environment.NewLine}{exception.Message}" +
            $"{Environment.NewLine}{Environment.NewLine}El detalle quedó en {AppPaths.LogFile}",
            "Stop Wasting Time",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
}
