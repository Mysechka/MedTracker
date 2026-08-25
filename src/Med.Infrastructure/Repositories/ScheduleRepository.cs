using Med.Application.Abstractions;
using Med.Infrastructure.Supabase;
using Med.Domain.Entities;
using Med.Infrastructure.Mapping;
using Med.Infrastructure.Models;
using static Supabase.Postgrest.Constants;

namespace Med.Infrastructure.Repositories;

public sealed class ScheduleRepository : IScheduleRepository
{
    private readonly ISupabaseClientAccessor _accessor;
    private readonly IAuthService _auth;

    public ScheduleRepository(ISupabaseClientAccessor accessor, IAuthService auth)
    {
        _accessor = accessor;
        _auth = auth;
    }

    public async Task<IReadOnlyList<Schedule>> ListByCourseAsync(
        Guid courseId,
        CancellationToken cancellationToken = default)
    {
        _ = SupabaseRepositoryHelper.RequireCurrentUserId(_auth);
        global::Supabase.Client client = await SupabaseRepositoryHelper
            .GetInitializedClientAsync(_accessor, cancellationToken)
            .ConfigureAwait(false);

        var response = await client
            .From<ScheduleRow>()
            .Filter("course_id", Operator.Equals, courseId.ToString())
            .Get(cancellationToken)
            .ConfigureAwait(false);

        return response.Models.Select(EntityMappers.ToDomain).ToArray();
    }

    public async Task<Schedule?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _ = SupabaseRepositoryHelper.RequireCurrentUserId(_auth);
        global::Supabase.Client client = await SupabaseRepositoryHelper
            .GetInitializedClientAsync(_accessor, cancellationToken)
            .ConfigureAwait(false);

        var response = await client
            .From<ScheduleRow>()
            .Filter("id", Operator.Equals, id.ToString())
            .Get(cancellationToken)
            .ConfigureAwait(false);

        ScheduleRow? row = response.Models.FirstOrDefault();
        return row is null ? null : EntityMappers.ToDomain(row);
    }

    public async Task UpsertAsync(Schedule schedule, CancellationToken cancellationToken = default)
    {
        Guid userId = SupabaseRepositoryHelper.RequireCurrentUserId(_auth);
        global::Supabase.Client client = await SupabaseRepositoryHelper
            .GetInitializedClientAsync(_accessor, cancellationToken)
            .ConfigureAwait(false);

        ScheduleRow row = EntityMappers.ToRow(schedule, userId);
        await client.From<ScheduleRow>().Upsert(row, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _ = SupabaseRepositoryHelper.RequireCurrentUserId(_auth);
        global::Supabase.Client client = await SupabaseRepositoryHelper
            .GetInitializedClientAsync(_accessor, cancellationToken)
            .ConfigureAwait(false);

        await client
            .From<ScheduleRow>()
            .Filter("id", Operator.Equals, id.ToString())
            .Delete(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }
}
