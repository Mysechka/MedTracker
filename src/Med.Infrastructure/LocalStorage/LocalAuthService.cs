using System.Security.Cryptography;
using System.Text;
using Med.Application.Abstractions;
using Med.Domain.Entities;
using Med.Domain.ValueObjects;

namespace Med.Infrastructure.LocalStorage;

public sealed class LocalAuthService : IAuthService
{
    private readonly LocalProfileRepository _profileRepo;
    private readonly RepositoryModeProvider _modeProvider;
    private readonly LocalDatabase _db;
    private readonly IServiceProvider? _serviceProvider;
    private readonly IAuthService? _cloudAuth;
    private AuthSession? _currentSession;

    public LocalAuthService(
        LocalProfileRepository profileRepo,
        RepositoryModeProvider modeProvider,
        IServiceProvider? serviceProvider = null,
        IAuthService? cloudAuth = null)
        : this(profileRepo, modeProvider, profileRepo.Database, serviceProvider, cloudAuth)
    {
    }

    public LocalAuthService(
        LocalProfileRepository profileRepo,
        RepositoryModeProvider modeProvider,
        LocalDatabase db,
        IServiceProvider? serviceProvider = null,
        IAuthService? cloudAuth = null)
    {
        _profileRepo = profileRepo;
        _modeProvider = modeProvider;
        _db = db;
        _serviceProvider = serviceProvider;
        _cloudAuth = cloudAuth;

        InitializeSessionFromDb();
    }

