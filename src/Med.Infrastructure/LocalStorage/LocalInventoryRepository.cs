using Med.Application.Abstractions;
using Med.Domain.Entities;
using Microsoft.Data.Sqlite;

namespace Med.Infrastructure.LocalStorage;

public sealed class LocalInventoryRepository : IInventoryRepository
{
    private readonly LocalDatabase _db;

    public LocalInventoryRepository(LocalDatabase db)
    {
        _db = db;
    }

    public async Task<Inventory?> GetByMedicationAsync(Guid medicationId, CancellationToken cancellationToken = default)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, user_id, medication_id, quantity_on_hand, low_stock_threshold FROM inventory WHERE medication_id = @medication_id;";
        cmd.Parameters.AddWithValue("@medication_id", medicationId.ToString());

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        Guid id = Guid.Parse(reader.GetString(0));
        Guid userId = Guid.Parse(reader.GetString(1));
        Guid medId = Guid.Parse(reader.GetString(2));
        decimal qty = reader.GetDecimal(3);
        decimal threshold = reader.GetDecimal(4);

        return Inventory.Create(id, userId, medId, qty, threshold);
    }

    public async Task UpsertAsync(Inventory inventory, CancellationToken cancellationToken = default)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO inventory (id, user_id, medication_id, quantity_on_hand, low_stock_threshold, updated_at)
            VALUES (@id, @user_id, @medication_id, @quantity_on_hand, @low_stock_threshold, datetime('now'))
            ON CONFLICT(medication_id) DO UPDATE SET
                quantity_on_hand = excluded.quantity_on_hand,
                low_stock_threshold = excluded.low_stock_threshold,
                updated_at = datetime('now');
            """;

        cmd.Parameters.AddWithValue("@id", inventory.Id.ToString());
        cmd.Parameters.AddWithValue("@user_id", inventory.UserId.ToString());
        cmd.Parameters.AddWithValue("@medication_id", inventory.MedicationId.ToString());
        cmd.Parameters.AddWithValue("@quantity_on_hand", inventory.QuantityOnHand);
        cmd.Parameters.AddWithValue("@low_stock_threshold", inventory.LowStockThreshold);

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Inventory>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, user_id, medication_id, quantity_on_hand, low_stock_threshold FROM inventory;";

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<Inventory>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            Guid id = Guid.Parse(reader.GetString(0));
            Guid userId = Guid.Parse(reader.GetString(1));
            Guid medId = Guid.Parse(reader.GetString(2));
            decimal qty = reader.GetDecimal(3);
            decimal threshold = reader.GetDecimal(4);

            result.Add(Inventory.Create(id, userId, medId, qty, threshold));
        }

        return result;
    }
}
