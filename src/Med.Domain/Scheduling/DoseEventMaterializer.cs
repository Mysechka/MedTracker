using Med.Domain.Entities;
using Med.Domain.Enums;
using Med.Domain.ValueObjects;

namespace Med.Domain.Scheduling;

/// <summary>
/// Идемпотентная материализация dose_events на горизонт вперёд.
/// AsNeeded не генерируется — событие создаётся в момент приёма.
/// </summary>
public static class DoseEventMaterializer
{
    public static readonly TimeSpan DefaultHorizon = TimeSpan.FromDays(14);

    public static IReadOnlyList<DoseEvent> Materialize(
        Course course,
        Schedule schedule,
        Profile profile,
        DateTimeOffset utcNow,
        TimeSpan? horizon = null,
        Func<Guid>? idFactory = null)
    {
        if (utcNow.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("utcNow должен быть UTC.", nameof(utcNow));
        }

        if (!course.IsActive || schedule.Type == ScheduleType.AsNeeded)
        {
            return Array.Empty<DoseEvent>();
        }

        TimeSpan window = horizon ?? DefaultHorizon;
        if (window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(horizon));
        }

        TimeZoneInfo tz = profile.ResolveTimeZone();
        DateTimeOffset horizonEnd = utcNow + window;

        DateOnly rangeStart = LocalTimeConverter.ToLocalDate(utcNow, tz);
        if (rangeStart < course.StartsOn)
        {
            rangeStart = course.StartsOn;
        }

        DateOnly rangeEnd = LocalTimeConverter.ToLocalDate(horizonEnd, tz);
        DateOnly courseEnd = course.EffectiveEndsOn;
        if (rangeEnd > courseEnd)
        {
            rangeEnd = courseEnd;
        }

        if (rangeEnd < rangeStart)
        {
            return Array.Empty<DoseEvent>();
        }

        Func<Guid> newId = idFactory ?? Guid.NewGuid;
        int estimatedDays = Math.Min(rangeEnd.DayNumber - rangeStart.DayNumber + 1, 32);
        List<DoseEvent> events = new(estimatedDays * Math.Max(1, schedule.FixedTimes.Count));

