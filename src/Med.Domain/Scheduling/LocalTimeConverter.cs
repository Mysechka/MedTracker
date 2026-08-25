namespace Med.Domain.Scheduling;

/// <summary>
/// Конвертация локального «настенного» времени в UTC-инстант.
/// Невалидный час (весенний перевод) — пропускается (null).
/// Неоднозначный час (осенний возврат) — берётся более ранний инстант (больший offset / DST).
/// </summary>
public static class LocalTimeConverter
{
    public static DateTimeOffset? ToUtcInstant(DateOnly date, TimeOnly time, TimeZoneInfo timeZone)
    {
        DateTime local = date.ToDateTime(time, DateTimeKind.Unspecified);

        if (timeZone.IsInvalidTime(local))
        {
            return null;
        }

        if (timeZone.IsAmbiguousTime(local))
        {
            TimeSpan[] offsets = timeZone.GetAmbiguousTimeOffsets(local);
            TimeSpan earlierOffset = offsets[0] > offsets[1] ? offsets[0] : offsets[1];
            return new DateTimeOffset(local, earlierOffset).ToUniversalTime();
        }

        TimeSpan offset = timeZone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    public static DateOnly ToLocalDate(DateTimeOffset utcInstant, TimeZoneInfo timeZone)
    {
        DateTime local = TimeZoneInfo.ConvertTimeFromUtc(utcInstant.UtcDateTime, timeZone);
        return DateOnly.FromDateTime(local);
    }
}
