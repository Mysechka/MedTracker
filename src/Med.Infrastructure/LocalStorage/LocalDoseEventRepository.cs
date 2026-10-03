using System.Globalization;
using Med.Application.Abstractions;
using Med.Domain.Entities;
using Med.Domain.Enums;
using Microsoft.Data.Sqlite;

namespace Med.Infrastructure.LocalStorage;

public sealed class LocalDoseEventRepository : IDoseEventRepository
{
    private readonly LocalDatabase _db;
    private readonly IAuthService? _auth;

    public LocalDoseEventRepository(LocalDatabase db, IAuthService? auth = null)
    {
        _db = db;
        _auth = auth;
    }

    public async Task<IReadOnlyList<DoseEvent>> ListForLocalDateAsync(
        DateOnly localDate,
        CancellationToken cancellationToken = default)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, course_id, schedule_id, scheduled_at, local_date, state, taken_at, source, dedupe_key FROM dose_events WHERE local_date = @local_date ORDER BY scheduled_at ASC;";
        cmd.Parameters.AddWithValue("@local_date", localDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<DoseEvent>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(ReadDoseEvent(reader));
        }

        return result;
    }

    public async Task<IReadOnlyList<DoseEvent>> ListByScheduleAsync(
        Guid scheduleId,
        CancellationToken cancellationToken = default)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, course_id, schedule_id, scheduled_at, local_date, state, taken_at, source, dedupe_key FROM dose_events WHERE schedule_id = @schedule_id ORDER BY scheduled_at ASC;";
        cmd.Parameters.AddWithValue("@schedule_id", scheduleId.ToString());

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<DoseEvent>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(ReadDoseEvent(reader));
        }

        return result;
    }

    public async Task UpsertManyAsync(
        IReadOnlyList<DoseEvent> events,
        CancellationToken cancellationToken = default)
    {
        if (events.Count == 0)
        {
            return;
        }

        Guid userId = _auth?.CurrentUserId ?? Guid.Empty;

        using var conn = _db.CreateConnection();
        using var transaction = conn.BeginTransaction();

        foreach (DoseEvent e in events)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = """
                INSERT INTO dose_events (id, user_id, course_id, schedule_id, scheduled_at, local_date, state, taken_at, source, dedupe_key, updated_at)
                VALUES (@id, @user_id, @course_id, @schedule_id, @scheduled_at, @local_date, @state, @taken_at, @source, @dedupe_key, datetime('now'))
                ON CONFLICT(dedupe_key) DO UPDATE SET
                    state = excluded.state,
                    taken_at = excluded.taken_at,
                    source = excluded.source,
                    updated_at = datetime('now');
                """;

            cmd.Parameters.AddWithValue("@id", e.Id.ToString());
            cmd.Parameters.AddWithValue("@user_id", userId.ToString());
            cmd.Parameters.AddWithValue("@course_id", e.CourseId.ToString());
            cmd.Parameters.AddWithValue("@schedule_id", e.ScheduleId.ToString());
            cmd.Parameters.AddWithValue("@scheduled_at", e.ScheduledAt.ToString("O", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@local_date", e.LocalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@state", e.State.ToString());
            cmd.Parameters.AddWithValue("@taken_at", (object?)e.TakenAt?.ToString("O", CultureInfo.InvariantCulture) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@source", (object?)e.Source?.ToString() ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@dedupe_key", e.DedupeKey);

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        transaction.Commit();
    }

    public async Task InsertMissingAsync(
        IReadOnlyList<DoseEvent> events,
        CancellationToken cancellationToken = default)
    {
        if (events.Count == 0)
        {
            return;
        }

        Guid userId = _auth?.CurrentUserId ?? Guid.Empty;

        using var conn = _db.CreateConnection();
        using var transaction = conn.BeginTransaction();

        foreach (DoseEvent e in events)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = """
                INSERT INTO dose_events (id, user_id, course_id, schedule_id, scheduled_at, local_date, state, taken_at, source, dedupe_key, updated_at)
                VALUES (@id, @user_id, @course_id, @schedule_id, @scheduled_at, @local_date, @state, @taken_at, @source, @dedupe_key, datetime('now'))
                ON CONFLICT(dedupe_key) DO NOTHING;
                """;

            cmd.Parameters.AddWithValue("@id", e.Id.ToString());
            cmd.Parameters.AddWithValue("@user_id", userId.ToString());
            cmd.Parameters.AddWithValue("@course_id", e.CourseId.ToString());
            cmd.Parameters.AddWithValue("@schedule_id", e.ScheduleId.ToString());
            cmd.Parameters.AddWithValue("@scheduled_at", e.ScheduledAt.ToString("O", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@local_date", e.LocalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@state", e.State.ToString());
            cmd.Parameters.AddWithValue("@taken_at", (object?)e.TakenAt?.ToString("O", CultureInfo.InvariantCulture) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@source", (object?)e.Source?.ToString() ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@dedupe_key", e.DedupeKey);

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        transaction.Commit();
    }

    public async Task DeleteObsoleteScheduledAsync(
        IReadOnlyList<Guid> courseIds,
        DateOnly fromLocalDate,
        IReadOnlySet<string> keepDedupeKeys,
        CancellationToken cancellationToken = default)
    {
        if (courseIds.Count == 0)
        {
            return;
        }

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();

        string fromDateStr = fromLocalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var courseParams = string.Join(",", courseIds.Select((_, i) => $"@c{i}"));
        cmd.CommandText = $"SELECT id, dedupe_key FROM dose_events WHERE course_id IN ({courseParams}) AND local_date >= @fromDate AND state = 'Scheduled';";

        for (int i = 0; i < courseIds.Count; i++)
        {
            cmd.Parameters.AddWithValue($"@c{i}", courseIds[i].ToString());
        }
        cmd.Parameters.AddWithValue("@fromDate", fromDateStr);

        var idsToDelete = new List<string>();
        using (var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                string id = reader.GetString(0);
                string dedupeKey = reader.GetString(1);
                if (!keepDedupeKeys.Contains(dedupeKey))
                {
                    idsToDelete.Add(id);
                }
            }
        }

        if (idsToDelete.Count > 0)
        {
            using var delCmd = conn.CreateCommand();
            var idParams = string.Join(",", idsToDelete.Select((_, i) => $"@id{i}"));
            delCmd.CommandText = $"DELETE FROM dose_events WHERE id IN ({idParams});";
            for (int i = 0; i < idsToDelete.Count; i++)
            {
                delCmd.Parameters.AddWithValue($"@id{i}", idsToDelete[i]);
            }
            await delCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<DoseEvent?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, course_id, schedule_id, scheduled_at, local_date, state, taken_at, source, dedupe_key FROM dose_events WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id.ToString());

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return ReadDoseEvent(reader);
    }

    public async Task<IReadOnlyList<DoseEvent>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, course_id, schedule_id, scheduled_at, local_date, state, taken_at, source, dedupe_key FROM dose_events ORDER BY scheduled_at ASC;";

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<DoseEvent>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(ReadDoseEvent(reader));
        }

        return result;
    }

    private static DoseEvent ReadDoseEvent(SqliteDataReader reader)
    {
        Guid id = Guid.Parse(reader.GetString(0));
        Guid courseId = Guid.Parse(reader.GetString(1));
        Guid scheduleId = Guid.Parse(reader.GetString(2));
        DateTimeOffset scheduledAt = DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        DateOnly localDate = DateOnly.Parse(reader.GetString(4), CultureInfo.InvariantCulture);
        DoseEventState state = Enum.Parse<DoseEventState>(reader.GetString(5), ignoreCase: true);
        DateTimeOffset? takenAt = reader.IsDBNull(6) ? null : DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        DoseEventSource? source = reader.IsDBNull(7) ? null : Enum.Parse<DoseEventSource>(reader.GetString(7), ignoreCase: true);
        string dedupeKey = reader.GetString(8);

        return new DoseEvent(id, courseId, scheduleId, scheduledAt, localDate, state, takenAt, source, dedupeKey);
    }
}
