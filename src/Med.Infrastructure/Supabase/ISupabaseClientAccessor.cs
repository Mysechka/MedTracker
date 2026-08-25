namespace Med.Infrastructure.Supabase;

/// <summary>Thread-safe доступ к инициализированному Supabase Client.</summary>
public interface ISupabaseClientAccessor
{
    Task<global::Supabase.Client> GetClientAsync(CancellationToken cancellationToken = default);
}
