using Med.Application.Abstractions;
using Med.Infrastructure.Supabase;
using Med.Domain.Entities;
using Med.Infrastructure.Mapping;
using Med.Infrastructure.Models;
using static Supabase.Postgrest.Constants;

namespace Med.Infrastructure.Repositories;

public sealed class InventoryRepository : IInventoryRepository
{
    private readonly ISupabaseClientAccessor _accessor;
    private readonly IAuthService _auth;

    public InventoryRepository(ISupabaseClientAccessor accessor, IAuthService auth)
    {
        _accessor = accessor;
        _auth = auth;
    }

    public async Task<Inventory?> GetByMedicationAsync(
        Guid medicationId,
        CancellationToken cancellationToken = default)
    {
        _ = SupabaseRepositoryHelper.RequireCurrentUserId(_auth);
        global::Supabase.Client client = await SupabaseRepositoryHelper
            .GetInitializedClientAsync(_accessor, cancellationToken)
            .ConfigureAwait(false);

        var response = await client
            .From<InventoryRow>()
            .Filter("medication_id", Operator.Equals, medicationId.ToString())
            .Get(cancellationToken)
            .ConfigureAwait(false);

        InventoryRow? row = response.Models.FirstOrDefault();
        return row is null ? null : EntityMappers.ToDomain(row);
    }

    public async Task UpsertAsync(Inventory inventory, CancellationToken cancellationToken = default)
    {
        Guid userId = SupabaseRepositoryHelper.RequireCurrentUserId(_auth);
        if (inventory.UserId != userId)
        {
            throw new InvalidOperationException("inventory.user_id должен совпадать с текущим пользователем.");
        }

        global::Supabase.Client client = await SupabaseRepositoryHelper
            .GetInitializedClientAsync(_accessor, cancellationToken)
            .ConfigureAwait(false);

        InventoryRow row = EntityMappers.ToRow(inventory);
        await client.From<InventoryRow>().Upsert(row, cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
