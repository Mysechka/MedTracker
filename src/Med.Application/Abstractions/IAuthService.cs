namespace Med.Application.Abstractions;

/// <summary>Аутентификация: email+пароль, локальный режим и magic link.</summary>
public interface IAuthService
{
    AuthSession? CurrentSession { get; }

    Guid? CurrentUserId { get; }

    bool IsLocalOnly => false;

    event EventHandler<AuthSession?>? AuthStateChanged;

    Task<AuthSession> SignUpWithPasswordAsync(
        string email,
        string password,
        string? username = null,
        CancellationToken cancellationToken = default);

    Task<AuthSession> SignInWithPasswordAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default);

    Task SendMagicLinkAsync(string email, CancellationToken cancellationToken = default);

    Task UpdatePasswordAsync(string newPassword, CancellationToken cancellationToken = default);

    Task UpdateEmailAsync(string newEmail, CancellationToken cancellationToken = default) => Task.CompletedTask;

    Task SignOutAsync(CancellationToken cancellationToken = default);

    Task MigrateToCloudAsync(string email, string password, CancellationToken cancellationToken = default) => Task.CompletedTask;

    void UpdateSessionUsername(string username) { }
}
