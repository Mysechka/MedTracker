namespace Med.Presentation.Abstractions;

/// <summary>Всплывающие уведомления снизу экрана. Реализация живёт в Presentation, UI — в Med.Ui.</summary>
public interface ISnackbarService
{
    Task ShowAsync(string message, string? title = null, CancellationToken cancellationToken = default);
}
