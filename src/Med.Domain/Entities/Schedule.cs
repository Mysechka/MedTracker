using Med.Domain.Enums;

namespace Med.Domain.Entities;

/// <summary>Правило генерации приёмов внутри курса.</summary>
public sealed record Schedule(
    Guid Id,
    Guid CourseId,
    ScheduleType Type,
    WeekDays DaysOfWeek,
    decimal DoseAmount,
    IReadOnlyList<TimeOnly> FixedTimes,
    int? IntervalHours,
    TimeOnly? IntervalAnchorTime,
    int? EveryNDays,
    MealKind? MealKind,
    MealRelation MealRelation,
    int OffsetMinutes)
{
    public static Schedule CreateFixedTimes(
        Guid id,
        Guid courseId,
        WeekDays daysOfWeek,
        decimal doseAmount,
        IReadOnlyList<TimeOnly> times)
    {
        EnsureDose(doseAmount);
        EnsureDays(daysOfWeek);
        if (times is null || times.Count == 0)
        {
            throw new ArgumentException("FixedTimes требует хотя бы одно время.", nameof(times));
        }

        return new Schedule(
            id,
            courseId,
            ScheduleType.FixedTimes,
            daysOfWeek,
            doseAmount,
            times.OrderBy(t => t).ToArray(),
            IntervalHours: null,
            IntervalAnchorTime: null,
            EveryNDays: null,
            MealKind: null,
            MealRelation.Independent,
            OffsetMinutes: 0);
    }

    public static Schedule CreateInterval(
        Guid id,
        Guid courseId,
        WeekDays daysOfWeek,
        decimal doseAmount,
        int intervalHours,
        TimeOnly anchorTime,
        int? everyNDays = null)
    {
        EnsureDose(doseAmount);
        EnsureDays(daysOfWeek);
        if (intervalHours <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(intervalHours), "Интервал должен быть > 0 часов.");
        }

        if (everyNDays is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(everyNDays), "every_n_days должен быть > 0.");
        }

        return new Schedule(
            id,
            courseId,
            ScheduleType.Interval,
            daysOfWeek,
            doseAmount,
            FixedTimes: Array.Empty<TimeOnly>(),
            intervalHours,
            anchorTime,
            everyNDays,
            MealKind: null,
            MealRelation.Independent,
            OffsetMinutes: 0);
    }

    public static Schedule CreateMealRelative(
        Guid id,
        Guid courseId,
        WeekDays daysOfWeek,
        decimal doseAmount,
        MealKind mealKind,
        MealRelation mealRelation,
        int offsetMinutes)
    {
        EnsureDose(doseAmount);
        EnsureDays(daysOfWeek);

        return new Schedule(
            id,
            courseId,
            ScheduleType.MealRelative,
            daysOfWeek,
            doseAmount,
            FixedTimes: Array.Empty<TimeOnly>(),
            IntervalHours: null,
            IntervalAnchorTime: null,
            EveryNDays: null,
            mealKind,
            mealRelation,
            offsetMinutes);
    }

    public static Schedule CreateAsNeeded(
        Guid id,
        Guid courseId,
        decimal doseAmount)
    {
        EnsureDose(doseAmount);

        return new Schedule(
            id,
            courseId,
            ScheduleType.AsNeeded,
            WeekDays.All,
            doseAmount,
            FixedTimes: Array.Empty<TimeOnly>(),
            IntervalHours: null,
            IntervalAnchorTime: null,
            EveryNDays: null,
            MealKind: null,
            MealRelation.Independent,
            OffsetMinutes: 0);
    }

    private static void EnsureDose(decimal doseAmount)
    {
        if (doseAmount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(doseAmount), "Доза должна быть > 0.");
        }
    }

    private static void EnsureDays(WeekDays daysOfWeek)
    {
        if (daysOfWeek == WeekDays.None)
        {
            throw new ArgumentException("Нужно выбрать хотя бы один день недели (или все семь).", nameof(daysOfWeek));
        }
    }
}
