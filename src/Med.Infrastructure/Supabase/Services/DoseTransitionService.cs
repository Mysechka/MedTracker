using Med.Application.Abstractions;
using Med.Domain.Abstractions;
using Med.Domain.Enums;
using Supabase.Postgrest.Responses;

namespace Med.Infrastructure.Supabase.Services;

public sealed class DoseTransitionService : IDoseTransitionService
{
    private readonly ISupabaseClientAccessor _accessor;
    private readonly ISystemClock _clock;

    public DoseTransitionService(ISupabaseClientAccessor accessor, ISystemClock? clock = null)
    {
        _accessor = accessor;
        _clock = clock ?? new DefaultSystemClock();
    }

    private sealed class DefaultSystemClock : ISystemClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
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
            ["p_taken_at"] = (takenAt ?? _clock.UtcNow).ToUniversalTime(),
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
            ["p_skipped_at"] = (skippedAt ?? _clock.UtcNow).ToUniversalTime(),
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
            ["p_undone_at"] = (undoneAt ?? _clock.UtcNow).ToUniversalTime(),
        };

        BaseResponse response = await client.Rpc("undo_confirm_dose", parameters).ConfigureAwait(false);
        return JsonRpcParser.ParseDoseTransition(response.Content ?? "{}", doseEventId);
    }
}
