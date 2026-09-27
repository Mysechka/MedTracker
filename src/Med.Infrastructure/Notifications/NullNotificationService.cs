using Med.Application.Abstractions;

namespace Med.Infrastructure.Notifications;

/// <summary>
/// Пустая реализация сервиса уведомлений (Null Object Pattern).
/// </summary>
public sealed class NullNotificationService : INotificationService
{
    public static NullNotificationService Instance { get; } = new();

    public Task ShowAsync(string title, string body, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task ScheduleAsync(string id, string title, string body, DateTimeOffset at, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task CancelAsync(string id, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
