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

public sealed class SupabaseEntityRealtimeSync : IEntityRealtimeSync
{
    private readonly ISupabaseClientAccessor _accessor;
    private readonly IAuthService _auth;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private IRealtimeChannel? _channel;

    public SupabaseEntityRealtimeSync(ISupabaseClientAccessor accessor, IAuthService auth)
    {
        _accessor = accessor;
        _auth = auth;
    }

    public event EventHandler<EntityChangeNotification>? EntityChanged;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_auth.CurrentUserId is not { } userId)
        {
            throw new InvalidOperationException("Realtime-синхронизация требует аутентифицированного пользователя.");
        }

        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_channel is not null)
            {
                return;
            }

            global::Supabase.Client client = await _accessor.GetClientAsync(cancellationToken).ConfigureAwait(false);

            _channel = client.Realtime.Channel($"entity-sync:{userId}");

            var doseFilter = new PostgresChangesFilter
            {
                Schema = "public",
                Table = "dose_events",
                Filter = $"user_id=eq.{userId}",
            };
            _channel.OnPostgresChange(HandleDoseChange, RealtimeListenType.All, doseFilter);

            var medFilter = new PostgresChangesFilter
            {
                Schema = "public",
                Table = "medications",
                Filter = $"user_id=eq.{userId}",
            };
            _channel.OnPostgresChange(HandleMedicationChange, RealtimeListenType.All, medFilter);

            var courseFilter = new PostgresChangesFilter
            {
                Schema = "public",
                Table = "courses",
                Filter = $"user_id=eq.{userId}",
            };
            _channel.OnPostgresChange(HandleCourseChange, RealtimeListenType.All, courseFilter);

            var scheduleFilter = new PostgresChangesFilter
            {
                Schema = "public",
                Table = "schedules",
            };
            _channel.OnPostgresChange(HandleScheduleChange, RealtimeListenType.All, scheduleFilter);

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

    private void HandleDoseChange(IRealtimeChannel _, PostgresChangesResponse change)
    {
        EntityChangeType? changeType = ResolveChangeType(change.Event);
        if (changeType is not { } resolvedChangeType) return;

        DoseEventRow? row = resolvedChangeType switch
        {
            EntityChangeType.Delete => change.OldModel<DoseEventRow>(),
            _ => change.Model<DoseEventRow>(),
        };

        if (row is null) return;

        DoseEvent domain = EntityMappers.ToDomain(row);
        var doseEventChange = new DoseEventChange(
            domain.Id,
            domain.State,
            domain.ScheduledAt,
            resolvedChangeType switch
            {
                EntityChangeType.Insert => DoseEventChangeType.Insert,
                EntityChangeType.Update => DoseEventChangeType.Update,
                _ => DoseEventChangeType.Delete,
            });

        EntityChanged?.Invoke(this, new EntityChangeNotification(
            "dose_events",
            domain.Id,
            resolvedChangeType,
            DateTimeOffset.UtcNow,
            doseEventChange));
    }

    private void HandleMedicationChange(IRealtimeChannel _, PostgresChangesResponse change)
    {
        EntityChangeType? changeType = ResolveChangeType(change.Event);
        if (changeType is not { } resolvedChangeType) return;

        MedicationRow? row = resolvedChangeType switch
        {
            EntityChangeType.Delete => change.OldModel<MedicationRow>(),
            _ => change.Model<MedicationRow>(),
        };

        if (row is null) return;

        Medication domain = EntityMappers.ToDomain(row);
        EntityChanged?.Invoke(this, new EntityChangeNotification(
            "medications",
            domain.Id,
            resolvedChangeType,
            DateTimeOffset.UtcNow,
            domain));
    }

    private void HandleCourseChange(IRealtimeChannel _, PostgresChangesResponse change)
    {
        EntityChangeType? changeType = ResolveChangeType(change.Event);
        if (changeType is not { } resolvedChangeType) return;

        CourseRow? row = resolvedChangeType switch
        {
            EntityChangeType.Delete => change.OldModel<CourseRow>(),
            _ => change.Model<CourseRow>(),
        };

        if (row is null) return;

        Course domain = EntityMappers.ToDomain(row);
        EntityChanged?.Invoke(this, new EntityChangeNotification(
            "courses",
            domain.Id,
            resolvedChangeType,
            DateTimeOffset.UtcNow,
            domain));
    }

    private void HandleScheduleChange(IRealtimeChannel _, PostgresChangesResponse change)
    {
        EntityChangeType? changeType = ResolveChangeType(change.Event);
        if (changeType is not { } resolvedChangeType) return;

        ScheduleRow? row = resolvedChangeType switch
        {
            EntityChangeType.Delete => change.OldModel<ScheduleRow>(),
            _ => change.Model<ScheduleRow>(),
        };

        if (row is null) return;

        Schedule domain = EntityMappers.ToDomain(row);
        EntityChanged?.Invoke(this, new EntityChangeNotification(
            "schedules",
            domain.Id,
            resolvedChangeType,
            DateTimeOffset.UtcNow,
            domain));
    }

    private static EntityChangeType? ResolveChangeType(RealtimeConstants.EventType eventType) => eventType switch
    {
        RealtimeConstants.EventType.Insert => EntityChangeType.Insert,
        RealtimeConstants.EventType.Update => EntityChangeType.Update,
        RealtimeConstants.EventType.Delete => EntityChangeType.Delete,
        _ => null,
    };
}
