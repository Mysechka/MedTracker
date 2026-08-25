using Med.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using SupabaseClientOptions = Supabase.SupabaseOptions;

namespace Med.Infrastructure.Supabase;

public sealed class SupabaseClientAccessor : ISupabaseClientAccessor
{
    private readonly IOptions<Configuration.SupabaseOptions> _options;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private global::Supabase.Client? _client;

    public SupabaseClientAccessor(IOptions<Configuration.SupabaseOptions> options)
    {
        _options = options;
    }

    public async Task<global::Supabase.Client> GetClientAsync(CancellationToken cancellationToken = default)
    {
        if (_client is not null)
        {
            return _client;
        }

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_client is null)
            {
                Configuration.SupabaseOptions config = _options.Value;
                _client = new global::Supabase.Client(
                    config.Url,
                    config.AnonKey,
                    new SupabaseClientOptions
                    {
                        AutoRefreshToken = true,
                        AutoConnectRealtime = true,
                    });

                await _client.InitializeAsync().ConfigureAwait(false);
            }

            return _client;
        }
        finally
        {
            _initLock.Release();
        }
    }
}
