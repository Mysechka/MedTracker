using Med.Application.Abstractions;
using Med.Domain.Entities;
using Med.Infrastructure.LocalStorage;
using Supabase.Postgrest.Responses;

namespace Med.Infrastructure.Supabase.Services;

public sealed class InventoryCommandService(
    ISupabaseClientAccessor accessor,
    RepositoryModeProvider? modeProvider = null,
    LocalInventoryRepository? localInventory = null,
    IAuthService? auth = null) : IInventoryCommandService
{
    public async Task<InventoryCommandResult> RestockAsync(
        Guid medicationId,
        decimal amount,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (modeProvider?.Mode == RepositoryMode.Local && localInventory is not null)
        {
            if (amount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount), "Количество пополнения должно быть > 0.");
            }

            Inventory? existing = await localInventory.GetByMedicationAsync(medicationId, cancellationToken).ConfigureAwait(false);
            Guid userId = existing?.UserId ?? auth?.CurrentUserId ?? Guid.Empty;
            Inventory updated = Inventory.Create(
                existing?.Id ?? Guid.NewGuid(),
                userId,
                medicationId,
                (existing?.QuantityOnHand ?? 0m) + amount,
                existing?.LowStockThreshold ?? 0m);

            await localInventory.UpsertAsync(updated, cancellationToken).ConfigureAwait(false);
            return new InventoryCommandResult("Applied", Guid.NewGuid(), updated.QuantityOnHand, Reason: null);
        }

        global::Supabase.Client client = await accessor.GetClientAsync(cancellationToken).ConfigureAwait(false);

        var parameters = new Dictionary<string, object?>
        {
            ["p_medication_id"] = medicationId,
            ["p_amount"] = amount,
            ["p_note"] = note,
        };

        BaseResponse response = await client.Rpc("restock_inventory", parameters).ConfigureAwait(false);
        return JsonRpcParser.ParseInventoryCommand(response.Content ?? "{}");
    }
}
