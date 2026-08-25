using Med.Domain.Enums;

namespace Med.Domain.ValueObjects;

public static class WeekDaysExtensions
{
    public static WeekDays ToWeekDay(this DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => WeekDays.Monday,
        DayOfWeek.Tuesday => WeekDays.Tuesday,
        DayOfWeek.Wednesday => WeekDays.Wednesday,
        DayOfWeek.Thursday => WeekDays.Thursday,
        DayOfWeek.Friday => WeekDays.Friday,
        DayOfWeek.Saturday => WeekDays.Saturday,
        DayOfWeek.Sunday => WeekDays.Sunday,
        _ => WeekDays.None,
    };

    public static bool Includes(this WeekDays days, DayOfWeek day) =>
        days != WeekDays.None && days.HasFlag(day.ToWeekDay());
}
