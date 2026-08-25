using Med.Application.Abstractions;
using Med.Infrastructure.Supabase;
using Med.Domain.Entities;
using Med.Infrastructure.Mapping;
using Med.Infrastructure.Models;
using static Supabase.Postgrest.Constants;

namespace Med.Infrastructure.Repositories;

public sealed class CourseRepository : ICourseRepository
{
    private readonly ISupabaseClientAccessor _accessor;
    private readonly IAuthService _auth;

    public CourseRepository(ISupabaseClientAccessor accessor, IAuthService auth)
    {
        _accessor = accessor;
        _auth = auth;
    }

    public async Task<IReadOnlyList<Course>> ListAsync(CancellationToken cancellationToken = default)
    {
        _ = SupabaseRepositoryHelper.RequireCurrentUserId(_auth);
        global::Supabase.Client client = await SupabaseRepositoryHelper
            .GetInitializedClientAsync(_accessor, cancellationToken)
            .ConfigureAwait(false);

        var response = await client.From<CourseRow>().Get(cancellationToken).ConfigureAwait(false);
        return response.Models.Select(EntityMappers.ToDomain).ToArray();
    }

    public async Task<Course?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _ = SupabaseRepositoryHelper.RequireCurrentUserId(_auth);
        global::Supabase.Client client = await SupabaseRepositoryHelper
            .GetInitializedClientAsync(_accessor, cancellationToken)
            .ConfigureAwait(false);

        var response = await client
            .From<CourseRow>()
            .Filter("id", Operator.Equals, id.ToString())
            .Get(cancellationToken)
            .ConfigureAwait(false);

        CourseRow? row = response.Models.FirstOrDefault();
        return row is null ? null : EntityMappers.ToDomain(row);
    }

    public async Task UpsertAsync(Course course, CancellationToken cancellationToken = default)
    {
        Guid userId = SupabaseRepositoryHelper.RequireCurrentUserId(_auth);
        if (course.UserId != userId)
        {
            throw new InvalidOperationException("course.user_id должен совпадать с текущим пользователем.");
        }

        global::Supabase.Client client = await SupabaseRepositoryHelper
            .GetInitializedClientAsync(_accessor, cancellationToken)
            .ConfigureAwait(false);

        CourseRow row = EntityMappers.ToRow(course);
        await client.From<CourseRow>().Upsert(row, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _ = SupabaseRepositoryHelper.RequireCurrentUserId(_auth);
        global::Supabase.Client client = await SupabaseRepositoryHelper
            .GetInitializedClientAsync(_accessor, cancellationToken)
            .ConfigureAwait(false);

        await client
            .From<CourseRow>()
            .Filter("id", Operator.Equals, id.ToString())
            .Delete(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }
}
