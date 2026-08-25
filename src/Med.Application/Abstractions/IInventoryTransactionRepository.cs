using Med.Domain.Entities;

namespace Med.Application.Abstractions;

/// <summary>Append-only журнал транзакций остатков.</summary>
public interface IInventoryTransactionRepository
{
    Task<IReadOnlyList<InventoryTransaction>> ListByMedicationAsync(
        Guid medicationId,
        CancellationToken cancellationToken = default);
}
