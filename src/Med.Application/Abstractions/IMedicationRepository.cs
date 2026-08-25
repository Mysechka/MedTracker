using Med.Domain.Entities;

namespace Med.Application.Abstractions;

public interface IMedicationRepository
{
    Task<IReadOnlyList<Medication>> ListAsync(CancellationToken cancellationToken = default);

    Task<Medication?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task UpsertAsync(Medication medication, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
