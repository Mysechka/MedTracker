using Med.Application.Abstractions;
using Med.Domain.Entities;
using Med.Domain.Enums;
using Med.Infrastructure.Mapping;
using Med.Infrastructure.Models;
using Microsoft.Extensions.Logging;
using Supabase.Realtime.Interfaces;
using Supabase.Realtime.PostgresChanges;
using RealtimeConstants = Supabase.Realtime.Constants;
using RealtimeListenType = Supabase.Realtime.PostgresChanges.PostgresChangesOptions.ListenType;

namespace Med.Infrastructure.Supabase.Realtime;

/// <summary>
/// Единая служба Realtime-синхронизации сущностей (dose_events, medications, courses, schedules)
/// с поддержкой автоматического переподключения (reconnect с exponential backoff).
/// </summary>
public sealed class SupabaseEntityRealtimeSync : IEntityRealtimeSync, IDisposable
{
    private readonly ISupabaseClientAccessor _accessor;
    private readonly IAuthService _auth;
    private readonly ILogger<SupabaseEntityRealtimeSync>? _logger;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private IRealtimeChannel? _channel;
    private CancellationTokenSource? _reconnectCts;

    public SupabaseEntityRealtimeSync(
        ISupabaseClientAccessor accessor,
        IAuthService auth,
        ILogger<SupabaseEntityRealtimeSync>? logger = null)
    {
        _accessor = accessor;
        _auth = auth;
        _logger = logger;
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

            try
            {
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
                    Filter = $"user_id=eq.{userId}",
                };
                _channel.OnPostgresChange(HandleScheduleChange, RealtimeListenType.All, scheduleFilter);

                await _channel.Subscribe().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _channel = null;
                _logger?.LogWarning(ex, "Ошибка подписки Realtime канала. Запуск цикла переподключения.");
                ScheduleReconnect();
                throw;
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _reconnectCts?.Cancel();
        _reconnectCts?.Dispose();
        _reconnectCts = null;

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

    private void ScheduleReconnect()
    {
        _reconnectCts?.Cancel();
        _reconnectCts?.Dispose();
        _reconnectCts = new CancellationTokenSource();
        CancellationToken ct = _reconnectCts.Token;

        _ = Task.Run(async () =>
        {
            int attempt = 0;
            while (!ct.IsCancellationRequested)
            {
                attempt++;
                int delay = Math.Min(1000 * (1 << Math.Min(attempt, 5)), 30_000);
                _logger?.LogWarning("Попытка Realtime reconnect #{Attempt}, задержка {Delay}мс", attempt, delay);
                try
                {
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                    await StartAsync(ct).ConfigureAwait(false);
                    _logger?.LogInformation("Realtime успешно переподключен (попытка {Attempt})", attempt);
                    break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Попытка переподключения #{Attempt} завершилась неудачей", attempt);
                }
            }
        }, ct);
    }

    private void HandleDoseChange(IRealtimeChannel _, PostgresChangesResponse change)
    {
        try
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
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Ошибка при обработке Realtime-события dose_events");
        }
    }

    private void HandleMedicationChange(IRealtimeChannel _, PostgresChangesResponse change)
    {
        try
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
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Ошибка при обработке Realtime-события medications");
        }
    }

    private void HandleCourseChange(IRealtimeChannel _, PostgresChangesResponse change)
    {
        try
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
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Ошибка при обработке Realtime-события courses");
        }
    }

    private void HandleScheduleChange(IRealtimeChannel _, PostgresChangesResponse change)
    {
        try
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
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Ошибка при обработке Realtime-события schedules");
        }
    }

    private static EntityChangeType? ResolveChangeType(RealtimeConstants.EventType eventType) => eventType switch
    {
        RealtimeConstants.EventType.Insert => EntityChangeType.Insert,
        RealtimeConstants.EventType.Update => EntityChangeType.Update,
        RealtimeConstants.EventType.Delete => EntityChangeType.Delete,
        _ => null,
    };

    public void Dispose()
    {
        _reconnectCts?.Cancel();
        _reconnectCts?.Dispose();
        _reconnectCts = null;

        if (_channel is not null)
        {
            try
            {
                _channel.Unsubscribe();
            }
            catch { }
            _channel = null;
        }

        _lifecycleLock.Dispose();
    }
}
