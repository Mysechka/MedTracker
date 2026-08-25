namespace Med.Domain.Enums;

/// <summary>
/// Дни недели, в которые действует расписание.
/// «Выбрать все» в UI = <see cref="All"/>.
/// </summary>
[Flags]
public enum WeekDays : byte
{
    None = 0,
    Monday = 1 << 0,
    Tuesday = 1 << 1,
    Wednesday = 1 << 2,
    Thursday = 1 << 3,
    Friday = 1 << 4,
    Saturday = 1 << 5,
    Sunday = 1 << 6,
    All = Monday | Tuesday | Wednesday | Thursday | Friday | Saturday | Sunday,
}
