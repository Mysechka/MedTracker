using Med.Application.Abstractions;
using Supabase.Gotrue;
using Supabase.Gotrue.Interfaces;

namespace Med.Infrastructure.Supabase.Auth;

public sealed class SupabaseAuthService : IAuthService
{
    private readonly ISupabaseClientAccessor _accessor;
    private readonly SemaphoreSlim _listenerLock = new(1, 1);
    private bool _listenersAttached;
    private AuthSession? _cachedSession;

    public SupabaseAuthService(ISupabaseClientAccessor accessor)
    {
        _accessor = accessor;
    }

    public AuthSession? CurrentSession => _cachedSession;

    public Guid? CurrentUserId => _cachedSession?.UserId;

    public event EventHandler<AuthSession?>? AuthStateChanged;

    public async Task<AuthSession> SignUpWithPasswordAsync(
        string email,
        string password,
        string? username = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        global::Supabase.Client client = await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);

        SignUpOptions? options = null;
        if (!string.IsNullOrWhiteSpace(username))
        {
            options = new SignUpOptions
            {
                Data = new Dictionary<string, object> { ["username"] = username.Trim() },
            };
        }

        Session session = await client.Auth.SignUp(email, password, options).ConfigureAwait(false)
            ?? throw new InvalidOperationException("SignUp не вернул сессию.");

        AuthSession mapped = MapSession(session);
        SetCachedSession(mapped);
        return mapped;
    }

    public async Task<AuthSession> SignInWithPasswordAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        global::Supabase.Client client = await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);

        Session session = await client.Auth.SignInWithPassword(email, password).ConfigureAwait(false)
            ?? throw new InvalidOperationException("SignIn не вернул сессию.");

        AuthSession mapped = MapSession(session);
        SetCachedSession(mapped);
        return mapped;
    }

    public async Task SendMagicLinkAsync(string email, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        global::Supabase.Client client = await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        await client.Auth.SendMagicLink(email).ConfigureAwait(false);
    }

    public async Task UpdatePasswordAsync(string newPassword, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        global::Supabase.Client client = await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        await client.Auth.Update(new UserAttributes { Password = newPassword }).ConfigureAwait(false);
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        global::Supabase.Client client = await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        await client.Auth.SignOut().ConfigureAwait(false);
        SetCachedSession(null);
    }

    private async Task<global::Supabase.Client> EnsureReadyAsync(CancellationToken cancellationToken)
    {
        global::Supabase.Client client = await _accessor.GetClientAsync(cancellationToken).ConfigureAwait(false);
        await EnsureListenersAsync(client).ConfigureAwait(false);
        RefreshCacheFromClient(client);
        return client;
    }

    private async Task EnsureListenersAsync(global::Supabase.Client client)
    {
        if (_listenersAttached)
        {
            return;
        }

        await _listenerLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_listenersAttached)
            {
                return;
            }

            client.Auth.AddStateChangedListener(OnAuthStateChanged);
            _listenersAttached = true;
        }
        finally
        {
            _listenerLock.Release();
        }
    }

    private void OnAuthStateChanged(IGotrueClient<User, Session> gotrueClient, Constants.AuthState state)
    {
        if (state is Constants.AuthState.SignedOut)
        {
            SetCachedSession(null);
            return;
        }

        _ = RefreshCacheAndNotifyAsync();
    }

    private async Task RefreshCacheAndNotifyAsync()
    {
        try
        {
            global::Supabase.Client client = await _accessor.GetClientAsync().ConfigureAwait(false);
            RefreshCacheFromClient(client);
        }
        catch (Exception)
        {
            // Нет сети / невалидный URL — не валим UI.
        }
    }

    private void RefreshCacheFromClient(global::Supabase.Client client)
    {
        Session? session = client.Auth.CurrentSession;
        SetCachedSession(session is null ? null : MapSession(session));
    }

    private void SetCachedSession(AuthSession? session)
    {
        if (_cachedSession?.UserId == session?.UserId
            && _cachedSession?.AccessToken == session?.AccessToken)
        {
            return;
        }

        _cachedSession = session;
        AuthStateChanged?.Invoke(this, session);
    }

    private static AuthSession MapSession(Session session)
    {
        User user = session.User ?? throw new InvalidOperationException("Сессия без пользователя.");
        if (!Guid.TryParse(user.Id, out Guid userId))
        {
            throw new InvalidOperationException($"Некорректный user id: {user.Id}.");
        }

        string email = user.Email ?? string.Empty;
        DateTimeOffset expiresAt = DateTimeOffset.UtcNow.AddSeconds(session.ExpiresIn);

        return new AuthSession(
            userId,
            email,
            session.AccessToken ?? string.Empty,
            session.RefreshToken ?? string.Empty,
            expiresAt);
    }
}
