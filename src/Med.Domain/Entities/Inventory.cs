namespace Med.Domain.Entities;

/// <summary>
/// Складской остаток лекарства. Защищён инвариантами от отрицательного остатка через with.
/// </summary>
public sealed record Inventory
{
    private readonly decimal _quantityOnHand;
    private readonly decimal _lowStockThreshold;

    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public Guid MedicationId { get; init; }

    public decimal QuantityOnHand
    {
        get => _quantityOnHand;
        init
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Остаток не может быть отрицательным.");
            }
            _quantityOnHand = value;
        }
    }

    public decimal LowStockThreshold
    {
        get => _lowStockThreshold;
        init
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Порог низкого остатка не может быть отрицательным.");
            }
            _lowStockThreshold = value;
        }
    }

    public Inventory(
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

        Id = id;
        UserId = userId;
        MedicationId = medicationId;
        QuantityOnHand = quantityOnHand;
        LowStockThreshold = lowStockThreshold;
    }

    public static Inventory Create(
        Guid id,
        Guid userId,
        Guid medicationId,
        decimal quantityOnHand,
        decimal lowStockThreshold) =>
        new(id, userId, medicationId, quantityOnHand, lowStockThreshold);

    public bool IsLow => QuantityOnHand <= LowStockThreshold;
}
