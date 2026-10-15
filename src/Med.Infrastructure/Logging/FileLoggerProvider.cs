using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Med.Infrastructure.Logging;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logDirectory;
    private readonly LogLevel _minLevel;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();
    private readonly Channel<string> _logChannel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
    {
        SingleReader = true,
        AllowSynchronousContinuations = false,
    });
    private readonly Task _outputTask;
    private bool _isDisposed;

    public FileLoggerProvider(string? logDirectory = null, LogLevel minLevel = LogLevel.Information)
    {
        _logDirectory = logDirectory ?? Path.Combine(AppContext.BaseDirectory, "logs");
        _minLevel = minLevel;

        try
        {
            Directory.CreateDirectory(_logDirectory);
        }
        catch
        {
            // Directory will be retried on write if needed
        }

        _outputTask = Task.Run(ProcessLogQueueAsync);
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new FileLogger(name, this, _minLevel));

    internal void EnqueueLog(string entry)
    {
        if (!_isDisposed)
        {
            _logChannel.Writer.TryWrite(entry);
        }
    }

    private async Task ProcessLogQueueAsync()
    {
        var reader = _logChannel.Reader;
        var batch = new StringBuilder(1024);

        while (await reader.WaitToReadAsync().ConfigureAwait(false))
        {
            batch.Clear();
            while (reader.TryRead(out string? entry))
            {
                batch.AppendLine(entry);
            }

            if (batch.Length > 0)
            {
                await WriteBatchToFileAsync(batch.ToString()).ConfigureAwait(false);
            }
        }
    }

    private async Task WriteBatchToFileAsync(string content)
    {
        try
        {
            Directory.CreateDirectory(_logDirectory);
            string filePath = Path.Combine(_logDirectory, $"medtracker-{DateTime.UtcNow:yyyy-MM-dd}.log");
            await File.AppendAllTextAsync(filePath, content, Encoding.UTF8).ConfigureAwait(false);
        }
        catch
        {
            // Suppress background logging errors to never crash the app
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _logChannel.Writer.TryComplete();

        try
        {
            _outputTask.Wait(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Ignore shutdown exceptions
        }
    }
}

public sealed class FileLogger(string categoryName, FileLoggerProvider provider, LogLevel minLevel) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= minLevel && logLevel != LogLevel.None;

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

        string message = formatter(state, exception);
        if (string.IsNullOrEmpty(message) && exception is null)
        {
            return;
        }

        string levelStr = logLevel switch
        {
            LogLevel.Trace => "TRACE",
            LogLevel.Debug => "DEBUG",
            LogLevel.Information => "INFO ",
            LogLevel.Warning => "WARN ",
            LogLevel.Error => "ERROR",
            LogLevel.Critical => "CRIT ",
            _ => "NONE "
        };

        string header = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] [{levelStr}] [{categoryName}] [T#{Environment.CurrentManagedThreadId}] {message}";
        if (exception is null)
        {
            provider.EnqueueLog(header);
            return;
        }

        StringBuilder sb = new(header.Length + 256);
        sb.Append(header)
          .AppendLine()
          .Append("   Exception: ").Append(exception.GetType().FullName).Append(": ").Append(exception.Message);

        if (!string.IsNullOrWhiteSpace(exception.StackTrace))
        {
            sb.AppendLine().Append("   StackTrace: ").Append(exception.StackTrace);
        }
        if (exception.InnerException is { } inner)
        {
            sb.AppendLine().Append("   InnerException: ").Append(inner.GetType().FullName).Append(": ").Append(inner.Message);
        }

        provider.EnqueueLog(sb.ToString());
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();
        public void Dispose() { }
    }
}
