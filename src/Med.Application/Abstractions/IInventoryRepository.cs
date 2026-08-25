using Med.Domain.Entities;

namespace Med.Application.Abstractions;

public interface IInventoryRepository
{
    Task<Inventory?> GetByMedicationAsync(
        Guid medicationId,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(Inventory inventory, CancellationToken cancellationToken = default);
}
