using Med.Application.Abstractions;
using Med.Domain.Abstractions;
using Med.Infrastructure.Supabase;
using Supabase.Postgrest.Responses;

namespace Med.Infrastructure.Supabase.Services;

public sealed class DoseEventMaterializerService : IDoseEventMaterializer
{
    private readonly ISupabaseClientAccessor _accessor;
    private readonly ISystemClock _clock;

    public DoseEventMaterializerService(ISupabaseClientAccessor accessor, ISystemClock? clock = null)
    {
        _accessor = accessor;
        _clock = clock ?? new DefaultSystemClock();
    }

    private sealed class DefaultSystemClock : ISystemClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }

    public async Task<int> MaterializeAsync(
        int horizonDays = 14,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (horizonDays <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(horizonDays));
        }

        global::Supabase.Client client = await _accessor.GetClientAsync(cancellationToken)
            .ConfigureAwait(false);

        var parameters = new Dictionary<string, object?>
        {
            ["p_horizon_days"] = horizonDays,
            ["p_user_id"] = null,
            ["p_now"] = _clock.UtcNow,
        };

        BaseResponse response = await client.Rpc("materialize_upcoming_doses", parameters)
            .ConfigureAwait(false);

        return JsonRpcParser.ParseInt(response.Content);
    }
}
