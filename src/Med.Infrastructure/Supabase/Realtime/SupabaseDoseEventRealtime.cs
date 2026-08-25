using Med.Application.Abstractions;
using Med.Domain.Entities;
using Med.Domain.Enums;
using Med.Infrastructure.Mapping;
using Med.Infrastructure.Models;
using Supabase.Realtime.Interfaces;
using Supabase.Realtime.PostgresChanges;
using RealtimeConstants = Supabase.Realtime.Constants;
using RealtimeListenType = Supabase.Realtime.PostgresChanges.PostgresChangesOptions.ListenType;

namespace Med.Infrastructure.Supabase.Realtime;

public sealed class SupabaseDoseEventRealtime : IDoseEventRealtime
{
    private readonly ISupabaseClientAccessor _accessor;
    private readonly IAuthService _auth;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private IRealtimeChannel? _channel;

    public SupabaseDoseEventRealtime(ISupabaseClientAccessor accessor, IAuthService auth)
    {
        _accessor = accessor;
        _auth = auth;
    }

    public event EventHandler<DoseEventChange>? Changed;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_auth.CurrentUserId is not { } userId)
        {
            throw new InvalidOperationException("Realtime dose_events требует аутентифицированного пользователя.");
        }

        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_channel is not null)
            {
                return;
            }

            global::Supabase.Client client = await _accessor.GetClientAsync(cancellationToken).ConfigureAwait(false);

            var filter = new PostgresChangesFilter
            {
                Schema = "public",
                Table = "dose_events",
                Filter = $"user_id=eq.{userId}",
            };

            _channel = client.Realtime.Channel($"dose-events:{userId}");
            _channel.OnPostgresChange(HandleChange, RealtimeListenType.All, filter);
            await _channel.Subscribe().ConfigureAwait(false);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_channel is null)
            {
                return;
            }

            _channel.Unsubscribe();
            _channel = null;
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private void HandleChange(IRealtimeChannel _, PostgresChangesResponse change)
    {
        DoseEventChangeType? changeType = change.Event switch
        {
            RealtimeConstants.EventType.Insert => DoseEventChangeType.Insert,
            RealtimeConstants.EventType.Update => DoseEventChangeType.Update,
            RealtimeConstants.EventType.Delete => DoseEventChangeType.Delete,
            _ => null,
        };

        if (changeType is not { } resolvedChangeType)
        {
            return;
        }

        DoseEventRow? row = resolvedChangeType switch
        {
            DoseEventChangeType.Delete => change.OldModel<DoseEventRow>(),
            _ => change.Model<DoseEventRow>(),
        };

        if (row is null)
        {
            return;
        }

        DoseEvent domain = EntityMappers.ToDomain(row);
        var payload = new DoseEventChange(
            domain.Id,
            domain.State,
            domain.ScheduledAt,
            resolvedChangeType);

        Changed?.Invoke(this, payload);
    }
}
