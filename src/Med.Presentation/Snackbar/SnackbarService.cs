using Med.Presentation.Abstractions;

namespace Med.Presentation.Snackbar;

public sealed class SnackbarService : ISnackbarService
{
    private static readonly TimeSpan VisibleDuration = TimeSpan.FromSeconds(4.5);

    private readonly SnackbarViewModel _viewModel;
    private readonly IUiDispatcher _ui;
    private readonly Lock _gate = new();
    private CancellationTokenSource? _hideCts;

    public SnackbarService(SnackbarViewModel viewModel, IUiDispatcher ui)
    {
        _viewModel = viewModel;
        _ui = ui;
    }

    public async Task ShowAsync(string message, string? title = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        CancellationToken hideToken;
        lock (_gate)
        {
            _hideCts?.Cancel();
            _hideCts?.Dispose();
            _hideCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            hideToken = _hideCts.Token;
        }

        _ui.Post(() =>
        {
            _viewModel.Title = title ?? string.Empty;
            _viewModel.Message = message;
            _viewModel.IsOpen = true;
        });

        try
        {
            await Task.Delay(VisibleDuration, hideToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (hideToken.IsCancellationRequested)
        {
            return;
        }

        _ui.Post(() => _viewModel.IsOpen = false);
    }
}
