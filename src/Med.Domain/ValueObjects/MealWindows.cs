using Med.Domain.Enums;

namespace Med.Domain.ValueObjects;

/// <summary>Локальные «настенные» окна приёмов пищи в таймзоне профиля.</summary>
public sealed record MealWindows(TimeOnly Breakfast, TimeOnly Lunch, TimeOnly Dinner)
{
    public TimeOnly For(MealKind kind) => kind switch
    {
        MealKind.Breakfast => Breakfast,
        MealKind.Lunch => Lunch,
        MealKind.Dinner => Dinner,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public MealWindows WithBreakfast(TimeOnly breakfast) => this with { Breakfast = breakfast };

    public MealWindows WithLunch(TimeOnly lunch) => this with { Lunch = lunch };

    public MealWindows WithDinner(TimeOnly dinner) => this with { Dinner = dinner };
}
