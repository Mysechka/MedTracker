using Med.Application.Abstractions;
using Med.Domain.Enums;

namespace Med.Application.UseCases;

public sealed class ConfirmDoseUseCase(IDoseTransitionService transitions)
{
    public Task<DoseTransitionResult> ExecuteAsync(
        Guid doseEventId,
        DoseEventSource source = DoseEventSource.App,
        DateTimeOffset? takenAt = null,
        CancellationToken cancellationToken = default) =>
        transitions.ConfirmAsync(doseEventId, source, takenAt, cancellationToken);
}
