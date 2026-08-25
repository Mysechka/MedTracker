using Med.Application.Abstractions;
using Med.Infrastructure.Supabase;
using Med.Domain.Entities;
using Med.Infrastructure.Mapping;
using Med.Infrastructure.Models;
using static Supabase.Postgrest.Constants;

namespace Med.Infrastructure.Repositories;

public sealed class DiagnosisRepository : IDiagnosisRepository
{
    private readonly ISupabaseClientAccessor _accessor;
    private readonly IAuthService _auth;

    public DiagnosisRepository(ISupabaseClientAccessor accessor, IAuthService auth)
    {
        _accessor = accessor;
        _auth = auth;
    }

    public async Task<IReadOnlyList<Diagnosis>> ListAsync(CancellationToken cancellationToken = default)
    {
        _ = SupabaseRepositoryHelper.RequireCurrentUserId(_auth);
        global::Supabase.Client client = await SupabaseRepositoryHelper
            .GetInitializedClientAsync(_accessor, cancellationToken)
            .ConfigureAwait(false);

        var response = await client.From<DiagnosisRow>().Get(cancellationToken).ConfigureAwait(false);
        return response.Models.Select(EntityMappers.ToDomain).ToArray();
    }

    public async Task<Diagnosis?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _ = SupabaseRepositoryHelper.RequireCurrentUserId(_auth);
        global::Supabase.Client client = await SupabaseRepositoryHelper
            .GetInitializedClientAsync(_accessor, cancellationToken)
            .ConfigureAwait(false);

        var response = await client
            .From<DiagnosisRow>()
            .Filter("id", Operator.Equals, id.ToString())
            .Get(cancellationToken)
            .ConfigureAwait(false);

        DiagnosisRow? row = response.Models.FirstOrDefault();
        return row is null ? null : EntityMappers.ToDomain(row);
    }

    public async Task UpsertAsync(Diagnosis diagnosis, CancellationToken cancellationToken = default)
    {
        Guid userId = SupabaseRepositoryHelper.RequireCurrentUserId(_auth);
        if (diagnosis.UserId != userId)
        {
            throw new InvalidOperationException("diagnosis.user_id должен совпадать с текущим пользователем.");
        }

        global::Supabase.Client client = await SupabaseRepositoryHelper
            .GetInitializedClientAsync(_accessor, cancellationToken)
            .ConfigureAwait(false);

        DiagnosisRow row = EntityMappers.ToRow(diagnosis);
        await client.From<DiagnosisRow>().Upsert(row, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _ = SupabaseRepositoryHelper.RequireCurrentUserId(_auth);
        global::Supabase.Client client = await SupabaseRepositoryHelper
            .GetInitializedClientAsync(_accessor, cancellationToken)
            .ConfigureAwait(false);

        await client
            .From<DiagnosisRow>()
            .Filter("id", Operator.Equals, id.ToString())
            .Delete(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }
}
