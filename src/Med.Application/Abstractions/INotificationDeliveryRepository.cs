using Med.Domain.Entities;

namespace Med.Application.Abstractions;

public interface INotificationDeliveryRepository
{
    Task<IReadOnlyList<NotificationDelivery>> ListByDoseEventAsync(
        Guid doseEventId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NotificationDelivery>> ListRecentAsync(
        int limit = 50,
        CancellationToken cancellationToken = default);
}
