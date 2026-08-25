using Med.Domain.Entities;

namespace Med.Application.Abstractions;

public interface IProfileRepository
{
    Task<Profile?> GetCurrentAsync(CancellationToken cancellationToken = default);

    Task UpdateAsync(Profile profile, CancellationToken cancellationToken = default);
}
