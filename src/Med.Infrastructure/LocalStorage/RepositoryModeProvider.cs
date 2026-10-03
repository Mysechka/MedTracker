namespace Med.Infrastructure.LocalStorage;

public sealed class RepositoryModeProvider
{
    public RepositoryMode Mode { get; set; } = RepositoryMode.Local;

    public bool IsLocalOnly => Mode == RepositoryMode.Local;
}
