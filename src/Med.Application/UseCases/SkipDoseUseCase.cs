using Med.Application.Abstractions;
using Med.Domain.Enums;

namespace Med.Application.UseCases;

public sealed class SkipDoseUseCase(IDoseTransitionService transitions)
{
    public Task<DoseTransitionResult> ExecuteAsync(
        Guid doseEventId,
        DoseEventSource source = DoseEventSource.App,
        DateTimeOffset? skippedAt = null,
        CancellationToken cancellationToken = default) =>
        transitions.SkipAsync(doseEventId, source, skippedAt, cancellationToken);
}
