using Med.Application.Abstractions;
using Med.Domain.Abstractions;
using Med.Domain.Entities;
using Med.Domain.Enums;
using Med.Domain.State;

namespace Med.Infrastructure.LocalStorage;

public sealed class LocalDoseTransitionService : IDoseTransitionService
{
    private readonly IDoseEventRepository _doseEvents;
    private readonly ISystemClock _clock;

    public LocalDoseTransitionService(IDoseEventRepository doseEvents, ISystemClock? clock = null)
    {
        _doseEvents = doseEvents;
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
        DoseEvent? doseEvent = await _doseEvents.GetAsync(doseEventId, cancellationToken).ConfigureAwait(false);
        if (doseEvent is null)
        {
            return new DoseTransitionResult("Rejected", doseEventId, Reason: "Событие не найдено");
        }

        // Если пользователь в приложении явно нажал «Принял» на ранее пропущенной дозе,
        // сбрасываем пропуск в Scheduled, чтобы корректно зафиксировать приём.
        if (doseEvent.State == DoseEventState.Skipped && source == DoseEventSource.App)
        {
            doseEvent = doseEvent with { State = DoseEventState.Scheduled, TakenAt = null, Source = null };
        }

        DateTimeOffset takenUtc = (takenAt ?? _clock.UtcNow).ToUniversalTime();
        TransitionResult transition = DoseEventTransitions.MarkTaken(doseEvent, takenUtc, source);
        if (transition.IsApplied && transition.Event is not null)
        {
            await _doseEvents.UpsertManyAsync([transition.Event], cancellationToken).ConfigureAwait(false);
        }

        return new DoseTransitionResult(
            transition.Outcome.ToString(),
            doseEventId,
            transition.Event?.State ?? doseEvent.State,
            transition.Reason);
    }

    public async Task<DoseTransitionResult> SkipAsync(
        Guid doseEventId,
        DoseEventSource source,
        DateTimeOffset? skippedAt = null,
        CancellationToken cancellationToken = default)
    {
        DoseEvent? doseEvent = await _doseEvents.GetAsync(doseEventId, cancellationToken).ConfigureAwait(false);
        if (doseEvent is null)
        {
            return new DoseTransitionResult("Rejected", doseEventId, Reason: "Событие не найдено");
        }

        DateTimeOffset skippedUtc = (skippedAt ?? _clock.UtcNow).ToUniversalTime();
        TransitionResult transition = DoseEventTransitions.MarkSkipped(doseEvent, skippedUtc, source);
        if (transition.IsApplied && transition.Event is not null)
        {
            await _doseEvents.UpsertManyAsync([transition.Event], cancellationToken).ConfigureAwait(false);
        }

        return new DoseTransitionResult(
            transition.Outcome.ToString(),
            doseEventId,
            transition.Event?.State ?? doseEvent.State,
            transition.Reason);
    }

    public async Task<DoseTransitionResult> UndoConfirmAsync(
        Guid doseEventId,
        DateTimeOffset? undoneAt = null,
        CancellationToken cancellationToken = default)
    {
        DoseEvent? doseEvent = await _doseEvents.GetAsync(doseEventId, cancellationToken).ConfigureAwait(false);
        if (doseEvent is null)
        {
            return new DoseTransitionResult("Rejected", doseEventId, Reason: "Событие не найдено");
        }

        TransitionResult transition = DoseEventTransitions.Undo(doseEvent);
        if (transition.IsApplied && transition.Event is not null)
        {
            await _doseEvents.UpsertManyAsync([transition.Event], cancellationToken).ConfigureAwait(false);
        }

        return new DoseTransitionResult(
            transition.Outcome.ToString(),
            doseEventId,
            transition.Event?.State ?? doseEvent.State,
            transition.Reason);
    }
}
