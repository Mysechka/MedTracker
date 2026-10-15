using System.Globalization;
using Med.Application.Abstractions;
using Med.Domain.Entities;
using Med.Domain.Enums;
using Microsoft.Data.Sqlite;

namespace Med.Infrastructure.LocalStorage;

public sealed class LocalDoseEventRepository(LocalDatabase db, IAuthService? auth = null) : IDoseEventRepository
{
    private const string SelectColumns =
        "SELECT id, course_id, schedule_id, scheduled_at, local_date, state, taken_at, source, dedupe_key FROM dose_events";

    public Task<IReadOnlyList<DoseEvent>> ListForLocalDateAsync(
        DateOnly localDate,
        CancellationToken cancellationToken = default) =>
        QueryListAsync(
            $"{SelectColumns} WHERE local_date = @p ORDER BY scheduled_at ASC;",
            ("@p", localDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            cancellationToken);

    public Task<IReadOnlyList<DoseEvent>> ListByScheduleAsync(
        Guid scheduleId,
        CancellationToken cancellationToken = default) =>
        QueryListAsync(
            $"{SelectColumns} WHERE schedule_id = @p ORDER BY scheduled_at ASC;",
            ("@p", scheduleId.ToString()),
            cancellationToken);

    public Task<IReadOnlyList<DoseEvent>> ListAllAsync(CancellationToken cancellationToken = default) =>
        QueryListAsync($"{SelectColumns} ORDER BY scheduled_at ASC;", null, cancellationToken);

    public async Task<DoseEvent?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var conn = db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"{SelectColumns} WHERE id = @id LIMIT 1;";
        cmd.Parameters.AddWithValue("@id", id.ToString());

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadDoseEvent(reader) : null;
    }

    public Task UpsertManyAsync(
        IReadOnlyList<DoseEvent> events,
        CancellationToken cancellationToken = default) =>
        ExecuteBatchWriteAsync(
            events,
            """
            INSERT INTO dose_events (id, user_id, course_id, schedule_id, scheduled_at, local_date, state, taken_at, source, dedupe_key, updated_at)
            VALUES (@id, @user_id, @course_id, @schedule_id, @scheduled_at, @local_date, @state, @taken_at, @source, @dedupe_key, datetime('now'))
            ON CONFLICT(dedupe_key) DO UPDATE SET
                state = excluded.state,
                taken_at = excluded.taken_at,
                source = excluded.source,
                updated_at = datetime('now');
            """,
            cancellationToken);

    public Task InsertMissingAsync(
        IReadOnlyList<DoseEvent> events,
        CancellationToken cancellationToken = default) =>
        ExecuteBatchWriteAsync(
            events,
            """
            INSERT INTO dose_events (id, user_id, course_id, schedule_id, scheduled_at, local_date, state, taken_at, source, dedupe_key, updated_at)
            VALUES (@id, @user_id, @course_id, @schedule_id, @scheduled_at, @local_date, @state, @taken_at, @source, @dedupe_key, datetime('now'))
            ON CONFLICT(dedupe_key) DO NOTHING;
            """,
            cancellationToken);

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

        using var conn = db.CreateConnection();
        using var cmd = conn.CreateCommand();

        string fromDateStr = fromLocalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string courseParams = string.Join(",", courseIds.Select((_, i) => $"@c{i}"));
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
                if (!keepDedupeKeys.Contains(reader.GetString(1)))
                {
                    idsToDelete.Add(reader.GetString(0));
                }
            }
        }

        if (idsToDelete.Count == 0)
        {
            return;
        }

        using var transaction = conn.BeginTransaction();
        using var delCmd = conn.CreateCommand();
        delCmd.Transaction = transaction;
        delCmd.CommandText = "DELETE FROM dose_events WHERE id = @id;";
        var idParam = delCmd.Parameters.Add("@id", SqliteType.Text);
        delCmd.Prepare();

        foreach (string id in idsToDelete)
        {
            idParam.Value = id;
            await delCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        transaction.Commit();
    }

    private async Task<IReadOnlyList<DoseEvent>> QueryListAsync(
        string sql,
        (string Name, object Value)? param,
        CancellationToken cancellationToken)
    {
        using var conn = db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        if (param is { } p)
        {
            cmd.Parameters.AddWithValue(p.Name, p.Value);
        }

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<DoseEvent>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(ReadDoseEvent(reader));
        }

        return result;
    }

    private async Task ExecuteBatchWriteAsync(
        IReadOnlyList<DoseEvent> events,
        string sql,
        CancellationToken cancellationToken)
    {
        if (events.Count == 0)
        {
            return;
        }

        string userIdStr = (auth?.CurrentUserId ?? Guid.Empty).ToString();

        using var conn = db.CreateConnection();
        using var transaction = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;

        var pId = cmd.Parameters.Add("@id", SqliteType.Text);
        var pUserId = cmd.Parameters.Add("@user_id", SqliteType.Text);
        var pCourseId = cmd.Parameters.Add("@course_id", SqliteType.Text);
        var pScheduleId = cmd.Parameters.Add("@schedule_id", SqliteType.Text);
        var pScheduledAt = cmd.Parameters.Add("@scheduled_at", SqliteType.Text);
        var pLocalDate = cmd.Parameters.Add("@local_date", SqliteType.Text);
        var pState = cmd.Parameters.Add("@state", SqliteType.Text);
        var pTakenAt = cmd.Parameters.Add("@taken_at", SqliteType.Text);
        var pSource = cmd.Parameters.Add("@source", SqliteType.Text);
        var pDedupeKey = cmd.Parameters.Add("@dedupe_key", SqliteType.Text);
        pUserId.Value = userIdStr;
        cmd.Prepare();

        foreach (DoseEvent e in events)
        {
            pId.Value = e.Id.ToString();
            pCourseId.Value = e.CourseId.ToString();
            pScheduleId.Value = e.ScheduleId.ToString();
            pScheduledAt.Value = e.ScheduledAt.ToString("O", CultureInfo.InvariantCulture);
            pLocalDate.Value = e.LocalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            pState.Value = e.State.ToString();
            pTakenAt.Value = (object?)e.TakenAt?.ToString("O", CultureInfo.InvariantCulture) ?? DBNull.Value;
            pSource.Value = (object?)e.Source?.ToString() ?? DBNull.Value;
            pDedupeKey.Value = e.DedupeKey;

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        transaction.Commit();
    }

    private static DoseEvent ReadDoseEvent(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        Guid.Parse(reader.GetString(1)),
        Guid.Parse(reader.GetString(2)),
        DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        DateOnly.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
        Enum.Parse<DoseEventState>(reader.GetString(5), ignoreCase: true),
        reader.IsDBNull(6) ? null : DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        reader.IsDBNull(7) ? null : Enum.Parse<DoseEventSource>(reader.GetString(7), ignoreCase: true),
        reader.GetString(8));
}
