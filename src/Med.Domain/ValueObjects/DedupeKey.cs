using System.Globalization;

namespace Med.Domain.ValueObjects;

/// <summary>
/// Идемпотентный ключ материализации: schedule_id + scheduled_at.
/// Формат фиксирован — от него зависит unique-ограничение в Postgres.
/// </summary>
public readonly record struct DedupeKey
{
    public string Value { get; }

    private DedupeKey(string value) => Value = value;

    public static DedupeKey Create(Guid scheduleId, DateTimeOffset scheduledAtUtc)
    {
        if (scheduledAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("scheduled_at для dedupe_key должен быть в UTC.", nameof(scheduledAtUtc));
        }

        string id = scheduleId.ToString("N", CultureInfo.InvariantCulture);
        string at = scheduledAtUtc.UtcDateTime.ToString("o", CultureInfo.InvariantCulture);
        return new DedupeKey(string.Create(CultureInfo.InvariantCulture, $"{id}|{at}"));
    }

    public override string ToString() => Value;
}
