using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Med.Infrastructure.Logging;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logDirectory;
    private readonly LogLevel _minLevel;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();
    private readonly BlockingCollection<string> _logQueue = new(new ConcurrentQueue<string>());
    private readonly CancellationTokenSource _cts = new();
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

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name => new FileLogger(name, this, _minLevel));
    }

    internal void EnqueueLog(string entry)
    {
        if (!_isDisposed && !_logQueue.IsAddingCompleted)
        {
            try
            {
                _logQueue.Add(entry);
            }
            catch (InvalidOperationException)
            {
                // Queue completed
            }
        }
    }

    private async Task ProcessLogQueueAsync()
    {
        while (!_cts.Token.IsCancellationRequested || !_logQueue.IsCompleted)
        {
            try
            {
                if (_logQueue.TryTake(out string? entry, 200, _cts.Token) && entry != null)
                {
                    await WriteToFileAsync(entry).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Suppress background logging errors to never crash the app
            }
        }

        // Drain remaining entries on shutdown
        while (_logQueue.TryTake(out string? remainingEntry))
        {
            try
            {
                await WriteToFileAsync(remainingEntry).ConfigureAwait(false);
            }
            catch { }
        }
    }

    private async Task WriteToFileAsync(string entry)
    {
        try
        {
            if (!Directory.Exists(_logDirectory))
            {
                Directory.CreateDirectory(_logDirectory);
            }

            string filePath = Path.Combine(_logDirectory, $"medtracker-{DateTime.UtcNow:yyyy-MM-dd}.log");
            byte[] bytes = Encoding.UTF8.GetBytes(entry + Environment.NewLine);

            using FileStream stream = new(
                filePath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite,
                bufferSize: 4096,
                useAsync: true);

            await stream.WriteAsync(bytes).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }
        catch
        {
            // File I/O fallback
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _logQueue.CompleteAdding();
        _cts.Cancel();

        try
        {
            _outputTask.Wait(TimeSpan.FromSeconds(1));
        }
        catch { }

        _cts.Dispose();
        _logQueue.Dispose();
    }
}

public sealed class FileLogger : ILogger
{
    private readonly string _categoryName;
    private readonly FileLoggerProvider _provider;
    private readonly LogLevel _minLevel;

    public FileLogger(string categoryName, FileLoggerProvider provider, LogLevel minLevel)
    {
        _categoryName = categoryName;
        _provider = provider;
        _minLevel = minLevel;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= _minLevel && logLevel != LogLevel.None;

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
        if (string.IsNullOrEmpty(message) && exception == null)
        {
            return;
        }

        string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
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

        StringBuilder sb = new();
        sb.Append($"[{timestamp}] [{levelStr}] [{_categoryName}] [T#{Environment.CurrentManagedThreadId}] {message}");

        if (exception != null)
        {
            sb.AppendLine();
            sb.Append($"   Exception: {exception.GetType().FullName}: {exception.Message}");
            if (!string.IsNullOrWhiteSpace(exception.StackTrace))
            {
                sb.AppendLine();
                sb.Append($"   StackTrace: {exception.StackTrace}");
            }
            if (exception.InnerException != null)
            {
                sb.AppendLine();
                sb.Append($"   InnerException: {exception.InnerException.GetType().FullName}: {exception.InnerException.Message}");
            }
        }

        _provider.EnqueueLog(sb.ToString());
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();
        public void Dispose() { }
    }
}
