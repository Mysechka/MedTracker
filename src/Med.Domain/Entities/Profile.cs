using Med.Domain.ValueObjects;

namespace Med.Domain.Entities;

/// <summary>Профиль пользователя. Идентичность = auth.uid().</summary>
public sealed record Profile(
    Guid UserId,
    string Username,
    string TimeZoneId,
    MealWindows Meals,
    TimeSpan ConfirmationWindow)
{
    public static readonly TimeSpan DefaultConfirmationWindow = TimeSpan.FromHours(3);

    public static Profile Create(
        Guid userId,
        string username,
        string timeZoneId,
        MealWindows meals,
        TimeSpan? confirmationWindow = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);
        _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);

        TimeSpan window = confirmationWindow ?? DefaultConfirmationWindow;
        if (window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(confirmationWindow), "Окно подтверждения должно быть > 0.");
        }

        return new Profile(userId, username.Trim(), timeZoneId, meals, window);
    }

    public Profile WithUsername(string username)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        return this with { Username = username.Trim() };
    }

    public Profile WithTimeZoneId(string timeZoneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);
        _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        return this with { TimeZoneId = timeZoneId };
    }

    public Profile WithMeals(MealWindows meals) => this with { Meals = meals };

    public Profile WithConfirmationWindow(TimeSpan window)
    {
        if (window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(window), "Окно подтверждения должно быть > 0.");
        }

        return this with { ConfirmationWindow = window };
    }

    public TimeZoneInfo ResolveTimeZone() => TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
}
