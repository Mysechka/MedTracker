using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Med.Application.Abstractions;
using Med.Application.UseCases;
using Med.Domain.Abstractions;
using Med.Domain.Entities;
using Med.Domain.Enums;
using Med.Domain.Scheduling;

namespace Med.Presentation.Today;

public sealed partial class DoseEventRowViewModel : ObservableObject
{
    public DoseEventRowViewModel(DoseEvent doseEvent)
    {
        Id = doseEvent.Id;
        ScheduledAt = doseEvent.ScheduledAt;
        LocalDate = doseEvent.LocalDate;
        State = doseEvent.State;
        Source = doseEvent.Source;
        Display = $"{doseEvent.ScheduledAt:HH:mm} UTC · {doseEvent.State}";
    }

    public Guid Id { get; }

    public DateTimeOffset ScheduledAt { get; }

    public DateOnly LocalDate { get; }

    public DoseEventState State { get; }

    public DoseEventSource? Source { get; }

    public string Display { get; }
}

public sealed partial class TodayViewModel : ViewModelBase
{
    private readonly IDoseEventRepository _doseEvents;
    private readonly IProfileRepository _profiles;
    private readonly ISystemClock _clock;
    private readonly ConfirmDoseUseCase _confirm;
    private readonly SkipDoseUseCase _skip;
    private readonly UndoConfirmDoseUseCase _undo;
    private readonly MaterializeUpcomingDosesUseCase _materialize;
    private readonly IDoseEventRealtime _realtime;

    public TodayViewModel(
        IDoseEventRepository doseEvents,
        IProfileRepository profiles,
        ISystemClock clock,
        ConfirmDoseUseCase confirm,
        SkipDoseUseCase skip,
        UndoConfirmDoseUseCase undo,
        MaterializeUpcomingDosesUseCase materialize,
        IDoseEventRealtime realtime)
    {
        _doseEvents = doseEvents;
        _profiles = profiles;
        _clock = clock;
        _confirm = confirm;
        _skip = skip;
        _undo = undo;
        _materialize = materialize;
        _realtime = realtime;
        _realtime.Changed += OnRealtimeChanged;
    }

    public ObservableCollection<DoseEventRowViewModel> Items { get; } = [];

    [ObservableProperty]
    private DoseEventRowViewModel? _selected;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private string _localDateLabel = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await RunAsync(async () =>
        {
            await _materialize.ExecuteAsync(cancellationToken);
            await LoadDayAsync(cancellationToken);
            await _realtime.StartAsync(cancellationToken);
            Message = $"Realtime: подписан. Событий: {Items.Count}";
        });
    }

    [RelayCommand]
    private async Task ConfirmAsync(CancellationToken cancellationToken)
    {
        if (Selected is null)
        {
            Message = "Выберите событие.";
            return;
        }

        await RunAsync(async () =>
        {
            DoseTransitionResult result = await _confirm.ExecuteAsync(
                Selected.Id,
                cancellationToken: cancellationToken);
            Message = $"{result.Outcome}: {result.State}";
            await LoadDayAsync(cancellationToken);
        });
    }

    [RelayCommand]
    private async Task SkipAsync(CancellationToken cancellationToken)
    {
        if (Selected is null)
        {
            Message = "Выберите событие.";
            return;
        }

        await RunAsync(async () =>
        {
            DoseTransitionResult result = await _skip.ExecuteAsync(
                Selected.Id,
                cancellationToken: cancellationToken);
            Message = $"{result.Outcome}: {result.State}";
            await LoadDayAsync(cancellationToken);
        });
    }

    [RelayCommand]
    private async Task UndoAsync(CancellationToken cancellationToken)
    {
        if (Selected is null)
        {
            Message = "Выберите событие.";
            return;
        }

        await RunAsync(async () =>
        {
            DoseTransitionResult result = await _undo.ExecuteAsync(
                Selected.Id,
                cancellationToken: cancellationToken);
            Message = $"{result.Outcome}: {result.State}";
            await LoadDayAsync(cancellationToken);
        });
    }

    private async Task LoadDayAsync(CancellationToken cancellationToken)
    {
        Profile? profile = await _profiles.GetCurrentAsync(cancellationToken);
        TimeZoneInfo tz = profile?.ResolveTimeZone() ?? TimeZoneInfo.Utc;
        DateOnly localDate = LocalTimeConverter.ToLocalDate(_clock.UtcNow, tz);
        LocalDateLabel = $"{localDate:yyyy-MM-dd} ({tz.Id})";

        IReadOnlyList<DoseEvent> events = await _doseEvents.ListForLocalDateAsync(localDate, cancellationToken);
        Items.Clear();
        foreach (DoseEvent doseEvent in events.OrderBy(e => e.ScheduledAt))
        {
            Items.Add(new DoseEventRowViewModel(doseEvent));
        }
    }

    private void OnRealtimeChanged(object? sender, DoseEventChange change)
    {
        // Обновление списка без локального таймера — только реакция на Realtime.
        _ = RefreshQuietAsync();
    }

    private async Task RefreshQuietAsync()
    {
        try
        {
            await LoadDayAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
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
