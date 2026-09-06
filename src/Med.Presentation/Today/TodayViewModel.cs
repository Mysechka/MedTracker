using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Med.Application.Abstractions;
using Med.Application.Agenda;
using Med.Application.UseCases;
using Med.Domain.Entities;
using Med.Domain.Enums;
using Med.Presentation.Abstractions;
using Med.Presentation.Feedback;
using Med.Presentation.Messaging;
using Med.Presentation.Sync;

namespace Med.Presentation.Today;

public sealed partial class TodayViewModel : ViewModelBase,
    IRecipient<DoseEventStatusChangedMessage>,
    IRecipient<ScheduleUpdatedMessage>,
    IRecipient<EntitySavedMessage<Course>>,
    IDisposable
{
    private readonly GetDayAgendaUseCase _agenda;
    private readonly ConfirmDoseUseCase _confirm;
    private readonly SkipDoseUseCase _skip;
    private readonly UndoConfirmDoseUseCase _undo;
    private readonly MaterializeUpcomingDosesUseCase _materialize;
    private readonly IDoseEventRealtime _realtime;
    private readonly IAuthService _auth;
    private readonly IUiDispatcher _ui;
    private readonly UserFeedback _feedback;
    private readonly IMessenger _messenger;
    private readonly EntityChangeDeduplicator _deduplicator;

    public TodayViewModel(
        GetDayAgendaUseCase agenda,
        ConfirmDoseUseCase confirm,
        SkipDoseUseCase skip,
        UndoConfirmDoseUseCase undo,
        MaterializeUpcomingDosesUseCase materialize,
        IDoseEventRealtime realtime,
        IAuthService auth,
        IUiDispatcher ui,
        UserFeedback feedback,
        IMessenger? messenger = null,
        EntityChangeDeduplicator? deduplicator = null)
    {
        _agenda = agenda;
        _confirm = confirm;
        _skip = skip;
        _undo = undo;
        _materialize = materialize;
        _realtime = realtime;
        _auth = auth;
        _ui = ui;
        _feedback = feedback;
        _messenger = messenger ?? WeakReferenceMessenger.Default;
        _deduplicator = deduplicator ?? new EntityChangeDeduplicator();

        _realtime.Changed += OnRealtimeChanged;
        _messenger.RegisterAll(this);
    }

    public ObservableCollection<DoseRowViewModel> Items { get; } = [];

    [ObservableProperty]
    private string _dayTitle = string.Empty;

    [ObservableProperty]
    private string _timeZoneLabel = string.Empty;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>День загружен и приёмов в нём нет — основание показать пустое состояние.</summary>
    [ObservableProperty]
    private bool _isEmpty;

    public bool IsAuthenticated => _auth.CurrentUserId is not null;

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        OnPropertyChanged(nameof(IsAuthenticated));
        if (_auth.CurrentUserId is null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            await _materialize.ExecuteAsync(cancellationToken);
            await LoadDayAsync(cancellationToken);
            await _realtime.StartAsync(cancellationToken);
        });
    }

    private Task ConfirmRowAsync(DoseRowViewModel row) =>
        RunAsync(async () =>
        {
            DoseTransitionResult result = await _confirm.ExecuteAsync(row.Id);
            _deduplicator.RecordLocalChange<DoseEvent>(row.Id);
            _messenger.Send(new DoseEventStatusChangedMessage(
                new DoseEventChange(row.Id, DoseEventState.Taken, DateTimeOffset.UtcNow, DoseEventChangeType.Update),
                ChangeSource.Local));
            _feedback.Notify(Describe(result, "Приём отмечен."));
            await LoadDayAsync(CancellationToken.None);
        });

    private Task SkipRowAsync(DoseRowViewModel row) =>
        RunAsync(async () =>
        {
            DoseTransitionResult result = await _skip.ExecuteAsync(row.Id);
            _deduplicator.RecordLocalChange<DoseEvent>(row.Id);
            _messenger.Send(new DoseEventStatusChangedMessage(
                new DoseEventChange(row.Id, DoseEventState.Skipped, DateTimeOffset.UtcNow, DoseEventChangeType.Update),
                ChangeSource.Local));
            _feedback.Notify(Describe(result, "Приём пропущен."));
            await LoadDayAsync(CancellationToken.None);
        });

    private Task UndoRowAsync(DoseRowViewModel row) =>
        RunAsync(async () =>
        {
            DoseTransitionResult result = await _undo.ExecuteAsync(row.Id);
            _deduplicator.RecordLocalChange<DoseEvent>(row.Id);
            _messenger.Send(new DoseEventStatusChangedMessage(
                new DoseEventChange(row.Id, DoseEventState.Scheduled, DateTimeOffset.UtcNow, DoseEventChangeType.Update),
                ChangeSource.Local));
            _feedback.Notify(Describe(result, "Подтверждение отменено."));
            await LoadDayAsync(CancellationToken.None);
        });

    private async Task LoadDayAsync(CancellationToken cancellationToken)
    {
        DayAgenda agenda = await _agenda.ExecuteAsync(cancellationToken);

        DayTitle = FormatDay(agenda.LocalDate);
        TimeZoneLabel = agenda.TimeZoneId;

        Items.Clear();
        foreach (DoseAgendaItem item in agenda.Items)
        {
            Items.Add(new DoseRowViewModel(item, ConfirmRowAsync, SkipRowAsync, UndoRowAsync));
        }

        IsEmpty = Items.Count == 0;
        ProgressText = FormatProgress(agenda.Items);
    }

    internal Task? LastReloadTask { get; private set; }

    public void Dispose()
    {
        _realtime.Changed -= OnRealtimeChanged;
        _messenger.UnregisterAll(this);
    }

    public void Receive(DoseEventStatusChangedMessage message)
    {
        if (message.Source != ChangeSource.Realtime)
        {
            return;
        }

        _ui.Post(() => LastReloadTask = ReloadQuietAsync());
    }

    public void Receive(ScheduleUpdatedMessage message)
    {
        _ui.Post(() => LastReloadTask = ReloadWithMaterializeAsync());
    }

    public void Receive(EntitySavedMessage<Course> message)
    {
        _ui.Post(() => LastReloadTask = ReloadWithMaterializeAsync());
    }

    private async Task ReloadWithMaterializeAsync()
    {
        try
        {
            await _materialize.ExecuteAsync(CancellationToken.None);
            await LoadDayAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _feedback.Notify(ex.Message);
        }
    }

    private void OnRealtimeChanged(object? sender, DoseEventChange change)
    {
        // Обновление без локального таймера — только реакция на Realtime.
        // Событие приходит из фонового потока, поэтому список меняется через диспетчер UI.
        _ui.Post(() => LastReloadTask = ReloadQuietAsync());
    }

    private async Task ReloadQuietAsync()
    {
        try
        {
            await LoadDayAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _feedback.Notify(ex.Message);
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
            _feedback.Notify(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string Describe(DoseTransitionResult result, string appliedText)
    {
        if (result.IsApplied)
        {
            return appliedText;
        }

        if (result.IsNoOp)
        {
            return "Состояние уже было таким — ничего не изменилось.";
        }

        return result.Reason is { Length: > 0 } reason
            ? $"Действие отклонено: {reason}"
            : "Действие отклонено: состояние приёма уже изменилось.";
    }

    private static string FormatDay(DateOnly localDate) =>
        localDate.ToDateTime(TimeOnly.MinValue).ToString("dddd, d MMMM", CultureInfo.CurrentCulture);

    private static string FormatProgress(IReadOnlyList<DoseAgendaItem> items)
    {
        if (items.Count == 0)
        {
            return string.Empty;
        }

        int taken = items.Count(static item => item.State == DoseEventState.Taken);
        return $"Принято {taken} из {items.Count}";
    }
}
