using Med.Application.Abstractions;
using Med.Infrastructure.Supabase;
using Med.Domain.Entities;
using Med.Infrastructure.Mapping;
using Med.Infrastructure.Models;
using static Supabase.Postgrest.Constants;

namespace Med.Infrastructure.Repositories;

public sealed class NotificationDeliveryRepository : INotificationDeliveryRepository
{
    private readonly ISupabaseClientAccessor _accessor;
    private readonly IAuthService _auth;

    public NotificationDeliveryRepository(ISupabaseClientAccessor accessor, IAuthService auth)
    {
        _accessor = accessor;
        _auth = auth;
    }

    public async Task<IReadOnlyList<NotificationDelivery>> ListByDoseEventAsync(
        Guid doseEventId,
        CancellationToken cancellationToken = default)
    {
        _ = SupabaseRepositoryHelper.RequireCurrentUserId(_auth);
        global::Supabase.Client client = await SupabaseRepositoryHelper
            .GetInitializedClientAsync(_accessor, cancellationToken)
            .ConfigureAwait(false);

        var response = await client
            .From<NotificationDeliveryRow>()
            .Filter("dose_event_id", Operator.Equals, doseEventId.ToString())
            .Get(cancellationToken)
            .ConfigureAwait(false);

        return response.Models.Select(EntityMappers.ToDomain).ToArray();
    }

    public async Task<IReadOnlyList<NotificationDelivery>> ListRecentAsync(
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        _ = SupabaseRepositoryHelper.RequireCurrentUserId(_auth);
        global::Supabase.Client client = await SupabaseRepositoryHelper
            .GetInitializedClientAsync(_accessor, cancellationToken)
            .ConfigureAwait(false);

        var response = await client
            .From<NotificationDeliveryRow>()
            .Order("created_at", Ordering.Descending)
            .Limit(limit)
            .Get(cancellationToken)
            .ConfigureAwait(false);

        return response.Models.Select(EntityMappers.ToDomain).ToArray();
    }
}
