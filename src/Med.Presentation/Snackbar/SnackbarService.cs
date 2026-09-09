using Med.Presentation.Abstractions;
using Microsoft.Extensions.Logging;

namespace Med.Presentation.Snackbar;

public sealed class SnackbarService : ISnackbarService, IDisposable
{
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(4.0);

    private readonly SnackbarViewModel _viewModel;
    private readonly IUiDispatcher _ui;
    private readonly ILogger<SnackbarService>? _logger;
    private readonly Lock _gate = new();

    private long _currentSequence;
    private CancellationTokenSource? _activeCts;
    private DateTimeOffset _timerStartedAt;
    private TimeSpan _remainingDuration;
    private bool _isPaused;
    private bool _isDisposed;

    public SnackbarService(
        SnackbarViewModel viewModel,
        IUiDispatcher ui,
        ILogger<SnackbarService>? logger = null)
    {
        _viewModel = viewModel;
        _ui = ui;
        _logger = logger;

        _viewModel.DismissAction = Dismiss;
        _viewModel.PauseAction = Pause;
        _viewModel.ResumeAction = Resume;
    }

    public Task ShowAsync(string message, string? title = null, CancellationToken cancellationToken = default) =>
        ShowAsync(message, title, null, SnackbarType.Info, cancellationToken);

    public async Task ShowAsync(
        string message,
        string? title = null,
        TimeSpan? duration = null,
        SnackbarType type = SnackbarType.Info,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        TimeSpan targetDuration = duration ?? DefaultDuration;
        long sequence;
        CancellationToken currentToken;

        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            sequence = Interlocked.Increment(ref _currentSequence);
            _activeCts?.Cancel();
            _activeCts?.Dispose();
            _activeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            currentToken = _activeCts.Token;

            _remainingDuration = targetDuration;
            _timerStartedAt = DateTimeOffset.UtcNow;
            _isPaused = false;
        }

        _logger?.LogInformation(
            "[Snackbar] Notification #{Sequence} created: Title='{Title}', Type={Type}, Duration={Duration}s, Message='{Message}'",
            sequence, title ?? string.Empty, type, targetDuration.TotalSeconds, message);

        _ui.Post(() =>
        {
            _viewModel.Title = title ?? string.Empty;
            _viewModel.Message = message;
            _viewModel.Type = type;
            _viewModel.IsOpen = true;
        });

        await RunTimerAsync(sequence, targetDuration, currentToken).ConfigureAwait(false);
    }

    private async Task RunTimerAsync(long sequence, TimeSpan duration, CancellationToken token)
    {
        try
        {
            _logger?.LogDebug("[Snackbar] Notification #{Sequence} timer started ({Duration}s)", sequence, duration.TotalSeconds);
            await Task.Delay(duration, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger?.LogDebug("[Snackbar] Notification #{Sequence} timer cancelled or reset", sequence);
            return;
        }

        lock (_gate)
        {
            if (sequence != _currentSequence || _isPaused || _isDisposed)
            {
                return;
            }
        }

        _logger?.LogInformation("[Snackbar] Notification #{Sequence} auto-closed by timeout", sequence);
        _ui.Post(() =>
        {
            lock (_gate)
            {
                if (sequence == _currentSequence)
                {
                    _viewModel.IsOpen = false;
                }
            }
        });
    }

    public void Dismiss()
    {
        long sequence;
        lock (_gate)
        {
            sequence = _currentSequence;
            _activeCts?.Cancel();
            _activeCts?.Dispose();
            _activeCts = null;
            _isPaused = false;
        }

        _logger?.LogInformation("[Snackbar] Notification #{Sequence} dismissed by user", sequence);
        _ui.Post(() => _viewModel.IsOpen = false);
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (_isPaused || _activeCts == null || !_viewModel.IsOpen)
            {
                return;
            }

            TimeSpan elapsed = DateTimeOffset.UtcNow - _timerStartedAt;
            _remainingDuration = _remainingDuration > elapsed ? _remainingDuration - elapsed : TimeSpan.FromMilliseconds(500);
            _isPaused = true;
            _activeCts.Cancel();
            _activeCts.Dispose();
            _activeCts = null;
        }

        _logger?.LogDebug("[Snackbar] Notification #{Sequence} paused on hover (remaining: {Remaining:F1}s)", _currentSequence, _remainingDuration.TotalSeconds);
    }

    public void Resume()
    {
        long sequence;
        TimeSpan duration;
        CancellationToken token;

        lock (_gate)
        {
            if (!_isPaused || _isDisposed || !_viewModel.IsOpen)
            {
                return;
            }

            sequence = _currentSequence;
            duration = _remainingDuration > TimeSpan.FromMilliseconds(200) ? _remainingDuration : TimeSpan.FromMilliseconds(500);
            _activeCts = new CancellationTokenSource();
            token = _activeCts.Token;
            _timerStartedAt = DateTimeOffset.UtcNow;
            _isPaused = false;
        }

        _logger?.LogDebug("[Snackbar] Notification #{Sequence} resumed from hover ({Duration:F1}s remaining)", sequence, duration.TotalSeconds);
        _ = RunTimerAsync(sequence, duration, token);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _isDisposed = true;
            _activeCts?.Cancel();
            _activeCts?.Dispose();
            _activeCts = null;
        }
    }
}
