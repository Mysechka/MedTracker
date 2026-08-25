namespace Med.Application.Abstractions;

/// <summary>Активная сессия Supabase Auth.</summary>
public sealed record AuthSession(
    Guid UserId,
    string Email,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt);
