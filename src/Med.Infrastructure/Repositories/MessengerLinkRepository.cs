using Med.Application.Abstractions;
using Med.Infrastructure.Supabase;
using Med.Domain.Entities;
using Med.Domain.Enums;
using Med.Infrastructure.Mapping;
using Med.Infrastructure.Models;
using static Supabase.Postgrest.Constants;

namespace Med.Infrastructure.Repositories;

public sealed class MessengerLinkRepository : IMessengerLinkRepository
{
    private readonly ISupabaseClientAccessor _accessor;
    private readonly IAuthService _auth;

    public MessengerLinkRepository(ISupabaseClientAccessor accessor, IAuthService auth)
    {
        _accessor = accessor;
        _auth = auth;
    }

    public async Task<IReadOnlyList<MessengerLink>> ListAsync(CancellationToken cancellationToken = default)
    {
        _ = SupabaseRepositoryHelper.RequireCurrentUserId(_auth);
        global::Supabase.Client client = await SupabaseRepositoryHelper
            .GetInitializedClientAsync(_accessor, cancellationToken)
            .ConfigureAwait(false);

        var response = await client.From<MessengerLinkRow>().Get(cancellationToken).ConfigureAwait(false);
        return response.Models.Select(EntityMappers.ToDomain).ToArray();
    }

    public async Task<MessengerLink?> GetByChannelAsync(
        MessengerChannelType channelType,
        CancellationToken cancellationToken = default)
    {
        _ = SupabaseRepositoryHelper.RequireCurrentUserId(_auth);
        global::Supabase.Client client = await SupabaseRepositoryHelper
            .GetInitializedClientAsync(_accessor, cancellationToken)
            .ConfigureAwait(false);

        string channelDb = EntityMappers.ToMessengerChannelDb(channelType);
        var response = await client
            .From<MessengerLinkRow>()
            .Filter("channel_type", Operator.Equals, channelDb)
            .Get(cancellationToken)
            .ConfigureAwait(false);

        MessengerLinkRow? row = response.Models.FirstOrDefault();
        return row is null ? null : EntityMappers.ToDomain(row);
    }

    public async Task UpsertAsync(MessengerLink link, CancellationToken cancellationToken = default)
    {
        Guid userId = SupabaseRepositoryHelper.RequireCurrentUserId(_auth);
        if (link.UserId != userId)
        {
            throw new InvalidOperationException("messenger_link.user_id должен совпадать с текущим пользователем.");
        }

        global::Supabase.Client client = await SupabaseRepositoryHelper
            .GetInitializedClientAsync(_accessor, cancellationToken)
            .ConfigureAwait(false);

        MessengerLinkRow row = EntityMappers.ToRow(link);
        await client.From<MessengerLinkRow>().Upsert(row, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _ = SupabaseRepositoryHelper.RequireCurrentUserId(_auth);
        global::Supabase.Client client = await SupabaseRepositoryHelper
            .GetInitializedClientAsync(_accessor, cancellationToken)
            .ConfigureAwait(false);

        await client
            .From<MessengerLinkRow>()
            .Filter("id", Operator.Equals, id.ToString())
            .Delete(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }
}
