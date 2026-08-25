using Med.Domain.Entities;

namespace Med.Application.Abstractions;

public interface IDiagnosisRepository
{
    Task<IReadOnlyList<Diagnosis>> ListAsync(CancellationToken cancellationToken = default);

    Task<Diagnosis?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task UpsertAsync(Diagnosis diagnosis, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
