using Med.Domain.Enums;

namespace Med.Domain.Entities;

/// <summary>Append-only запись журнала остатков. Удалять прошлые строки нельзя.</summary>
public sealed record InventoryTransaction(
    Guid Id,
    Guid InventoryId,
    Guid MedicationId,
    Guid? DoseEventId,
    InventoryTransactionKind Kind,
    decimal Amount,
    DateTimeOffset CreatedAt,
    string? Note)
{
    public static InventoryTransaction Debit(
        Guid id,
        Guid inventoryId,
        Guid medicationId,
        Guid doseEventId,
        decimal amount,
        DateTimeOffset createdAtUtc) =>
        Create(id, inventoryId, medicationId, doseEventId, InventoryTransactionKind.Debit, amount, createdAtUtc, null);

    public static InventoryTransaction Credit(
        Guid id,
        Guid inventoryId,
        Guid medicationId,
        Guid doseEventId,
        decimal amount,
        DateTimeOffset createdAtUtc) =>
        Create(id, inventoryId, medicationId, doseEventId, InventoryTransactionKind.Credit, amount, createdAtUtc, "undo");

    public static InventoryTransaction Restock(
        Guid id,
        Guid inventoryId,
        Guid medicationId,
        decimal amount,
        DateTimeOffset createdAtUtc,
        string? note = null) =>
        Create(id, inventoryId, medicationId, doseEventId: null, InventoryTransactionKind.Restock, amount, createdAtUtc, note);

    private static InventoryTransaction Create(
        Guid id,
        Guid inventoryId,
        Guid medicationId,
        Guid? doseEventId,
        InventoryTransactionKind kind,
        decimal amount,
        DateTimeOffset createdAtUtc,
        string? note)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Сумма транзакции должна быть > 0.");
        }

        if (createdAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("CreatedAt хранится в UTC.", nameof(createdAtUtc));
        }

        return new InventoryTransaction(id, inventoryId, medicationId, doseEventId, kind, amount, createdAtUtc, note);
    }
}
