using Med.Domain.Entities;
using Med.Domain.Enums;

namespace Med.Domain.State;

public enum TransitionOutcome
{
    Applied = 0,
    /// <summary>Повтор терминального действия — без изменений (идемпотентный no-op).</summary>
    NoOp = 1,
    Rejected = 2,
}

public sealed record TransitionResult(
    TransitionOutcome Outcome,
    DoseEvent Event,
    string? Reason = null)
{
    public bool IsApplied => Outcome == TransitionOutcome.Applied;
}

/// <summary>
/// Чистые переходы машины состояний dose_event.
/// Первое терминальное состояние побеждает; повтор Taken/Skipped — no-op.
/// </summary>
public static class DoseEventTransitions
{
    private static readonly DoseEventState[] OpenStates =
    [
        DoseEventState.Scheduled,
        DoseEventState.Notified,
    ];

    public static TransitionResult MarkNotified(DoseEvent doseEvent, DoseEventSource source = DoseEventSource.System)
    {
        if (doseEvent.State == DoseEventState.Notified)
        {
            return new TransitionResult(TransitionOutcome.NoOp, doseEvent);
        }

        if (doseEvent.State != DoseEventState.Scheduled)
        {
            return Reject(doseEvent, $"Нельзя перевести {doseEvent.State} → Notified.");
        }

        return Apply(doseEvent with { State = DoseEventState.Notified, Source = source });
    }

    public static TransitionResult MarkTaken(
        DoseEvent doseEvent,
        DateTimeOffset takenAtUtc,
        DoseEventSource source)
    {
        EnsureUtc(takenAtUtc, nameof(takenAtUtc));

        if (doseEvent.State == DoseEventState.Taken)
        {
            return new TransitionResult(TransitionOutcome.NoOp, doseEvent);
        }

        if (!IsOpen(doseEvent.State))
        {
            return Reject(doseEvent, $"Нельзя подтвердить приём из состояния {doseEvent.State}.");
        }

        return Apply(doseEvent with
        {
            State = DoseEventState.Taken,
            TakenAt = takenAtUtc,
            Source = source,
        });
    }

    public static TransitionResult MarkSkipped(
        DoseEvent doseEvent,
        DateTimeOffset skippedAtUtc,
        DoseEventSource source)
    {
        EnsureUtc(skippedAtUtc, nameof(skippedAtUtc));

        if (doseEvent.State == DoseEventState.Skipped)
        {
            return new TransitionResult(TransitionOutcome.NoOp, doseEvent);
        }

        if (!IsOpen(doseEvent.State))
        {
            return Reject(doseEvent, $"Нельзя пропустить приём из состояния {doseEvent.State}.");
        }

        return Apply(doseEvent with
        {
            State = DoseEventState.Skipped,
            TakenAt = skippedAtUtc,
            Source = source,
        });
    }

    /// <summary>
    /// Notified → Missed после истечения окна подтверждения.
    /// Scheduled без уведомления также может стать Missed (окно от scheduled_at).
    /// </summary>
    public static TransitionResult MarkMissedIfExpired(
        DoseEvent doseEvent,
        DateTimeOffset utcNow,
        TimeSpan confirmationWindow)
    {
        EnsureUtc(utcNow, nameof(utcNow));
        if (confirmationWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(confirmationWindow));
        }

        if (doseEvent.State == DoseEventState.Missed)
        {
            return new TransitionResult(TransitionOutcome.NoOp, doseEvent);
        }

        if (!IsOpen(doseEvent.State))
        {
            return Reject(doseEvent, $"Нельзя пометить Missed из состояния {doseEvent.State}.");
        }

        DateTimeOffset deadline = doseEvent.ScheduledAt + confirmationWindow;
        if (utcNow < deadline)
        {
            return Reject(doseEvent, "Окно подтверждения ещё не истекло.");
        }

        return Apply(doseEvent with
        {
            State = DoseEventState.Missed,
            Source = DoseEventSource.System,
        });
    }

    public static TransitionResult MarkCancelled(DoseEvent doseEvent, DoseEventSource source = DoseEventSource.System)
    {
        if (doseEvent.State == DoseEventState.Cancelled)
        {
            return new TransitionResult(TransitionOutcome.NoOp, doseEvent);
        }

        if (doseEvent.IsTerminal)
        {
            return Reject(doseEvent, $"Нельзя отменить терминальное состояние {doseEvent.State}.");
        }

        return Apply(doseEvent with
        {
            State = DoseEventState.Cancelled,
            Source = source,
        });
    }

    /// <summary>
    /// Undo Taken → возвращает в Notified (если было уведомление) логически как открытое
    /// для повторного решения: Scheduled. Остаток компенсируется отдельно.
    /// </summary>
    public static TransitionResult UndoTaken(DoseEvent doseEvent)
    {
        if (doseEvent.State != DoseEventState.Taken)
        {
            return Reject(doseEvent, "Undo возможен только из Taken.");
        }

        return Apply(doseEvent with
        {
            State = DoseEventState.Scheduled,
            TakenAt = null,
            Source = null,
        });
    }

    private static bool IsOpen(DoseEventState state) => OpenStates.Contains(state);

    private static TransitionResult Apply(DoseEvent updated) =>
        new(TransitionOutcome.Applied, updated);

    private static TransitionResult Reject(DoseEvent current, string reason) =>
        new(TransitionOutcome.Rejected, current, reason);

    private static void EnsureUtc(DateTimeOffset value, string paramName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Ожидается UTC DateTimeOffset.", paramName);
        }
    }
}
