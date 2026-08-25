using Med.Domain.Entities;

namespace Med.Application.Abstractions;

public interface IDoseEventRepository
{
    Task<IReadOnlyList<DoseEvent>> ListForLocalDateAsync(
        DateOnly localDate,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DoseEvent>> ListByScheduleAsync(
        Guid scheduleId,
        CancellationToken cancellationToken = default);

    Task UpsertManyAsync(
        IReadOnlyList<DoseEvent> events,
        CancellationToken cancellationToken = default);

    Task<DoseEvent?> GetAsync(Guid id, CancellationToken cancellationToken = default);
}
