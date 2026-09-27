namespace Med.Application.Abstractions;

/// <summary>
/// Сервис локальных системных уведомлений операционной системы (macOS / Android / Desktop).
/// Обеспечивает автономную работу напоминаний о приёме лекарств без внешних ботов.
/// </summary>
public interface INotificationService
{
    /// <summary>Показать системное уведомление немедленно.</summary>
    Task ShowAsync(string title, string body, CancellationToken cancellationToken = default);

    /// <summary>Запланировать системное уведомление на указанное время.</summary>
    Task ScheduleAsync(string id, string title, string body, DateTimeOffset at, CancellationToken cancellationToken = default);

    /// <summary>Отменить ранее запланированное системное уведомление по идентификатору.</summary>
    Task CancelAsync(string id, CancellationToken cancellationToken = default);
}
