using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace StopWastingTime.App.Infrastructure;

/// <summary>
/// Minimal append-only file logger. A windowed app has no console, so when the launcher fails to start
/// this file is the only place that explains why. It keeps one file and truncates it when it grows past
/// <see cref="MaxBytes"/>, which is plenty for a desktop app nobody watches.
/// </summary>
public sealed class FileLoggerProvider(string filePath) : ILoggerProvider
{
    private const long MaxBytes = 1024 * 1024;

    private readonly Lock _writeLock = new();

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
    }

    private void Write(string line)
    {
        lock (_writeLock)
        {
            try
            {
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (File.Exists(filePath) && new FileInfo(filePath).Length > MaxBytes)
                {
                    File.Delete(filePath);
                }

                File.AppendAllText(filePath, line, Encoding.UTF8);
            }
            catch (IOException)
            {
                // Logging must never take the app down.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var builder = new StringBuilder();
            builder.Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", System.Globalization.CultureInfo.InvariantCulture));
            builder.Append(" [").Append(logLevel).Append("] ");
            builder.Append(category).Append(" - ");
            builder.Append(formatter(state, exception));
            builder.AppendLine();

            if (exception is not null)
            {
                builder.AppendLine(exception.ToString());
            }

            provider.Write(builder.ToString());
        }
    }
}