        switch (schedule.Type)
        {
            case ScheduleType.FixedTimes:
                AppendFixedTimes(events, course, schedule, tz, rangeStart, rangeEnd, utcNow, horizonEnd, newId);
                break;
            case ScheduleType.MealRelative:
                AppendMealRelative(events, course, schedule, profile, tz, rangeStart, rangeEnd, utcNow, horizonEnd, newId);
                break;
            case ScheduleType.Interval:
                AppendInterval(events, course, schedule, tz, utcNow, horizonEnd, newId);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(schedule), schedule.Type, null);
        }

        events.Sort(static (a, b) =>
        {
            int cmp = a.ScheduledAt.CompareTo(b.ScheduledAt);
            return cmp != 0 ? cmp : string.CompareOrdinal(a.DedupeKey, b.DedupeKey);
        });

        return events;
    }

    /// <summary>
    /// Пересчёт будущих незакрытых событий при сдвиге окон еды.
    /// Taken никогда не пересчитывается; метод только предлагает новые Scheduled
    /// для горизонта (вызывающая сторона сопоставляет по dedupe_key / удаляет лишние Scheduled).
    /// </summary>
    public static IReadOnlyList<DoseEvent> RematerializeFuture(
        Course course,
        Schedule schedule,
        Profile profile,
        DateTimeOffset utcNow,
        IReadOnlyCollection<DoseEvent> existing,
        TimeSpan? horizon = null,
        Func<Guid>? idFactory = null)
    {
        IReadOnlyList<DoseEvent> proposed = Materialize(course, schedule, profile, utcNow, horizon, idFactory);
        if (proposed.Count == 0 || existing.Count == 0)
        {
            return proposed;
        }

        HashSet<string>? takenKeys = null;
        foreach (DoseEvent e in existing)
        {
            if (e.State == DoseEventState.Taken)
            {
                (takenKeys ??= new HashSet<string>(StringComparer.Ordinal)).Add(e.DedupeKey);
            }
        }

        if (takenKeys is null)
        {
            return proposed;
        }

        List<DoseEvent> filtered = new(proposed.Count);
        for (int i = 0; i < proposed.Count; i++)
        {
            DoseEvent item = proposed[i];
            if (!takenKeys.Contains(item.DedupeKey))
            {
                filtered.Add(item);
            }
        }

        return filtered;
    }

    private static void AppendFixedTimes(
        List<DoseEvent> sink,
        Course course,
        Schedule schedule,
        TimeZoneInfo tz,
        DateOnly rangeStart,
        DateOnly rangeEnd,
        DateTimeOffset utcNow,
        DateTimeOffset horizonEnd,
        Func<Guid> newId)
    {
        for (DateOnly day = rangeStart; day <= rangeEnd; day = day.AddDays(1))
        {
            if (!IsDayAllowed(schedule, course.StartsOn, day))
            {
                continue;
            }

            foreach (TimeOnly time in schedule.FixedTimes)
            {
                TryAdd(sink, course, schedule, tz, day, time, utcNow, horizonEnd, newId);
            }
        }
    }

    private static void AppendMealRelative(
        List<DoseEvent> sink,
        Course course,
        Schedule schedule,
        Profile profile,
        TimeZoneInfo tz,
        DateOnly rangeStart,
        DateOnly rangeEnd,
        DateTimeOffset utcNow,
        DateTimeOffset horizonEnd,
        Func<Guid> newId)
    {
        if (schedule.MealKind is not { } mealKind)
        {
            throw new InvalidOperationException("MealRelative требует MealKind.");
        }

        TimeOnly mealTime = profile.Meals.For(mealKind);
        int offsetMinutes = ResolveMealOffset(schedule.MealRelation, schedule.OffsetMinutes);
        (int dayShift, TimeOnly doseTime) = ApplyOffset(mealTime, offsetMinutes);

        for (DateOnly day = rangeStart; day <= rangeEnd; day = day.AddDays(1))
        {
            if (!IsDayAllowed(schedule, course.StartsOn, day))
            {
                continue;
            }

            DateOnly localDay = dayShift == 0 ? day : day.AddDays(dayShift);
            // Фильтр дней и границ курса — по дню приёма пищи, не по сдвинутой дате.
            TryAdd(sink, course, schedule, tz, localDay, doseTime, utcNow, horizonEnd, newId, courseDayFilter: day);
        }
    }

    private static (int DayShift, TimeOnly Time) ApplyOffset(TimeOnly mealTime, int offsetMinutes)
    {
        long totalMinutes = mealTime.Hour * 60L + mealTime.Minute + offsetMinutes;
        long dayShift = (long)Math.Floor(totalMinutes / (24.0 * 60));
        int minutesInDay = (int)(totalMinutes - dayShift * 24 * 60);
        return ((int)dayShift, new TimeOnly(minutesInDay / 60, minutesInDay % 60));
    }

    private static void AppendInterval(
        List<DoseEvent> sink,
        Course course,
        Schedule schedule,
        TimeZoneInfo tz,
        DateTimeOffset utcNow,
        DateTimeOffset horizonEnd,
        Func<Guid> newId)
    {
        if (schedule.IntervalHours is not { } hours || schedule.IntervalAnchorTime is not { } anchor)
        {
            throw new InvalidOperationException("Interval требует IntervalHours и IntervalAnchorTime.");
        }

        DateTimeOffset? cursorUtc = LocalTimeConverter.ToUtcInstant(course.StartsOn, anchor, tz);
        for (int minute = 1; minute < 24 * 60 && cursorUtc is null; minute++)
        {
            cursorUtc = LocalTimeConverter.ToUtcInstant(course.StartsOn, anchor.AddMinutes(minute), tz);
        }

        if (cursorUtc is null)
        {
            return;
        }

        DateTimeOffset cursor = cursorUtc.Value;
        DateOnly courseEnd = course.EffectiveEndsOn;
        TimeSpan step = TimeSpan.FromHours(hours);

        if (cursor < utcNow)
        {
            long elapsedTicks = utcNow.Ticks - cursor.Ticks;
            long stepsToSkip = elapsedTicks / step.Ticks;
            cursor = cursor.AddTicks(stepsToSkip * step.Ticks);
            while (cursor < utcNow)
            {
                cursor = cursor.Add(step);
            }
        }

        const int maxIterations = 14 * 24 + 8;
        for (int i = 0; i < maxIterations && cursor <= horizonEnd; i++)
        {
            DateOnly localDate = LocalTimeConverter.ToLocalDate(cursor, tz);
            if (localDate > courseEnd)
            {
                break;
            }

            if (localDate >= course.StartsOn
                && IsDayAllowed(schedule, course.StartsOn, localDate)
                && cursor >= utcNow)
            {
                sink.Add(DoseEvent.CreateScheduled(newId(), course.Id, schedule.Id, cursor, localDate));
            }

            cursor = cursor.Add(step);
        }
    }

    private static void TryAdd(
        List<DoseEvent> sink,
        Course course,
        Schedule schedule,
        TimeZoneInfo tz,
        DateOnly localDate,
        TimeOnly localTime,
        DateTimeOffset utcNow,
        DateTimeOffset horizonEnd,
        Func<Guid> newId,
        DateOnly? courseDayFilter = null)
    {
        if (!course.ContainsDate(courseDayFilter ?? localDate))
        {
            return;
        }

        if (LocalTimeConverter.ToUtcInstant(localDate, localTime, tz) is { } utc
            && utc >= utcNow
            && utc <= horizonEnd)
        {
            sink.Add(DoseEvent.CreateScheduled(newId(), course.Id, schedule.Id, utc, localDate));
        }
    }

    private static bool IsDayAllowed(Schedule schedule, DateOnly courseStart, DateOnly day)
    {
        if (!schedule.DaysOfWeek.Includes(day.DayOfWeek))
        {
            return false;
        }

        if (schedule.EveryNDays is { } everyN)
        {
            int delta = day.DayNumber - courseStart.DayNumber;
            if (delta < 0 || delta % everyN != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static int ResolveMealOffset(MealRelation relation, int offsetMinutes) => relation switch
    {
        MealRelation.Before => -Math.Abs(offsetMinutes),
        MealRelation.After => Math.Abs(offsetMinutes),
        MealRelation.With => 0,
        MealRelation.Independent => offsetMinutes,
        _ => throw new ArgumentOutOfRangeException(nameof(relation)),
    };
}
