namespace Med.Presentation.Abstractions;

public enum SnackbarType
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>Всплывающие уведомления снизу экрана. Реализация живёт в Presentation, UI — в Med.Ui.</summary>
public interface ISnackbarService
{
    Task ShowAsync(string message, string? title = null, CancellationToken cancellationToken = default);

    Task ShowAsync(
        string message,
        string? title,
        TimeSpan? duration,
        SnackbarType type = SnackbarType.Info,
        CancellationToken cancellationToken = default) =>
        ShowAsync(message, title, cancellationToken);

    void Dismiss() { }

    void Pause() { }

    void Resume() { }
}
