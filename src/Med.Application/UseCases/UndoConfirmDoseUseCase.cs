using Med.Application.Abstractions;

namespace Med.Application.UseCases;

public sealed class UndoConfirmDoseUseCase(IDoseTransitionService transitions)
{
    public Task<DoseTransitionResult> ExecuteAsync(
        Guid doseEventId,
        DateTimeOffset? undoneAt = null,
        CancellationToken cancellationToken = default) =>
        transitions.UndoConfirmAsync(doseEventId, undoneAt, cancellationToken);
}
