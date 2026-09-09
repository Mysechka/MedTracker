using Med.Domain.Enums;

namespace Med.Application.Abstractions;

public enum DoseEventChangeType
{
    Insert = 0,
    Update = 1,
    Delete = 2,
}

public sealed record DoseEventChange(
    Guid Id,
    DoseEventState State,
    DateTimeOffset ScheduledAt,
    DoseEventChangeType ChangeType,
    Guid? MedicationId = null);

/// <summary>Тонкая подписка на Realtime изменения dose_events.</summary>
public interface IDoseEventRealtime
{
    event EventHandler<DoseEventChange>? Changed;

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
