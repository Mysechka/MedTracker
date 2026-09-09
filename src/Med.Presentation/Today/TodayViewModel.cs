using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Med.Application.Abstractions;
using Med.Application.Agenda;
using Med.Application.UseCases;
using Med.Domain.Abstractions;
using Med.Domain.Entities;
using Med.Domain.Enums;
using Med.Presentation.Abstractions;
using Med.Presentation.Feedback;
using Med.Presentation.Messaging;
using Med.Presentation.Sync;
using Microsoft.Extensions.Logging;

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
    private readonly ISystemClock? _clock;
    private readonly ILogger<TodayViewModel>? _logger;
    private readonly Timer? _cleanupTimer;
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _rowRemovalTokens = new();

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
        EntityChangeDeduplicator? deduplicator = null,
        ISystemClock? clock = null,
        ILogger<TodayViewModel>? logger = null)
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
        _clock = clock;
        _logger = logger;

        _realtime.Changed += OnRealtimeChanged;
        _messenger.RegisterAll(this);

        _cleanupTimer = new Timer(_ => _ui.Post(CleanExpiredItems), null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
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
            CancelRowRemoval(row.Id);
            row.State = DoseEventState.Taken;
            DoseTransitionResult result = await _confirm.ExecuteAsync(row.Id);
            _deduplicator.RecordLocalChange<DoseEvent>(row.Id);
            DateTimeOffset now = _clock?.UtcNow ?? DateTimeOffset.UtcNow;
            _messenger.Send(new DoseEventStatusChangedMessage(
                new DoseEventChange(row.Id, DoseEventState.Taken, now, DoseEventChangeType.Update, row.MedicationId),
                ChangeSource.Local));
            _feedback.Notify(Describe(result, "Приём отмечен."));
            _logger?.LogInformation("[Today] Dose #{DoseId} marked as taken", row.Id);
            UpdateProgress();
        });

    private Task SkipRowAsync(DoseRowViewModel row) =>
        RunAsync(async () =>
        {
            DateTimeOffset now = _clock?.UtcNow ?? DateTimeOffset.UtcNow;
            row.State = DoseEventState.Skipped;
            row.SkippedAt = now;
            DoseTransitionResult result = await _skip.ExecuteAsync(row.Id);
            _deduplicator.RecordLocalChange<DoseEvent>(row.Id);
            _messenger.Send(new DoseEventStatusChangedMessage(
                new DoseEventChange(row.Id, DoseEventState.Skipped, now, DoseEventChangeType.Update, row.MedicationId),
                ChangeSource.Local));
            _feedback.Notify(Describe(result, "Приём пропущен."));
            _logger?.LogInformation("[Today] Dose #{DoseId} skipped; scheduled removal in 1 minute", row.Id);

            // Правило 2: после 1 минуты если на сообщение была реакция "пропустить", оно удаляется
            CancelRowRemoval(row.Id);
            CancellationTokenSource cts = new();
            _rowRemovalTokens[row.Id] = cts;
            _ = ScheduleRowRemovalAsync(row, TimeSpan.FromMinutes(1), cts.Token);
        });

    private async Task ScheduleRowRemovalAsync(DoseRowViewModel row, TimeSpan delay, CancellationToken token)
    {
        try
        {
            await Task.Delay(delay, token).ConfigureAwait(false);
            if (token.IsCancellationRequested)
            {
                return;
            }

            _ui.Post(() =>
            {
                if (row.State == DoseEventState.Skipped && Items.Contains(row))
                {
                    Items.Remove(row);
                    IsEmpty = Items.Count == 0;
                    _logger?.LogInformation("[Today] Auto-removed skipped dose #{DoseId} after 1 minute", row.Id);
                }
            });
        }
        catch (OperationCanceledException)
        {
            // Cancelled cleanly
        }
        finally
        {
            _rowRemovalTokens.TryRemove(row.Id, out _);
        }
    }

    private void CancelRowRemoval(Guid rowId)
    {
        if (_rowRemovalTokens.TryRemove(rowId, out CancellationTokenSource? cts))
        {
            try
            {
                cts.Cancel();
                cts.Dispose();
            }
            catch (ObjectDisposedException) { }
        }
    }

    private void CancelAllRowRemovals()
    {
        foreach (var kvp in _rowRemovalTokens)
        {
            try
            {
                kvp.Value.Cancel();
                kvp.Value.Dispose();
            }
            catch (ObjectDisposedException) { }
        }
        _rowRemovalTokens.Clear();
    }

    private Task UndoRowAsync(DoseRowViewModel row) =>
        RunAsync(async () =>
        {
            CancelRowRemoval(row.Id);
            DateTimeOffset now = _clock?.UtcNow ?? DateTimeOffset.UtcNow;
            row.State = DoseEventState.Scheduled;
            DoseTransitionResult result = await _undo.ExecuteAsync(row.Id);
            _deduplicator.RecordLocalChange<DoseEvent>(row.Id);
            _messenger.Send(new DoseEventStatusChangedMessage(
                new DoseEventChange(row.Id, DoseEventState.Scheduled, now, DoseEventChangeType.Update, row.MedicationId),
                ChangeSource.Local));
            _feedback.Notify(Describe(result, "Подтверждение отменено."));
            _logger?.LogInformation("[Today] Dose #{DoseId} confirmation undone", row.Id);
            await LoadDayAsync(CancellationToken.None);
        });

    private async Task LoadDayAsync(CancellationToken cancellationToken)
    {
        CancelAllRowRemovals();
        DayAgenda agenda = await _agenda.ExecuteAsync(cancellationToken);

        DayTitle = FormatDay(agenda.LocalDate);
        TimeZoneLabel = agenda.TimeZoneId;

        DateTimeOffset now = _clock?.UtcNow ?? DateTimeOffset.UtcNow;
        Items.Clear();
        foreach (DoseAgendaItem item in agenda.Items)
        {
            // Правило 1: после 3 часов если на напоминание не было ответа, оно удаляется
            bool isExpiredNoResponse = (item.State is DoseEventState.Scheduled or DoseEventState.Notified)
                && (now - item.ScheduledAt > TimeSpan.FromHours(3));

            // Правило 2: после 1 минуты если была реакция "пропустить", оно удаляется
            bool isExpiredSkipped = item.State == DoseEventState.Skipped
                && (now - item.ScheduledAt > TimeSpan.FromMinutes(1));

            if (isExpiredNoResponse || isExpiredSkipped)
            {
                continue;
            }

            Items.Add(new DoseRowViewModel(item, ConfirmRowAsync, SkipRowAsync, UndoRowAsync));
        }

        IsEmpty = Items.Count == 0;
        ProgressText = FormatProgress(agenda.Items);
    }

    private void CleanExpiredItems()
    {
        DateTimeOffset now = _clock?.UtcNow ?? DateTimeOffset.UtcNow;
        bool changed = false;
        for (int i = Items.Count - 1; i >= 0; i--)
        {
            DoseRowViewModel row = Items[i];
            // Правило 1: после 3 часов без ответа удаляется
            if ((row.State is DoseEventState.Scheduled or DoseEventState.Notified) && (now - row.ScheduledAt > TimeSpan.FromHours(3)))
            {
                CancelRowRemoval(row.Id);
                Items.RemoveAt(i);
                changed = true;
                _logger?.LogInformation("[Today] Removed unanswered dose #{DoseId} after 3 hours", row.Id);
            }
            // Правило 2: после 1 минуты после пропуска удаляется
            else if (row.State == DoseEventState.Skipped && row.SkippedAt is { } skippedAt && (now - skippedAt >= TimeSpan.FromMinutes(1)))
            {
                CancelRowRemoval(row.Id);
                Items.RemoveAt(i);
                changed = true;
                _logger?.LogInformation("[Today] Removed skipped dose #{DoseId} after 1 minute", row.Id);
            }
        }

        if (changed)
        {
            IsEmpty = Items.Count == 0;
        }
    }

    private void UpdateProgress()
    {
        if (Items.Count == 0)
        {
            ProgressText = string.Empty;
            return;
        }

        int taken = Items.Count(static item => item.State == DoseEventState.Taken);
        ProgressText = $"Принято {taken} из {Items.Count}";
    }

    internal Task? LastReloadTask { get; private set; }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
        CancelAllRowRemovals();
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
