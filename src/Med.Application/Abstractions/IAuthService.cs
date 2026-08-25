namespace Med.Application.Abstractions;

/// <summary>Аутентификация: email+пароль и magic link.</summary>
public interface IAuthService
{
    AuthSession? CurrentSession { get; }

    Guid? CurrentUserId { get; }

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

    Task SignOutAsync(CancellationToken cancellationToken = default);
}
