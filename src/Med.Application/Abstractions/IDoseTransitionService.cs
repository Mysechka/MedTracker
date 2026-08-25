using Med.Domain.Enums;

namespace Med.Application.Abstractions;

/// <summary>Ответ RPC confirm_dose / skip_dose / undo_confirm_dose.</summary>
public sealed record DoseTransitionResult(
    string Outcome,
    Guid DoseEventId,
    DoseEventState? State = null,
    string? Reason = null,
    Guid? TransactionId = null,
    decimal? QuantityOnHand = null);

/// <summary>Обёртка над RPC confirm_dose, skip_dose, undo_confirm_dose.</summary>
public interface IDoseTransitionService
{
    Task<DoseTransitionResult> ConfirmAsync(
        Guid doseEventId,
        DoseEventSource source,
        DateTimeOffset? takenAt = null,
        CancellationToken cancellationToken = default);

    Task<DoseTransitionResult> SkipAsync(
        Guid doseEventId,
        DoseEventSource source,
        DateTimeOffset? skippedAt = null,
        CancellationToken cancellationToken = default);

    Task<DoseTransitionResult> UndoConfirmAsync(
        Guid doseEventId,
        DateTimeOffset? undoneAt = null,
        CancellationToken cancellationToken = default);
}
