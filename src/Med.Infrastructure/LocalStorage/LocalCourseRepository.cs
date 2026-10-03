using System.Globalization;
using Med.Application.Abstractions;
using Med.Domain.Entities;
using Microsoft.Data.Sqlite;

namespace Med.Infrastructure.LocalStorage;

public sealed class LocalCourseRepository : ICourseRepository
{
    private readonly LocalDatabase _db;

    public LocalCourseRepository(LocalDatabase db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<Course>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, user_id, medication_id, starts_on, ends_on, duration_days, is_active, diagnosis_id FROM courses ORDER BY starts_on ASC;";

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<Course>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(ReadCourse(reader));
        }

        return result;
    }

    public async Task<Course?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, user_id, medication_id, starts_on, ends_on, duration_days, is_active, diagnosis_id FROM courses WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id.ToString());

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return ReadCourse(reader);
    }

    public async Task UpsertAsync(Course course, CancellationToken cancellationToken = default)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO courses (id, user_id, medication_id, starts_on, ends_on, duration_days, is_active, diagnosis_id, updated_at)
            VALUES (@id, @user_id, @medication_id, @starts_on, @ends_on, @duration_days, @is_active, @diagnosis_id, datetime('now'))
            ON CONFLICT(id) DO UPDATE SET
                starts_on = excluded.starts_on,
                ends_on = excluded.ends_on,
                duration_days = excluded.duration_days,
                is_active = excluded.is_active,
                diagnosis_id = excluded.diagnosis_id,
                updated_at = datetime('now');
            """;

        cmd.Parameters.AddWithValue("@id", course.Id.ToString());
        cmd.Parameters.AddWithValue("@user_id", course.UserId.ToString());
        cmd.Parameters.AddWithValue("@medication_id", course.MedicationId.ToString());
        cmd.Parameters.AddWithValue("@starts_on", course.StartsOn.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@ends_on", (object?)course.EndsOn?.ToString("O", CultureInfo.InvariantCulture) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@duration_days", (object?)course.DurationDays ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@is_active", course.IsActive ? 1 : 0);
        cmd.Parameters.AddWithValue("@diagnosis_id", (object?)course.DiagnosisId?.ToString() ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM courses WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id.ToString());

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static Course ReadCourse(SqliteDataReader reader)
    {
        Guid id = Guid.Parse(reader.GetString(0));
        Guid userId = Guid.Parse(reader.GetString(1));
        Guid medicationId = Guid.Parse(reader.GetString(2));
        DateOnly startsOn = DateOnly.Parse(reader.GetString(3), CultureInfo.InvariantCulture);
        DateOnly? endsOn = reader.IsDBNull(4) ? null : DateOnly.Parse(reader.GetString(4), CultureInfo.InvariantCulture);
        int? durationDays = reader.IsDBNull(5) ? null : reader.GetInt32(5);
        bool isActive = reader.GetInt32(6) == 1;
        Guid? diagnosisId = reader.IsDBNull(7) ? null : Guid.Parse(reader.GetString(7));

        return Course.Create(id, userId, medicationId, startsOn, endsOn, durationDays, isActive, diagnosisId);
    }
}
