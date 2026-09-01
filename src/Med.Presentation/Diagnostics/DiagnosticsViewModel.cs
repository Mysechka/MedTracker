using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Med.Application.Abstractions;
using Med.Domain.Entities;
using Med.Presentation.Abstractions;

namespace Med.Presentation.Diagnostics;

public sealed partial class DiagnosticsViewModel : ViewModelBase
{
    private readonly INotificationDeliveryRepository _deliveries;
    private readonly ITickInvoker _tick;
    private readonly IDoseEventRealtime _realtime;

    public DiagnosticsViewModel(
        INotificationDeliveryRepository deliveries,
        ITickInvoker tick,
        IDoseEventRealtime realtime,
        IUiDispatcher ui)
    {
        _deliveries = deliveries;
        _tick = tick;
        _realtime = realtime;
        _realtime.Changed += (_, change) => ui.Post(() =>
        {
            LastRealtimeEvent = $"{change.ChangeType} {change.Id} → {change.State}";
            RealtimeStatus = "активна (событие получено)";
        });
    }

    public ObservableCollection<NotificationDelivery> Deliveries { get; } = [];

    [ObservableProperty]
    private string _realtimeStatus = "неизвестно";

    [ObservableProperty]
    private string _lastRealtimeEvent = "—";

    [ObservableProperty]
    private string _tickResult = string.Empty;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Журнал загружен и пуст — основание показать пустое состояние.</summary>
    [ObservableProperty]
    private bool _isEmpty;

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await RunAsync(() => LoadAsync(cancellationToken));
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Deliveries.Clear();
        foreach (NotificationDelivery delivery in await _deliveries.ListRecentAsync(50, cancellationToken))
        {
            Deliveries.Add(delivery);
        }

        IsEmpty = Deliveries.Count == 0;
        Message = $"Записей в журнале: {Deliveries.Count}";
    }

    [RelayCommand]
    private async Task StartRealtimeAsync(CancellationToken cancellationToken)
    {
        await RunAsync(async () =>
        {
            await _realtime.StartAsync(cancellationToken);
            RealtimeStatus = "подписка запущена";
            Message = "Realtime StartAsync выполнен.";
        });
    }

    [RelayCommand]
    private async Task StopRealtimeAsync(CancellationToken cancellationToken)
    {
        await RunAsync(async () =>
        {
            await _realtime.StopAsync(cancellationToken);
            RealtimeStatus = "остановлена";
            Message = "Realtime StopAsync выполнен.";
        });
    }

    [RelayCommand]
    private async Task InvokeTickAsync(CancellationToken cancellationToken)
    {
        await RunAsync(async () =>
        {
            TickInvokeResult result = await _tick.InvokeAsync(cancellationToken);
            TickResult = result.RawBody;
            // Именно LoadAsync: вложенный RunAsync упёрся бы в IsBusy и журнал остался бы старым.
            await LoadAsync(cancellationToken);
            Message = result.Ok ? "tick выполнен." : "tick завершился ошибкой.";
        });
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await action();
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
