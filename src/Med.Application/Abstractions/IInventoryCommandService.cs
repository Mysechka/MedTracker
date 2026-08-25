namespace Med.Application.Abstractions;

/// <summary>Ответ RPC restock_inventory.</summary>
public sealed record InventoryCommandResult(
    string Outcome,
    Guid? TransactionId = null,
    decimal? QuantityOnHand = null,
    string? Reason = null);

public interface IInventoryCommandService
{
    Task<InventoryCommandResult> RestockAsync(
        Guid medicationId,
        decimal amount,
        string? note = null,
        CancellationToken cancellationToken = default);
}