    private void InitializeSessionFromDb()
    {
        try
        {
            string? activeIdStr = _db.GetSetting("active_local_user_id");
            if (string.Equals(activeIdStr, "logged_out", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string? repoMode = _db.GetSetting("repository_mode");
            string? migrationCompleted = _db.GetSetting("migration_completed");
            bool isCloud = string.Equals(repoMode, "Cloud", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(migrationCompleted, "true", StringComparison.OrdinalIgnoreCase);

            if (isCloud)
            {
                string? cloudEmail = _db.GetSetting("cloud_email");
                string? cloudUserIdStr = _db.GetSetting("cloud_user_id");
                string? cloudAccessToken = _db.GetSetting("cloud_access_token");
                string? cloudRefreshToken = _db.GetSetting("cloud_refresh_token");
                string? cloudUsername = _db.GetSetting("cloud_username");

                if (!string.IsNullOrEmpty(cloudEmail) && Guid.TryParse(cloudUserIdStr, out Guid cloudUserId))
                {
                    _modeProvider.Mode = RepositoryMode.Cloud;
                    _currentSession = new AuthSession(
                        cloudUserId,
                        cloudEmail,
                        cloudAccessToken ?? "cloud-token",
                        cloudRefreshToken ?? "cloud-refresh",
                        DateTimeOffset.UtcNow.AddDays(30),
                        string.IsNullOrWhiteSpace(cloudUsername) ? null : cloudUsername,
                        IsLocalOnly: false);

                    if (_cloudAuth is Supabase.Auth.SupabaseAuthService supabaseAuth && !string.IsNullOrEmpty(cloudAccessToken) && !string.IsNullOrEmpty(cloudRefreshToken))
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await supabaseAuth.RestoreSessionAsync(cloudAccessToken, cloudRefreshToken).ConfigureAwait(false);
                            }
                            catch
                            {
                                // Offline or expired token, cached session remains
                            }
                        });
                    }
                    return;
                }
            }

            Profile? profile = null;
            if (!string.IsNullOrEmpty(activeIdStr) && Guid.TryParse(activeIdStr, out Guid activeId))
            {
                profile = _profileRepo.GetById(activeId);
            }

            if (profile is null && activeIdStr is null)
            {
                profile = _profileRepo.GetCurrent();
                if (profile is not null)
                {
                    _db.SetSetting("active_local_user_id", profile.UserId.ToString());
                }
            }

            if (profile is not null)
            {
                _modeProvider.Mode = RepositoryMode.Local;
                _currentSession = new AuthSession(
                    profile.UserId,
                    Email: string.Empty,
                    AccessToken: "local-token",
                    RefreshToken: "local-refresh",
                    ExpiresAt: DateTimeOffset.MaxValue,
                    Username: profile.Username,
                    IsLocalOnly: true);
            }
        }
        catch
        {
            // Ignore during early initialization
        }
    }

    public AuthSession? CurrentSession => _currentSession;

    public Guid? CurrentUserId => _currentSession?.UserId;

    public bool IsLocalOnly => _currentSession?.IsLocalOnly ?? (_modeProvider.Mode == RepositoryMode.Local);

    public event EventHandler<AuthSession?>? AuthStateChanged;

    public static Guid GenerateDeterministicUserId(string username)
    {
        string normalized = (username ?? string.Empty).Trim().ToLowerInvariant();
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes("medtracker-local:" + normalized));
        byte[] guidBytes = new byte[16];
        Array.Copy(hash, guidBytes, 16);
        return new Guid(guidBytes);
    }

    public async Task<AuthSession> SignUpWithPasswordAsync(
        string email,
        string password,
        string? username = null,
        CancellationToken cancellationToken = default)
    {
        string name = !string.IsNullOrWhiteSpace(username)
            ? username.Trim()
            : (!string.IsNullOrWhiteSpace(email) ? email.Trim() : "Пользователь");

        Guid userId = GenerateDeterministicUserId(name);

        Profile profile = Profile.Create(
            userId,
            name,
            "Europe/Moscow",
            new MealWindows(new TimeOnly(8, 0), new TimeOnly(13, 0), new TimeOnly(19, 0)),
            TimeSpan.FromMinutes(30));

        await _profileRepo.UpdateAsync(profile, cancellationToken).ConfigureAwait(false);
        _db.SetSetting("active_local_user_id", userId.ToString());
        _db.SetSetting("repository_mode", "Local");
        _db.SetSetting("migration_completed", "false");

        _currentSession = new AuthSession(
            userId,
            Email: email ?? string.Empty,
            AccessToken: "local-token",
            RefreshToken: "local-refresh",
            ExpiresAt: DateTimeOffset.MaxValue,
            Username: name,
            IsLocalOnly: true);

        _modeProvider.Mode = RepositoryMode.Local;
        AuthStateChanged?.Invoke(this, _currentSession);
        return _currentSession;
    }

    private void SaveCloudSession(AuthSession session)
    {
        _currentSession = session with { IsLocalOnly = false };
        _modeProvider.Mode = RepositoryMode.Cloud;
        _db.SetSetting("repository_mode", "Cloud");
        _db.SetSetting("migration_completed", "true");
        _db.SetSetting("cloud_user_id", session.UserId.ToString());
        _db.SetSetting("cloud_email", session.Email);
        _db.SetSetting("cloud_access_token", session.AccessToken);
        _db.SetSetting("cloud_refresh_token", session.RefreshToken);
        if (!string.IsNullOrEmpty(session.Username))
        {
            _db.SetSetting("cloud_username", session.Username);
        }
    }

    public async Task<AuthSession> SignInWithPasswordAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (_cloudAuth is not null)
        {
            AuthSession session = await _cloudAuth.SignInWithPasswordAsync(email, password, cancellationToken).ConfigureAwait(false);
            SaveCloudSession(session);
            AuthStateChanged?.Invoke(this, _currentSession);
            return _currentSession!;
        }

        throw new NotSupportedException("В локальном режиме вход по паролю не поддерживается.");
    }

    public Task SendMagicLinkAsync(string email, CancellationToken cancellationToken = default)
    {
        if (_cloudAuth is not null)
        {
            return _cloudAuth.SendMagicLinkAsync(email, cancellationToken);
        }

        throw new NotSupportedException("В локальном режиме отправка magic link не поддерживается.");
    }

    public async Task UpdatePasswordAsync(string newPassword, CancellationToken cancellationToken = default)
    {
        if (_cloudAuth is not null)
        {
            await _cloudAuth.UpdatePasswordAsync(newPassword, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task UpdateEmailAsync(string newEmail, CancellationToken cancellationToken = default)
    {
        if (_cloudAuth is not null)
        {
            await _cloudAuth.UpdateEmailAsync(newEmail, cancellationToken).ConfigureAwait(false);
        }

        if (_currentSession is not null)
        {
            _currentSession = _currentSession with { Email = newEmail };
            if (!_currentSession.IsLocalOnly)
            {
                _db.SetSetting("cloud_email", newEmail);
            }
        }
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        if (_cloudAuth is not null)
        {
            try
            {
                await _cloudAuth.SignOutAsync(cancellationToken).ConfigureAwait(false);
            }
            catch { }
        }

        _db.SetSetting("active_local_user_id", "logged_out");
        _db.SetSetting("repository_mode", "Local");
        _db.SetSetting("migration_completed", "false");
        _db.SetSetting("cloud_user_id", "");
        _db.SetSetting("cloud_email", "");
        _db.SetSetting("cloud_access_token", "");
        _db.SetSetting("cloud_refresh_token", "");
        _db.SetSetting("cloud_username", "");
        _modeProvider.Mode = RepositoryMode.Local;
        _currentSession = null;
        AuthStateChanged?.Invoke(this, null);
    }

    public void UpdateSessionUsername(string username)
    {
        if (_currentSession is not null)
        {
            _currentSession = _currentSession with { Username = username };
            if (!_currentSession.IsLocalOnly)
            {
                _db.SetSetting("cloud_username", username);
            }
        }
    }

    public async Task MigrateToCloudAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        var migrationService = _serviceProvider?.GetService(typeof(DataMigrationService)) as DataMigrationService
            ?? throw new InvalidOperationException("Сервис миграции не настроен.");

        AuthSession cloudSession = await migrationService.MigrateAsync(
            email,
            password,
            onSessionObtained: session =>
            {
                SaveCloudSession(session);
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        SaveCloudSession(cloudSession);
        AuthStateChanged?.Invoke(this, _currentSession);
    }
}
