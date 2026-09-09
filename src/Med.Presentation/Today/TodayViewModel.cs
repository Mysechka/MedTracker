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
    internal IReadOnlyDictionary<Guid, CancellationTokenSource> RowRemovalTokens => _rowRemovalTokens;

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
            DoseEventState previousState = row.State;
            DateTimeOffset? previousTakenAt = row.TakenAt;
            try
            {
                CancelRowRemoval(row.Id);
                DateTimeOffset now = _clock?.UtcNow ?? DateTimeOffset.UtcNow;
                row.State = DoseEventState.Taken;
                row.TakenAt = now;
                DoseTransitionResult result = await _confirm.ExecuteAsync(row.Id);
                if (!result.IsApplied && !result.IsNoOp)
                {
                    row.State = result.State ?? previousState;
                    row.TakenAt = previousTakenAt;
                    _feedback.Notify(Describe(result, "Приём не был зафиксирован."));
                    return;
                }

                _deduplicator.RecordLocalChange<DoseEvent>(row.Id);
                _messenger.Send(new DoseEventStatusChangedMessage(
                    new DoseEventChange(row.Id, DoseEventState.Taken, now, DoseEventChangeType.Update, row.MedicationId),
                    ChangeSource.Local));
                _feedback.Notify(Describe(result, "Приём отмечен."));
                _logger?.LogInformation("[Today] Dose #{DoseId} marked as taken; scheduled removal in 3 seconds", row.Id);
                UpdateProgress();

                // Правило: после 3 секунд сообщение исчезает в прямом эфире
                CancelRowRemoval(row.Id);
                CancellationTokenSource cts = new();
                _rowRemovalTokens[row.Id] = cts;
                _ = ScheduleRowRemovalAsync(row, TimeSpan.FromSeconds(3), cts.Token);
            }
            catch
            {
                row.State = previousState;
                row.TakenAt = previousTakenAt;
                throw;
            }
        });

    private Task SkipRowAsync(DoseRowViewModel row) =>
        RunAsync(async () =>
        {
            DoseEventState previousState = row.State;
            DateTimeOffset? previousSkippedAt = row.SkippedAt;
            try
            {
                DateTimeOffset now = _clock?.UtcNow ?? DateTimeOffset.UtcNow;
                row.State = DoseEventState.Skipped;
                row.SkippedAt = now;
                DoseTransitionResult result = await _skip.ExecuteAsync(row.Id);
                if (!result.IsApplied && !result.IsNoOp)
                {
                    row.State = result.State ?? previousState;
                    row.SkippedAt = previousSkippedAt;
                    _feedback.Notify(Describe(result, "Пропуск не был зафиксирован."));
                    return;
                }

                _deduplicator.RecordLocalChange<DoseEvent>(row.Id);
                _messenger.Send(new DoseEventStatusChangedMessage(
                    new DoseEventChange(row.Id, DoseEventState.Skipped, now, DoseEventChangeType.Update, row.MedicationId),
                    ChangeSource.Local));
                _feedback.Notify(Describe(result, "Приём пропущен."));
                _logger?.LogInformation("[Today] Dose #{DoseId} skipped; scheduled removal in 30 seconds", row.Id);

                // Правило: после 30 секунд если на сообщение была реакция "пропустить", оно удаляется
                CancelRowRemoval(row.Id);
                CancellationTokenSource cts = new();
                _rowRemovalTokens[row.Id] = cts;
                _ = ScheduleRowRemovalAsync(row, TimeSpan.FromSeconds(30), cts.Token);
            }
            catch
            {
                row.State = previousState;
                row.SkippedAt = previousSkippedAt;
                throw;
            }
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
                if (Items.Contains(row))
                {
                    Items.Remove(row);
                    IsEmpty = Items.Count == 0;
                    _logger?.LogInformation("[Today] Auto-removed dose #{DoseId} (State: {State}) after delay", row.Id, row.State);
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
            row.TakenAt = null;
            row.SkippedAt = null;
            DoseTransitionResult result = await _undo.ExecuteAsync(row.Id);
            _deduplicator.RecordLocalChange<DoseEvent>(row.Id);
            _messenger.Send(new DoseEventStatusChangedMessage(
                new DoseEventChange(row.Id, DoseEventState.Scheduled, now, DoseEventChangeType.Update, row.MedicationId),
                ChangeSource.Local));
            _feedback.Notify(Describe(result, "Действие отменено."));
            _logger?.LogInformation("[Today] Dose #{DoseId} undone back to Scheduled", row.Id);
            UpdateProgress();
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

            // Правило 2: после 30 секунд если была реакция "пропустить", оно удаляется
            bool isExpiredSkipped = item.State == DoseEventState.Skipped
                && item.TakenAt is { } skippedAt && (now - skippedAt >= TimeSpan.FromSeconds(30));

            // Правило 3: после 3 секунд после приёма удаляется
            bool isExpiredTaken = item.State == DoseEventState.Taken
                && item.TakenAt is { } takenAt && (now - takenAt >= TimeSpan.FromSeconds(3));

            if (isExpiredNoResponse || isExpiredSkipped || isExpiredTaken)
            {
                continue;
            }

            var row = new DoseRowViewModel(item, ConfirmRowAsync, SkipRowAsync, UndoRowAsync);
            Items.Add(row);

            if (item.State == DoseEventState.Skipped && item.TakenAt is { } sAt)
            {
                TimeSpan remaining = TimeSpan.FromSeconds(30) - (now - sAt);
                if (remaining > TimeSpan.Zero)
                {
                    CancellationTokenSource cts = new();
                    _rowRemovalTokens[row.Id] = cts;
                    _ = ScheduleRowRemovalAsync(row, remaining, cts.Token);
                }
            }
            else if (item.State == DoseEventState.Taken && item.TakenAt is { } tAt)
            {
                TimeSpan remaining = TimeSpan.FromSeconds(3) - (now - tAt);
                if (remaining > TimeSpan.Zero)
                {
                    CancellationTokenSource cts = new();
                    _rowRemovalTokens[row.Id] = cts;
                    _ = ScheduleRowRemovalAsync(row, remaining, cts.Token);
                }
            }
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
            // Правило 2: после 30 секунд после пропуска удаляется
            else if (row.State == DoseEventState.Skipped && (row.SkippedAt is null || now - row.SkippedAt.Value >= TimeSpan.FromSeconds(30)))
            {
                CancelRowRemoval(row.Id);
                Items.RemoveAt(i);
                changed = true;
                _logger?.LogInformation("[Today] Removed skipped dose #{DoseId} after 30 seconds", row.Id);
            }
            // Правило 3: после 3 секунд после приёма удаляется
            else if (row.State == DoseEventState.Taken && (row.TakenAt is null || now - row.TakenAt.Value >= TimeSpan.FromSeconds(3)))
            {
                CancelRowRemoval(row.Id);
                Items.RemoveAt(i);
                changed = true;
                _logger?.LogInformation("[Today] Removed taken dose #{DoseId} after 3 seconds", row.Id);
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
            _logger?.LogError(ex, "[Today] Error during action: {Message}", ex.Message);
            _feedback.Notify(FormatErrorMessage(ex.Message));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string FormatErrorMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "Произошла ошибка.";
        }

        string trimmed = message.Trim();
        if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(trimmed);
                if (doc.RootElement.TryGetProperty("message", out var msgProp) && msgProp.GetString() is { Length: > 0 } msg)
                {
                    return msg switch
                    {
                        "insufficient inventory" => "Недостаточно остатка лекарства в аптечке.",
                        "inventory row missing for medication" => "Запись об остатке лекарства не найдена.",
                        "not authenticated" => "Сессия истекла. Войдите в аккаунт заново.",
                        _ => msg
                    };
                }
            }
            catch { }
        }

        return message;
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
