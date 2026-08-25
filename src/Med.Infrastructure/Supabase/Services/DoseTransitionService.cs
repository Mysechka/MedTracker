using Med.Application.Abstractions;
using Med.Domain.Enums;
using Supabase.Postgrest.Responses;

namespace Med.Infrastructure.Supabase.Services;

public sealed class DoseTransitionService : IDoseTransitionService
{
    private readonly ISupabaseClientAccessor _accessor;

    public DoseTransitionService(ISupabaseClientAccessor accessor)
    {
        _accessor = accessor;
    }

    public async Task<DoseTransitionResult> ConfirmAsync(
        Guid doseEventId,
        DoseEventSource source,
        DateTimeOffset? takenAt = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        global::Supabase.Client client = await _accessor.GetClientAsync(cancellationToken).ConfigureAwait(false);

        var parameters = new Dictionary<string, object?>
        {
            ["p_dose_event_id"] = doseEventId,
            ["p_source"] = source.ToString(),
            ["p_taken_at"] = (takenAt ?? DateTimeOffset.UtcNow).ToUniversalTime(),
        };

        BaseResponse response = await client.Rpc("confirm_dose", parameters).ConfigureAwait(false);
        return JsonRpcParser.ParseDoseTransition(response.Content ?? "{}", doseEventId);
    }

    public async Task<DoseTransitionResult> SkipAsync(
        Guid doseEventId,
        DoseEventSource source,
        DateTimeOffset? skippedAt = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        global::Supabase.Client client = await _accessor.GetClientAsync(cancellationToken).ConfigureAwait(false);

        var parameters = new Dictionary<string, object?>
        {
            ["p_dose_event_id"] = doseEventId,
            ["p_source"] = source.ToString(),
            ["p_skipped_at"] = (skippedAt ?? DateTimeOffset.UtcNow).ToUniversalTime(),
        };

        BaseResponse response = await client.Rpc("skip_dose", parameters).ConfigureAwait(false);
        return JsonRpcParser.ParseDoseTransition(response.Content ?? "{}", doseEventId);
    }

    public async Task<DoseTransitionResult> UndoConfirmAsync(
        Guid doseEventId,
        DateTimeOffset? undoneAt = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        global::Supabase.Client client = await _accessor.GetClientAsync(cancellationToken).ConfigureAwait(false);

        var parameters = new Dictionary<string, object?>
        {
            ["p_dose_event_id"] = doseEventId,
            ["p_undone_at"] = (undoneAt ?? DateTimeOffset.UtcNow).ToUniversalTime(),
        };

        BaseResponse response = await client.Rpc("undo_confirm_dose", parameters).ConfigureAwait(false);
        return JsonRpcParser.ParseDoseTransition(response.Content ?? "{}", doseEventId);
    }
}
