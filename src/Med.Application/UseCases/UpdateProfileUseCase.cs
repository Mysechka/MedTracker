using Med.Application.Abstractions;
using Med.Domain.Entities;
using Med.Domain.ValueObjects;

namespace Med.Application.UseCases;

public sealed class UpdateProfileUseCase(IProfileRepository profiles)
{
    public async Task<Profile> ExecuteAsync(
        string? username = null,
        string? timeZoneId = null,
        MealWindows? meals = null,
        TimeSpan? confirmationWindow = null,
        CancellationToken cancellationToken = default)
    {
        Profile current = await profiles.GetCurrentAsync(cancellationToken)
            ?? throw new InvalidOperationException("Профиль не найден.");

        Profile updated = current;

        if (username is not null)
        {
            updated = updated.WithUsername(username);
        }

        if (timeZoneId is not null)
        {
            updated = updated.WithTimeZoneId(timeZoneId);
        }

        if (meals is not null)
        {
            updated = updated.WithMeals(meals);
        }

        if (confirmationWindow is not null)
        {
            updated = updated.WithConfirmationWindow(confirmationWindow.Value);
        }

        await profiles.UpdateAsync(updated, cancellationToken);
        return updated;
    }
}
