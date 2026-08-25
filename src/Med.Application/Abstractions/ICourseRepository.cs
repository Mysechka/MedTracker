using Med.Domain.Entities;

namespace Med.Application.Abstractions;

public interface ICourseRepository
{
    Task<IReadOnlyList<Course>> ListAsync(CancellationToken cancellationToken = default);

    Task<Course?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task UpsertAsync(Course course, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
