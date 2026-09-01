using Med.Presentation.Abstractions;
using Med.Presentation.Feedback;

namespace Med.Presentation.Tests;

internal static class TestFeedback
{
    internal static UserFeedback Instance { get; } = new(new NoOpSnackbarService());

    private sealed class NoOpSnackbarService : ISnackbarService
    {
        public Task ShowAsync(string message, string? title = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
