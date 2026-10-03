using Med.Application.Abstractions;
using Med.Domain.Enums;

namespace Med.Infrastructure.LocalStorage;

public sealed class SwitchingDoseTransitionService : IDoseTransitionService
{
    private readonly LocalDoseTransitionService _local;
    private readonly IDoseTransitionService _cloud;
    private readonly RepositoryModeProvider _modeProvider;

    public SwitchingDoseTransitionService(
        LocalDoseTransitionService local,
        IDoseTransitionService cloud,
        RepositoryModeProvider modeProvider)
    {
        _local = local;
        _cloud = cloud;
        _modeProvider = modeProvider;
    }

    public Task<DoseTransitionResult> ConfirmAsync(Guid doseEventId, DoseEventSource source, DateTimeOffset? takenAt = null, CancellationToken cancellationToken = default)
        => _modeProvider.Mode == RepositoryMode.Local
            ? _local.ConfirmAsync(doseEventId, source, takenAt, cancellationToken)
            : _cloud.ConfirmAsync(doseEventId, source, takenAt, cancellationToken);

    public Task<DoseTransitionResult> SkipAsync(Guid doseEventId, DoseEventSource source, DateTimeOffset? skippedAt = null, CancellationToken cancellationToken = default)
        => _modeProvider.Mode == RepositoryMode.Local
            ? _local.SkipAsync(doseEventId, source, skippedAt, cancellationToken)
            : _cloud.SkipAsync(doseEventId, source, skippedAt, cancellationToken);

    public Task<DoseTransitionResult> UndoConfirmAsync(Guid doseEventId, DateTimeOffset? undoneAt = null, CancellationToken cancellationToken = default)
        => _modeProvider.Mode == RepositoryMode.Local
            ? _local.UndoConfirmAsync(doseEventId, undoneAt, cancellationToken)
            : _cloud.UndoConfirmAsync(doseEventId, undoneAt, cancellationToken);
}
