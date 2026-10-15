using Med.Application.Abstractions;
using Med.Domain.Abstractions;
using Med.Domain.Entities;
using Med.Domain.Enums;
using Med.Domain.State;

namespace Med.Infrastructure.LocalStorage;

public sealed class LocalDoseTransitionService(
    IDoseEventRepository doseEvents,
    ISystemClock? clock = null,
    IInventoryRepository? inventory = null,
    ICourseRepository? courses = null,
    IScheduleRepository? schedules = null) : IDoseTransitionService
{
    private readonly ISystemClock _clock = clock ?? DefaultSystemClock.Instance;

    private sealed class DefaultSystemClock : ISystemClock
    {
        public static readonly DefaultSystemClock Instance = new();
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }

    public async Task<DoseTransitionResult> ConfirmAsync(
        Guid doseEventId,
        DoseEventSource source,
        DateTimeOffset? takenAt = null,
        CancellationToken cancellationToken = default)
    {
        DoseEvent? doseEvent = await doseEvents.GetAsync(doseEventId, cancellationToken).ConfigureAwait(false);
        if (doseEvent is null)
        {
            return new DoseTransitionResult("Rejected", doseEventId, Reason: "Событие не найдено");
        }

        if (doseEvent.State == DoseEventState.Skipped && source == DoseEventSource.App)
        {
            doseEvent = doseEvent with { State = DoseEventState.Scheduled, TakenAt = null, Source = null };
        }

        DateTimeOffset takenUtc = (takenAt ?? _clock.UtcNow).ToUniversalTime();
        TransitionResult transition = DoseEventTransitions.MarkTaken(doseEvent, takenUtc, source);
        decimal? updatedQty = null;

        if (transition.IsApplied && transition.Event is not null)
        {
            await doseEvents.UpsertManyAsync([transition.Event], cancellationToken).ConfigureAwait(false);
            updatedQty = await TryAdjustInventoryAsync(transition.Event, isDebit: true, cancellationToken).ConfigureAwait(false);
        }

        return new DoseTransitionResult(
            transition.Outcome.ToString(),
            doseEventId,
            transition.Event?.State ?? doseEvent.State,
            transition.Reason,
            QuantityOnHand: updatedQty);
    }

    public async Task<DoseTransitionResult> SkipAsync(
        Guid doseEventId,
        DoseEventSource source,
        DateTimeOffset? skippedAt = null,
        CancellationToken cancellationToken = default)
    {
        DoseEvent? doseEvent = await doseEvents.GetAsync(doseEventId, cancellationToken).ConfigureAwait(false);
        if (doseEvent is null)
        {
            return new DoseTransitionResult("Rejected", doseEventId, Reason: "Событие не найдено");
        }

        DateTimeOffset skippedUtc = (skippedAt ?? _clock.UtcNow).ToUniversalTime();
        TransitionResult transition = DoseEventTransitions.MarkSkipped(doseEvent, skippedUtc, source);
        if (transition.IsApplied && transition.Event is not null)
        {
            await doseEvents.UpsertManyAsync([transition.Event], cancellationToken).ConfigureAwait(false);
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
        DoseEvent? doseEvent = await doseEvents.GetAsync(doseEventId, cancellationToken).ConfigureAwait(false);
        if (doseEvent is null)
        {
            return new DoseTransitionResult("Rejected", doseEventId, Reason: "Событие не найдено");
        }

        bool wasTaken = doseEvent.State == DoseEventState.Taken;
        TransitionResult transition = DoseEventTransitions.Undo(doseEvent);
        decimal? updatedQty = null;

        if (transition.IsApplied && transition.Event is not null)
        {
            await doseEvents.UpsertManyAsync([transition.Event], cancellationToken).ConfigureAwait(false);
            if (wasTaken)
            {
                updatedQty = await TryAdjustInventoryAsync(transition.Event, isDebit: false, cancellationToken).ConfigureAwait(false);
            }
        }

        return new DoseTransitionResult(
            transition.Outcome.ToString(),
            doseEventId,
            transition.Event?.State ?? doseEvent.State,
            transition.Reason,
            QuantityOnHand: updatedQty);
    }

    private async Task<decimal?> TryAdjustInventoryAsync(
        DoseEvent doseEvent,
        bool isDebit,
        CancellationToken cancellationToken)
    {
        if (inventory is null || courses is null)
        {
            return null;
        }

        Course? course = await courses.GetAsync(doseEvent.CourseId, cancellationToken).ConfigureAwait(false);
        if (course is null)
        {
            return null;
        }

        Inventory? inv = await inventory.GetByMedicationAsync(course.MedicationId, cancellationToken).ConfigureAwait(false);
        if (inv is null)
        {
            return null;
        }

        Schedule? schedule = schedules is not null
            ? await schedules.GetAsync(doseEvent.ScheduleId, cancellationToken).ConfigureAwait(false)
            : null;
        decimal amount = schedule?.DoseAmount is > 0 ? schedule.DoseAmount : 1m;

        decimal newQty = isDebit
            ? Math.Max(0m, inv.QuantityOnHand - amount)
            : inv.QuantityOnHand + amount;

        if (newQty != inv.QuantityOnHand)
        {
            await inventory.UpsertAsync(inv with { QuantityOnHand = newQty }, cancellationToken).ConfigureAwait(false);
        }

        return newQty;
    }
}
