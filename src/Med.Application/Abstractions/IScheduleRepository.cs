using Med.Domain.Entities;

namespace Med.Application.Abstractions;

public interface IScheduleRepository
{
    Task<IReadOnlyList<Schedule>> ListByCourseAsync(
        Guid courseId,
        CancellationToken cancellationToken = default);

    Task<Schedule?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task UpsertAsync(Schedule schedule, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
