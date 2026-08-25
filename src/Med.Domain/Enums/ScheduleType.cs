namespace Med.Domain.Enums;

public enum ScheduleType
{
    FixedTimes = 0,
    Interval = 1,
    MealRelative = 2,
    /// <summary>Не материализуется заранее — событие создаётся в момент приёма.</summary>
    AsNeeded = 3,
}
