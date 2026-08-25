namespace Med.Domain.Entities;

public sealed record Inventory(
    Guid Id,
    Guid UserId,
    Guid MedicationId,
    decimal QuantityOnHand,
    decimal LowStockThreshold)
{
    public static Inventory Create(
        Guid id,
        Guid userId,
        Guid medicationId,
        decimal quantityOnHand,
        decimal lowStockThreshold)
    {
        if (quantityOnHand < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantityOnHand));
        }

        if (lowStockThreshold < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lowStockThreshold));
        }

        return new Inventory(id, userId, medicationId, quantityOnHand, lowStockThreshold);
    }

    public bool IsLow => QuantityOnHand <= LowStockThreshold;
}
