using Med.Application.Abstractions;

namespace Med.Application.UseCases;

public sealed class RestockInventoryUseCase(IInventoryCommandService inventoryCommands)
{
    public Task<InventoryCommandResult> ExecuteAsync(
        Guid medicationId,
        decimal amount,
        string? note = null,
        CancellationToken cancellationToken = default) =>
        inventoryCommands.RestockAsync(medicationId, amount, note, cancellationToken);
}
