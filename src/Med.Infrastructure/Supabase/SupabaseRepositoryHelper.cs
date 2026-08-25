using Med.Application.Abstractions;

namespace Med.Infrastructure.Supabase;

internal static class SupabaseRepositoryHelper
{
    internal static async Task<global::Supabase.Client> GetInitializedClientAsync(
        ISupabaseClientAccessor accessor,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await accessor.GetClientAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static Guid RequireCurrentUserId(IAuthService auth)
    {
        if (auth.CurrentUserId is not { } userId)
        {
            throw new InvalidOperationException("Требуется аутентифицированный пользователь.");
        }

        return userId;
    }
}
