using Med.Application.Abstractions;

namespace Med.Infrastructure.Supabase.Realtime;

/// <summary>
/// Лёгкий фасад над IEntityRealtimeSync для поддержки интерфейса IDoseEventRealtime
/// без дублирования каналов подписки и сетевого трафика.
/// </summary>
public sealed class SupabaseDoseEventRealtime : IDoseEventRealtime, IDisposable
{
    private readonly IEntityRealtimeSync _sync;

    public SupabaseDoseEventRealtime(IEntityRealtimeSync sync)
    {
        _sync = sync ?? throw new ArgumentNullException(nameof(sync));
        _sync.EntityChanged += OnEntityChanged;
    }

    public event EventHandler<DoseEventChange>? Changed;

    public Task StartAsync(CancellationToken cancellationToken = default) =>
        _sync.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken = default) =>
        _sync.StopAsync(cancellationToken);

    private void OnEntityChanged(object? sender, EntityChangeNotification notification)
    {
        if (notification.TableName == "dose_events" && notification.Payload is DoseEventChange doseChange)
        {
            Changed?.Invoke(this, doseChange);
        }
    }

    public void Dispose()
    {
        _sync.EntityChanged -= OnEntityChanged;
    }
}
