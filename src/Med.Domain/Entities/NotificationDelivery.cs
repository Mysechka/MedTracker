using Med.Domain.Enums;

namespace Med.Domain.Entities;

/// <summary>Журнал доставки уведомления по dose_event.</summary>
public sealed record NotificationDelivery(
    Guid Id,
    Guid UserId,
    Guid DoseEventId,
    NotificationChannelType ChannelType,
    string? MessageId,
    NotificationDeliveryStatus Status,
    int Attempts,
    DateTimeOffset? NextRetryAt,
    string? LastError)
{
    public static NotificationDelivery CreatePending(
        Guid id,
        Guid userId,
        Guid doseEventId,
        NotificationChannelType channelType)
    {
        return new NotificationDelivery(
            id,
            userId,
            doseEventId,
            channelType,
            MessageId: null,
            NotificationDeliveryStatus.Pending,
            Attempts: 0,
            NextRetryAt: null,
            LastError: null);
    }
}
