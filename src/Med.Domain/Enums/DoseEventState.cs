namespace Med.Domain.Enums;

/// <summary>Жизненный цикл материализованного приёма.</summary>
public enum DoseEventState
{
    Scheduled = 0,
    Notified = 1,
    Taken = 2,
    Skipped = 3,
    Missed = 4,
    Cancelled = 5,
}
