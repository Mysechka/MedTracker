using Med.Domain.Enums;
using Med.Domain.ValueObjects;

namespace Med.Domain.Entities;

public sealed record DoseEvent(
    Guid Id,
    Guid CourseId,
    Guid ScheduleId,
    DateTimeOffset ScheduledAt,
    DateOnly LocalDate,
    DoseEventState State,
    DateTimeOffset? TakenAt,
    DoseEventSource? Source,
    string DedupeKey)
{
    public bool IsTerminal => State is DoseEventState.Taken
        or DoseEventState.Skipped
        or DoseEventState.Missed
        or DoseEventState.Cancelled;

    public static DoseEvent CreateScheduled(
        Guid id,
        Guid courseId,
        Guid scheduleId,
        DateTimeOffset scheduledAtUtc,
        DateOnly localDate)
    {
        if (scheduledAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("ScheduledAt хранится в UTC.", nameof(scheduledAtUtc));
        }

        DedupeKey key = ValueObjects.DedupeKey.Create(scheduleId, scheduledAtUtc);
        return new DoseEvent(
            id,
            courseId,
            scheduleId,
            scheduledAtUtc,
            localDate,
            DoseEventState.Scheduled,
            TakenAt: null,
            Source: null,
            key.Value);
    }

    /// <summary>Создаёт событие AsNeeded в момент приёма (сразу Taken).</summary>
    public static DoseEvent CreateAsNeededTaken(
        Guid id,
        Guid courseId,
        Guid scheduleId,
        DateTimeOffset takenAtUtc,
        DateOnly localDate,
        DoseEventSource source)
    {
        if (takenAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("takenAt хранится в UTC.", nameof(takenAtUtc));
        }

        DedupeKey key = ValueObjects.DedupeKey.Create(scheduleId, takenAtUtc);
        return new DoseEvent(
            id,
            courseId,
            scheduleId,
            takenAtUtc,
            localDate,
            DoseEventState.Taken,
            takenAtUtc,
            source,
            key.Value);
    }
}
