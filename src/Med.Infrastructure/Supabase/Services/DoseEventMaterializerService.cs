using Med.Application.Abstractions;
using Med.Infrastructure.Supabase;
using Supabase.Postgrest.Responses;

namespace Med.Infrastructure.Supabase.Services;

public sealed class DoseEventMaterializerService : IDoseEventMaterializer
{
    private readonly ISupabaseClientAccessor _accessor;

    public DoseEventMaterializerService(ISupabaseClientAccessor accessor)
    {
        _accessor = accessor;
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
            ["p_now"] = DateTimeOffset.UtcNow,
        };

        BaseResponse response = await client.Rpc("materialize_upcoming_doses", parameters)
            .ConfigureAwait(false);

        return JsonRpcParser.ParseInt(response.Content);
    }
}
