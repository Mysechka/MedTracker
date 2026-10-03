namespace Med.Application.Abstractions;

/// <summary>Активная сессия (Supabase Auth или локальная).</summary>
public sealed record AuthSession(
    Guid UserId,
    string Email,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt,
    string? Username = null,
    bool IsLocalOnly = false);
