using Med.Application.Abstractions;
using Med.Infrastructure.Supabase;
using Med.Domain.Entities;
using Med.Infrastructure.Mapping;
using Med.Infrastructure.Models;
using Supabase.Postgrest;
using static Supabase.Postgrest.Constants;

namespace Med.Infrastructure.Repositories;

public sealed class DoseEventRepository : IDoseEventRepository
{
    private readonly ISupabaseClientAccessor _accessor;
    private readonly IAuthService _auth;

    public DoseEventRepository(ISupabaseClientAccessor accessor, IAuthService auth)
    {
        _accessor = accessor;
        _auth = auth;
    }

    public async Task<IReadOnlyList<DoseEvent>> ListForLocalDateAsync(
        DateOnly localDate,
        CancellationToken cancellationToken = default)
    {
        _ = SupabaseRepositoryHelper.RequireCurrentUserId(_auth);
        global::Supabase.Client client = await SupabaseRepositoryHelper
            .GetInitializedClientAsync(_accessor, cancellationToken)
            .ConfigureAwait(false);

        string dateText = localDate.ToString("yyyy-MM-dd");
        var response = await client
            .From<DoseEventRow>()
            .Filter("local_date", Operator.Equals, dateText)
            .Get(cancellationToken)
            .ConfigureAwait(false);

        return response.Models.Select(EntityMappers.ToDomain).ToArray();
    }

    public async Task<IReadOnlyList<DoseEvent>> ListByScheduleAsync(
        Guid scheduleId,
        CancellationToken cancellationToken = default)
    {
        _ = SupabaseRepositoryHelper.RequireCurrentUserId(_auth);
        global::Supabase.Client client = await SupabaseRepositoryHelper
            .GetInitializedClientAsync(_accessor, cancellationToken)
            .ConfigureAwait(false);

        var response = await client
            .From<DoseEventRow>()
            .Filter("schedule_id", Operator.Equals, scheduleId.ToString())
            .Get(cancellationToken)
            .ConfigureAwait(false);

        return response.Models.Select(EntityMappers.ToDomain).ToArray();
    }

    public async Task UpsertManyAsync(
        IReadOnlyList<DoseEvent> events,
        CancellationToken cancellationToken = default)
    {
        if (events.Count == 0)
        {
            return;
        }

        Guid userId = SupabaseRepositoryHelper.RequireCurrentUserId(_auth);
        global::Supabase.Client client = await SupabaseRepositoryHelper
            .GetInitializedClientAsync(_accessor, cancellationToken)
            .ConfigureAwait(false);

        List<DoseEventRow> rows = events.Select(e => EntityMappers.ToRow(e, userId)).ToList();
        var options = new QueryOptions { OnConflict = "dedupe_key" };

        await client
            .From<DoseEventRow>()
            .Upsert(rows, options, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<DoseEvent?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _ = SupabaseRepositoryHelper.RequireCurrentUserId(_auth);
        global::Supabase.Client client = await SupabaseRepositoryHelper
            .GetInitializedClientAsync(_accessor, cancellationToken)
            .ConfigureAwait(false);

        var response = await client
            .From<DoseEventRow>()
            .Filter("id", Operator.Equals, id.ToString())
            .Get(cancellationToken)
            .ConfigureAwait(false);

        DoseEventRow? row = response.Models.FirstOrDefault();
        return row is null ? null : EntityMappers.ToDomain(row);
    }
}
