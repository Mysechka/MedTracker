using System.Globalization;
using Med.Application.Abstractions;
using Med.Domain.Entities;
using Med.Domain.Enums;
using Microsoft.Data.Sqlite;

namespace Med.Infrastructure.LocalStorage;

public sealed class LocalScheduleRepository : IScheduleRepository
{
    private readonly LocalDatabase _db;

    public LocalScheduleRepository(LocalDatabase db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<Schedule>> ListByCourseAsync(Guid courseId, CancellationToken cancellationToken = default)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, course_id, type, day_mon, day_tue, day_wed, day_thu, day_fri, day_sat, day_sun, dose_amount, fixed_times, interval_hours, interval_anchor_time, every_n_days, meal_kind, meal_relation, offset_minutes FROM schedules WHERE course_id = @course_id;";
        cmd.Parameters.AddWithValue("@course_id", courseId.ToString());

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<Schedule>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(ReadSchedule(reader));
        }

        return result;
    }

    public async Task<Schedule?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, course_id, type, day_mon, day_tue, day_wed, day_thu, day_fri, day_sat, day_sun, dose_amount, fixed_times, interval_hours, interval_anchor_time, every_n_days, meal_kind, meal_relation, offset_minutes FROM schedules WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id.ToString());

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return ReadSchedule(reader);
    }

    public async Task UpsertAsync(Schedule schedule, CancellationToken cancellationToken = default)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO schedules (
                id, course_id, type, day_mon, day_tue, day_wed, day_thu, day_fri, day_sat, day_sun,
                dose_amount, fixed_times, interval_hours, interval_anchor_time, every_n_days,
                meal_kind, meal_relation, offset_minutes, updated_at
            )
            VALUES (
                @id, @course_id, @type, @day_mon, @day_tue, @day_wed, @day_thu, @day_fri, @day_sat, @day_sun,
                @dose_amount, @fixed_times, @interval_hours, @interval_anchor_time, @every_n_days,
                @meal_kind, @meal_relation, @offset_minutes, datetime('now')
            )
            ON CONFLICT(id) DO UPDATE SET
                type = excluded.type,
                day_mon = excluded.day_mon,
                day_tue = excluded.day_tue,
                day_wed = excluded.day_wed,
                day_thu = excluded.day_thu,
                day_fri = excluded.day_fri,
                day_sat = excluded.day_sat,
                day_sun = excluded.day_sun,
                dose_amount = excluded.dose_amount,
                fixed_times = excluded.fixed_times,
                interval_hours = excluded.interval_hours,
                interval_anchor_time = excluded.interval_anchor_time,
                every_n_days = excluded.every_n_days,
                meal_kind = excluded.meal_kind,
                meal_relation = excluded.meal_relation,
                offset_minutes = excluded.offset_minutes,
                updated_at = datetime('now');
            """;

        cmd.Parameters.AddWithValue("@id", schedule.Id.ToString());
        cmd.Parameters.AddWithValue("@course_id", schedule.CourseId.ToString());
        cmd.Parameters.AddWithValue("@type", schedule.Type.ToString());
        cmd.Parameters.AddWithValue("@day_mon", schedule.DaysOfWeek.HasFlag(WeekDays.Monday) ? 1 : 0);
        cmd.Parameters.AddWithValue("@day_tue", schedule.DaysOfWeek.HasFlag(WeekDays.Tuesday) ? 1 : 0);
        cmd.Parameters.AddWithValue("@day_wed", schedule.DaysOfWeek.HasFlag(WeekDays.Wednesday) ? 1 : 0);
        cmd.Parameters.AddWithValue("@day_thu", schedule.DaysOfWeek.HasFlag(WeekDays.Thursday) ? 1 : 0);
        cmd.Parameters.AddWithValue("@day_fri", schedule.DaysOfWeek.HasFlag(WeekDays.Friday) ? 1 : 0);
        cmd.Parameters.AddWithValue("@day_sat", schedule.DaysOfWeek.HasFlag(WeekDays.Saturday) ? 1 : 0);
        cmd.Parameters.AddWithValue("@day_sun", schedule.DaysOfWeek.HasFlag(WeekDays.Sunday) ? 1 : 0);
        cmd.Parameters.AddWithValue("@dose_amount", schedule.DoseAmount);

        string? fixedTimesText = schedule.FixedTimes.Count > 0
            ? string.Join(",", schedule.FixedTimes.Select(t => t.ToString("HH:mm:ss", CultureInfo.InvariantCulture)))
            : null;
        cmd.Parameters.AddWithValue("@fixed_times", (object?)fixedTimesText ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@interval_hours", (object?)schedule.IntervalHours ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@interval_anchor_time", (object?)schedule.IntervalAnchorTime?.ToString("HH:mm:ss", CultureInfo.InvariantCulture) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@every_n_days", (object?)schedule.EveryNDays ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@meal_kind", (object?)schedule.MealKind?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@meal_relation", schedule.MealRelation.ToString());
        cmd.Parameters.AddWithValue("@offset_minutes", schedule.OffsetMinutes);

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM schedules WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id.ToString());

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static Schedule ReadSchedule(SqliteDataReader reader)
    {
        Guid id = Guid.Parse(reader.GetString(0));
        Guid courseId = Guid.Parse(reader.GetString(1));
        string typeStr = reader.GetString(2);
        ScheduleType type = Enum.Parse<ScheduleType>(typeStr, ignoreCase: true);

        WeekDays days = WeekDays.None;
        if (reader.GetInt32(3) == 1) days |= WeekDays.Monday;
        if (reader.GetInt32(4) == 1) days |= WeekDays.Tuesday;
        if (reader.GetInt32(5) == 1) days |= WeekDays.Wednesday;
        if (reader.GetInt32(6) == 1) days |= WeekDays.Thursday;
        if (reader.GetInt32(7) == 1) days |= WeekDays.Friday;
        if (reader.GetInt32(8) == 1) days |= WeekDays.Saturday;
        if (reader.GetInt32(9) == 1) days |= WeekDays.Sunday;

        decimal doseAmount = reader.GetDecimal(10);
        string? fixedTimesText = reader.IsDBNull(11) ? null : reader.GetString(11);
        int? intervalHours = reader.IsDBNull(12) ? null : reader.GetInt32(12);
        TimeOnly? intervalAnchor = reader.IsDBNull(13) ? null : TimeOnly.Parse(reader.GetString(13), CultureInfo.InvariantCulture);
        int? everyNDays = reader.IsDBNull(14) ? null : reader.GetInt32(14);
        string? mealKindStr = reader.IsDBNull(15) ? null : reader.GetString(15);
        string mealRelStr = reader.GetString(16);
        int offsetMinutes = reader.GetInt32(17);

        return type switch
        {
            ScheduleType.FixedTimes => Schedule.CreateFixedTimes(
                id,
                courseId,
                days,
                doseAmount,
                (fixedTimesText ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(s => TimeOnly.Parse(s, CultureInfo.InvariantCulture))
                    .ToArray()),
            ScheduleType.Interval => Schedule.CreateInterval(
                id,
                courseId,
                days,
                doseAmount,
                intervalHours ?? 1,
                intervalAnchor ?? TimeOnly.MinValue,
                everyNDays),
            ScheduleType.MealRelative => Schedule.CreateMealRelative(
                id,
                courseId,
                days,
                doseAmount,
                Enum.Parse<MealKind>(mealKindStr ?? "Breakfast", ignoreCase: true),
                Enum.Parse<MealRelation>(mealRelStr, ignoreCase: true),
                offsetMinutes),
            ScheduleType.AsNeeded => Schedule.CreateAsNeeded(id, courseId, doseAmount),
            _ => throw new InvalidOperationException($"Неизвестный тип расписания: {typeStr}")
        };
    }
}
