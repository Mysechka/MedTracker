using Med.Domain.Entities;

namespace Med.Application.Abstractions;

public interface IDocumentRepository
{
    Task<IReadOnlyList<Document>> ListAsync(CancellationToken cancellationToken = default);

    Task<Document?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task UpsertAsync(Document document, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
