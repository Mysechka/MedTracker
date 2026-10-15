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
    private readonly INotificationService? _notifications;
    private readonly ISystemClock? _clock;
    private readonly ILogger<TodayViewModel>? _logger;
    private int _totalDosesToday;
    private List<DoseAgendaItem> _rawAgendaItems = [];
    private CancellationTokenSource? _timerCts;

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
        INotificationService? notifications = null,
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
        _notifications = notifications;
        _clock = clock;
        _logger = logger;

        _messenger.RegisterAll(this);
        StartPeriodicTimingCheck();
    }

    public ObservableCollection<DoseRowViewModel> Items { get; } = [];

    [ObservableProperty]
    private string _dayTitle = string.Empty;

    [ObservableProperty]
    private string _timeZoneLabel = string.Empty;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>День загружен и приёмов в нём нет — основание показать пустое состояние.</summary>
    [ObservableProperty]
    private bool _isEmpty;

    public bool IsAuthenticated => _auth.CurrentUserId is not null;

    internal Task? LastReloadTask { get; private set; }

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
        TransitionRowAsync(
            row,
            DoseEventState.Taken,
            (id, ct) => _confirm.ExecuteAsync(id, DoseEventSource.App, takenAt: null, ct),
            "Приём отмечен.",
            "Приём не был зафиксирован.");

    private Task SkipRowAsync(DoseRowViewModel row) =>
        TransitionRowAsync(
            row,
            DoseEventState.Skipped,
            (id, ct) => _skip.ExecuteAsync(id, DoseEventSource.App, skippedAt: null, ct),
            "Приём пропущен.",
            "Пропуск не был зафиксирован.");

    private Task UndoRowAsync(DoseRowViewModel row) =>
        TransitionRowAsync(
            row,
            DoseEventState.Scheduled,
            (id, ct) => _undo.ExecuteAsync(id, undoneAt: null, ct),
            "Действие отменено.",
            "Действие не может быть отменено.");

    private Task TransitionRowAsync(
        DoseRowViewModel row,
        DoseEventState targetState,
        Func<Guid, CancellationToken, Task<DoseTransitionResult>> executeUseCase,
        string appliedMessage,
        string rejectedFallback) =>
        RunAsync(async () =>
        {
            DoseEventState previousState = row.State;
            DateTimeOffset? previousTakenAt = row.TakenAt;
            DateTimeOffset? previousSkippedAt = row.SkippedAt;
            bool previousIsCompleted = row.IsCompleted;

            try
            {
                DateTimeOffset now = _clock?.UtcNow ?? DateTimeOffset.UtcNow;
                bool isCompleting = targetState is DoseEventState.Taken or DoseEventState.Skipped;

                row.State = targetState;
                row.TakenAt = targetState == DoseEventState.Taken ? now : null;
                row.SkippedAt = targetState == DoseEventState.Skipped ? now : null;
                row.IsCompleted = isCompleting;
                UpdateRawItemState(row.Id, targetState, isCompleting ? now : null);

                DoseTransitionResult result = await executeUseCase(row.Id, CancellationToken.None);
                if (!result.IsApplied && !result.IsNoOp)
                {
                    row.State = result.State ?? previousState;
                    row.TakenAt = previousTakenAt;
                    row.SkippedAt = previousSkippedAt;
                    row.IsCompleted = previousIsCompleted;
                    UpdateRawItemState(row.Id, row.State, row.TakenAt ?? row.SkippedAt);
                    _feedback.Notify(Describe(result, rejectedFallback));
                    return;
                }

                _deduplicator.RecordLocalChange<DoseEvent>(row.Id);
                _messenger.Send(new DoseEventStatusChangedMessage(
                    new DoseEventChange(row.Id, targetState, now, DoseEventChangeType.Update, row.MedicationId),
                    ChangeSource.Local));

                if (isCompleting)
                {
                    _ = _notifications?.CancelAsync(row.Id.ToString());
                    _ = _notifications?.CancelAsync($"{row.Id}_advance");
                }
                else
                {
                    ScheduleDoseNotification(row.Id, row.Title, row.Details, row.Time, row.ScheduledAt, now);
                }

                _feedback.Notify(Describe(result, appliedMessage));
                _logger?.LogInformation("[Today] Dose #{DoseId} transitioned to {State}", row.Id, targetState);
                UpdateProgress();
            }
            catch
            {
                row.State = previousState;
                row.TakenAt = previousTakenAt;
                row.SkippedAt = previousSkippedAt;
                row.IsCompleted = previousIsCompleted;
                UpdateRawItemState(row.Id, previousState, previousTakenAt ?? previousSkippedAt);
                throw;
            }
        });

    private async Task LoadDayAsync(CancellationToken cancellationToken)
    {
        DayAgenda agenda = await _agenda.ExecuteAsync(cancellationToken);

        DayTitle = FormatDay(agenda.LocalDate);
        TimeZoneLabel = agenda.TimeZoneId;

        _rawAgendaItems = agenda.Items as List<DoseAgendaItem> ?? [.. agenda.Items];
        _totalDosesToday = _rawAgendaItems.Count;
        DateTimeOffset now = _clock?.UtcNow ?? DateTimeOffset.UtcNow;

        ScheduleNotifications(_rawAgendaItems, now, cancellationToken);
        RebuildVisibleItems(now);
    }

    private void ScheduleNotifications(IReadOnlyList<DoseAgendaItem> items, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (_notifications is null)
        {
            return;
        }

        for (int i = 0; i < items.Count; i++)
        {
            DoseAgendaItem item = items[i];
            if (item.State != DoseEventState.Scheduled)
            {
                continue;
            }

            string medName = item.MedicationName ?? "препарат";
            string dosePart = $"{item.DoseAmount?.ToString("G", CultureInfo.InvariantCulture)} {item.Unit}".Trim();
            string details = string.IsNullOrWhiteSpace(item.MedicationForm)
                ? dosePart
                : (string.IsNullOrWhiteSpace(dosePart) ? item.MedicationForm : $"{dosePart} · {item.MedicationForm}");

            ScheduleDoseNotification(
                item.DoseEventId,
                medName,
                details,
                item.LocalTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                item.ScheduledAt,
                now,
                cancellationToken);
        }
    }

    private void ScheduleDoseNotification(
        Guid doseId,
        string medName,
        string? details,
        string timeLabel,
        DateTimeOffset scheduledAt,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (_notifications is null)
        {
            return;
        }

        DateTimeOffset advanceTime = scheduledAt - TimeSpan.FromMinutes(5);
        if (advanceTime > now)
        {
            string advanceTitle = $"Через 5 минут приём: {medName}";
            string advanceBody = string.IsNullOrWhiteSpace(details)
                ? $"В {timeLabel} запланирован приём"
                : $"В {timeLabel} · {details}";
            _ = _notifications.ScheduleAsync($"{doseId}_advance", advanceTitle, advanceBody, advanceTime, cancellationToken);
        }

        if (scheduledAt > now)
        {
            string title = $"Время принять {medName}";
            string body = string.IsNullOrWhiteSpace(details) ? "Напоминание о приёме" : details;
            _ = _notifications.ScheduleAsync(doseId.ToString(), title, body, scheduledAt, cancellationToken);
        }
    }

    private void RebuildVisibleItems(DateTimeOffset now)
    {
        Items.Clear();
        for (int i = 0; i < _rawAgendaItems.Count; i++)
        {
            DoseAgendaItem item = _rawAgendaItems[i];
            if (IsItemVisible(item, now))
            {
                Items.Add(new DoseRowViewModel(item, ConfirmRowAsync, SkipRowAsync, UndoRowAsync, now));
            }
        }

        IsEmpty = Items.Count == 0;
        UpdateProgress();
    }

    private static bool IsItemVisible(DoseAgendaItem item, DateTimeOffset now) =>
        item.State is DoseEventState.Taken or DoseEventState.Skipped
        || (now - item.ScheduledAt <= TimeSpan.FromHours(3) && item.ScheduledAt - now <= TimeSpan.FromHours(1));

    private void UpdateRawItemState(Guid doseId, DoseEventState state, DateTimeOffset? actionAt)
    {
        for (int i = 0; i < _rawAgendaItems.Count; i++)
        {
            if (_rawAgendaItems[i].DoseEventId == doseId)
            {
                _rawAgendaItems[i] = _rawAgendaItems[i] with { State = state, TakenAt = actionAt };
                break;
            }
        }
    }

    private void UpdateProgress()
    {
        int total = _totalDosesToday;
        if (total <= 0)
        {
            ProgressText = string.Empty;
            ProgressValue = 0;
            return;
        }

        int taken = 0;
        for (int i = 0; i < Items.Count; i++)
        {
            if (Items[i].IsTaken)
            {
                taken++;
            }
        }

        ProgressText = $"Принято {taken} из {total}";
        ProgressValue = (double)taken / total;
    }

    private void StartPeriodicTimingCheck()
    {
        _timerCts?.Cancel();
        _timerCts?.Dispose();
        _timerCts = new CancellationTokenSource();
        CancellationToken token = _timerCts.Token;
        var weakRef = new WeakReference<TodayViewModel>(this);

        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await timer.WaitForNextTickAsync(token).ConfigureAwait(false);
                    if (!weakRef.TryGetTarget(out TodayViewModel? target))
                    {
                        break;
                    }
                    target._ui.Post(target.UpdateRowTimings);
                }
                catch
                {
                    break;
                }
            }
        }, token);
    }

    private void UpdateRowTimings()
    {
        DateTimeOffset now = _clock?.UtcNow ?? DateTimeOffset.UtcNow;

        int visibleIdx = 0;
        bool mismatch = false;
        for (int i = 0; i < _rawAgendaItems.Count; i++)
        {
            DoseAgendaItem raw = _rawAgendaItems[i];
            if (!IsItemVisible(raw, now))
            {
                continue;
            }

            if (visibleIdx >= Items.Count || Items[visibleIdx].Id != raw.DoseEventId)
            {
                mismatch = true;
                break;
            }
            visibleIdx++;
        }

        if (mismatch || visibleIdx != Items.Count)
        {
            RebuildVisibleItems(now);
            return;
        }

        for (int i = 0; i < Items.Count; i++)
        {
            Items[i].UpdateTiming(now);
        }
    }

    public void Dispose()
    {
        _timerCts?.Cancel();
        _timerCts?.Dispose();
        _messenger.UnregisterAll(this);
    }

    public void Receive(DoseEventStatusChangedMessage message)
    {
        if (message.Source == ChangeSource.Realtime)
        {
            _ui.Post(() => LastReloadTask = ReloadQuietAsync());
        }
    }

    public void Receive(ScheduleUpdatedMessage message) =>
        _ui.Post(() => LastReloadTask = ReloadWithMaterializeAsync());

    public void Receive(EntitySavedMessage<Course> message) =>
        _ui.Post(() => LastReloadTask = ReloadWithMaterializeAsync());

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

    private static string Describe(DoseTransitionResult result, string appliedText) =>
        result.IsApplied ? appliedText :
        result.IsNoOp ? "Состояние уже было таким — ничего не изменилось." :
        result.Reason is { Length: > 0 } reason
            ? $"Действие отклонено: {reason}"
            : "Действие отклонено: состояние приёма уже изменилось.";

    private static string FormatDay(DateOnly localDate) =>
        localDate.ToDateTime(TimeOnly.MinValue).ToString("dddd, d MMMM", CultureInfo.CurrentCulture);
}
