using Med.Application.Abstractions;
using Med.Domain.Entities;
using Microsoft.Data.Sqlite;

namespace Med.Infrastructure.LocalStorage;

public sealed class LocalMedicationRepository : IMedicationRepository
{
    private readonly LocalDatabase _db;

    public LocalMedicationRepository(LocalDatabase db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<Medication>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, user_id, name, form, dosage, unit, barcode, notes FROM medications ORDER BY created_at ASC;";

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<Medication>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(ReadMedication(reader));
        }

        return result;
    }

    public async Task<Medication?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, user_id, name, form, dosage, unit, barcode, notes FROM medications WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id.ToString());

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return ReadMedication(reader);
    }

    public async Task UpsertAsync(Medication medication, CancellationToken cancellationToken = default)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO medications (id, user_id, name, form, dosage, unit, barcode, notes, updated_at)
            VALUES (@id, @user_id, @name, @form, @dosage, @unit, @barcode, @notes, datetime('now'))
            ON CONFLICT(id) DO UPDATE SET
                name = excluded.name,
                form = excluded.form,
                dosage = excluded.dosage,
                unit = excluded.unit,
                barcode = excluded.barcode,
                notes = excluded.notes,
                updated_at = datetime('now');
            """;

        cmd.Parameters.AddWithValue("@id", medication.Id.ToString());
        cmd.Parameters.AddWithValue("@user_id", medication.UserId.ToString());
        cmd.Parameters.AddWithValue("@name", medication.Name);
        cmd.Parameters.AddWithValue("@form", medication.Form);
        cmd.Parameters.AddWithValue("@dosage", medication.Dosage);
        cmd.Parameters.AddWithValue("@unit", medication.Unit);
        cmd.Parameters.AddWithValue("@barcode", (object?)medication.Barcode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@notes", (object?)medication.Notes ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM medications WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id.ToString());

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static Medication ReadMedication(SqliteDataReader reader)
    {
        Guid id = Guid.Parse(reader.GetString(0));
        Guid userId = Guid.Parse(reader.GetString(1));
        string name = reader.GetString(2);
        string form = reader.GetString(3);
        string dosage = reader.GetString(4);
        string unit = reader.GetString(5);
        string? barcode = reader.IsDBNull(6) ? null : reader.GetString(6);
        string? notes = reader.IsDBNull(7) ? null : reader.GetString(7);

        return Medication.Create(id, userId, name, form, dosage, unit, barcode, notes);
    }
}
