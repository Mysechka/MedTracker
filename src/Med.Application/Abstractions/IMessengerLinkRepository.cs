using Med.Domain.Entities;
using Med.Domain.Enums;

namespace Med.Application.Abstractions;

public interface IMessengerLinkRepository
{
    Task<IReadOnlyList<MessengerLink>> ListAsync(CancellationToken cancellationToken = default);

    Task<MessengerLink?> GetByChannelAsync(
        MessengerChannelType channelType,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(MessengerLink link, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
