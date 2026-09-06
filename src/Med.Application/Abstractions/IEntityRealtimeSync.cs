namespace Med.Application.Abstractions;

public enum EntityChangeType
{
    Insert = 0,
    Update = 1,
    Delete = 2,
}

public sealed record EntityChangeNotification(
    string TableName,
    Guid EntityId,
    EntityChangeType ChangeType,
    DateTimeOffset Timestamp,
    object? Payload = null);

/// <summary>
/// Потокобезопасная подписка на Realtime изменения сущностей пользователя (Postgres Changes).
/// </summary>
public interface IEntityRealtimeSync
{
    event EventHandler<EntityChangeNotification>? EntityChanged;

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
