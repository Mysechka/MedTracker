using Med.Application.Abstractions;
using Supabase.Postgrest.Responses;

namespace Med.Infrastructure.Supabase.Services;

public sealed class InventoryCommandService : IInventoryCommandService
{
    private readonly ISupabaseClientAccessor _accessor;

    public InventoryCommandService(ISupabaseClientAccessor accessor)
    {
        _accessor = accessor;
    }

    public async Task<InventoryCommandResult> RestockAsync(
        Guid medicationId,
        decimal amount,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        global::Supabase.Client client = await _accessor.GetClientAsync(cancellationToken).ConfigureAwait(false);

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
