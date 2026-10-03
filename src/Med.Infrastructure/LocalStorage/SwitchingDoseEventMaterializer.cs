using Med.Application.Abstractions;

namespace Med.Infrastructure.LocalStorage;

public sealed class SwitchingDoseEventMaterializer : IDoseEventMaterializer
{
    private readonly LocalDoseEventMaterializer _local;
    private readonly IDoseEventMaterializer _cloud;
    private readonly RepositoryModeProvider _modeProvider;

    public SwitchingDoseEventMaterializer(
        LocalDoseEventMaterializer local,
        IDoseEventMaterializer cloud,
        RepositoryModeProvider modeProvider)
    {
        _local = local;
        _cloud = cloud;
        _modeProvider = modeProvider;
    }

    public Task<int> MaterializeAsync(int horizonDays = 14, CancellationToken cancellationToken = default)
    {
        return _modeProvider.Mode == RepositoryMode.Local
            ? _local.MaterializeAsync(horizonDays, cancellationToken)
            : _cloud.MaterializeAsync(horizonDays, cancellationToken);
    }
}
