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
    private readonly INotificationService? _notifications;
    private readonly ISystemClock? _clock;
    private readonly ILogger<TodayViewModel>? _logger;
    private int _totalDosesToday;
    private IReadOnlyList<DoseAgendaItem> _rawAgendaItems = [];

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
            bool previousIsCompleted = row.IsCompleted;
            try
            {
                DateTimeOffset now = _clock?.UtcNow ?? DateTimeOffset.UtcNow;
                row.State = DoseEventState.Taken;
                row.TakenAt = now;
                row.IsCompleted = true;
                UpdateRawItemState(row.Id, DoseEventState.Taken, now);
                DoseTransitionResult result = await _confirm.ExecuteAsync(row.Id);
                if (!result.IsApplied && !result.IsNoOp)
                {
                    row.State = result.State ?? previousState;
                    row.TakenAt = previousTakenAt;
                    row.IsCompleted = previousIsCompleted;
                    UpdateRawItemState(row.Id, row.State, row.TakenAt);
                    _feedback.Notify(Describe(result, "Приём не был зафиксирован."));
                    return;
                }

                _deduplicator.RecordLocalChange<DoseEvent>(row.Id);
                _messenger.Send(new DoseEventStatusChangedMessage(
                    new DoseEventChange(row.Id, DoseEventState.Taken, now, DoseEventChangeType.Update, row.MedicationId),
                    ChangeSource.Local));
                _ = _notifications?.CancelAsync(row.Id.ToString());
                _ = _notifications?.CancelAsync($"{row.Id}_advance");
                _feedback.Notify(Describe(result, "Приём отмечен."));
                _logger?.LogInformation("[Today] Dose #{DoseId} marked as taken", row.Id);
                UpdateProgress();
            }
            catch
            {
                row.State = previousState;
                row.TakenAt = previousTakenAt;
                row.IsCompleted = previousIsCompleted;
                UpdateRawItemState(row.Id, previousState, previousTakenAt);
                throw;
            }
        });

    private Task SkipRowAsync(DoseRowViewModel row) =>
        RunAsync(async () =>
        {
            DoseEventState previousState = row.State;
            DateTimeOffset? previousSkippedAt = row.SkippedAt;
            bool previousIsCompleted = row.IsCompleted;
            try
            {
                DateTimeOffset now = _clock?.UtcNow ?? DateTimeOffset.UtcNow;
                row.State = DoseEventState.Skipped;
                row.SkippedAt = now;
                row.IsCompleted = true;
                UpdateRawItemState(row.Id, DoseEventState.Skipped, now);
                DoseTransitionResult result = await _skip.ExecuteAsync(row.Id);
                if (!result.IsApplied && !result.IsNoOp)
                {
                    row.State = result.State ?? previousState;
                    row.SkippedAt = previousSkippedAt;
                    row.IsCompleted = previousIsCompleted;
                    UpdateRawItemState(row.Id, row.State, row.SkippedAt);
                    _feedback.Notify(Describe(result, "Пропуск не был зафиксирован."));
                    return;
                }

                _deduplicator.RecordLocalChange<DoseEvent>(row.Id);
                _messenger.Send(new DoseEventStatusChangedMessage(
                    new DoseEventChange(row.Id, DoseEventState.Skipped, now, DoseEventChangeType.Update, row.MedicationId),
                    ChangeSource.Local));
                _ = _notifications?.CancelAsync(row.Id.ToString());
                _ = _notifications?.CancelAsync($"{row.Id}_advance");
                _feedback.Notify(Describe(result, "Приём пропущен."));
                _logger?.LogInformation("[Today] Dose #{DoseId} skipped", row.Id);
                UpdateProgress();
            }
            catch
            {
                row.State = previousState;
                row.SkippedAt = previousSkippedAt;
                row.IsCompleted = previousIsCompleted;
                UpdateRawItemState(row.Id, previousState, previousSkippedAt);
                throw;
            }
        });

    private Task UndoRowAsync(DoseRowViewModel row) =>
        RunAsync(async () =>
        {
            DoseEventState previousState = row.State;
            DateTimeOffset? previousTakenAt = row.TakenAt;
            DateTimeOffset? previousSkippedAt = row.SkippedAt;
            bool previousIsCompleted = row.IsCompleted;
            try
            {
                DateTimeOffset now = _clock?.UtcNow ?? DateTimeOffset.UtcNow;
                row.State = DoseEventState.Scheduled;
                row.TakenAt = null;
                row.SkippedAt = null;
                row.IsCompleted = false;
                UpdateRawItemState(row.Id, DoseEventState.Scheduled, null);
                DoseTransitionResult result = await _undo.ExecuteAsync(row.Id);
                if (!result.IsApplied && !result.IsNoOp)
                {
                    row.State = result.State ?? previousState;
                    row.TakenAt = previousTakenAt;
                    row.SkippedAt = previousSkippedAt;
                    row.IsCompleted = previousIsCompleted;
                    UpdateRawItemState(row.Id, row.State, row.TakenAt ?? row.SkippedAt);
                    _feedback.Notify(Describe(result, "Действие не может быть отменено."));
                    return;
                }

                _deduplicator.RecordLocalChange<DoseEvent>(row.Id);
                _messenger.Send(new DoseEventStatusChangedMessage(
                    new DoseEventChange(row.Id, DoseEventState.Scheduled, now, DoseEventChangeType.Update, row.MedicationId),
                    ChangeSource.Local));
                if (_notifications is not null)
                {
                    DateTimeOffset advanceTime = row.ScheduledAt - TimeSpan.FromMinutes(5);
                    if (advanceTime > now)
                    {
                        string advanceTitle = $"Через 5 минут приём: {row.Title}";
                        string advanceBody = string.IsNullOrWhiteSpace(row.Details)
                            ? $"В {row.Time} запланирован приём"
                            : $"В {row.Time} · {row.Details}";
                        _ = _notifications.ScheduleAsync($"{row.Id}_advance", advanceTitle, advanceBody, advanceTime);
                    }

                    if (row.ScheduledAt > now)
                    {
                        string title = $"Время принять {row.Title}";
                        string body = string.IsNullOrWhiteSpace(row.Details) ? "Напоминание о приёме лекарства" : row.Details;
                        _ = _notifications.ScheduleAsync(row.Id.ToString(), title, body, row.ScheduledAt);
                    }
                }
                _feedback.Notify(Describe(result, "Действие отменено."));
                _logger?.LogInformation("[Today] Dose #{DoseId} undone back to Scheduled", row.Id);
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

        _rawAgendaItems = agenda.Items;
        _totalDosesToday = agenda.Items.Count;
        DateTimeOffset now = _clock?.UtcNow ?? DateTimeOffset.UtcNow;

        ScheduleNotifications(agenda.Items, now, cancellationToken);
        RebuildVisibleItems(now);
    }

    private void ScheduleNotifications(IReadOnlyList<DoseAgendaItem> items, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (_notifications is null)
        {
            return;
        }

        foreach (DoseAgendaItem item in items)
        {
            if (item.State != DoseEventState.Scheduled)
            {
                continue;
            }

            string medName = item.MedicationName ?? "препарат";
            string details = string.Join(" · ", new[] { $"{item.DoseAmount?.ToString("G", CultureInfo.InvariantCulture)} {item.Unit}".Trim(), item.MedicationForm }.Where(s => !string.IsNullOrWhiteSpace(s)));
            string body = string.IsNullOrWhiteSpace(details) ? "Напоминание о приёме" : details;

            // 1. Предварительное упоминание за 5 минут
            DateTimeOffset advanceTime = item.ScheduledAt - TimeSpan.FromMinutes(5);
            if (advanceTime > now)
            {
                string advanceTitle = $"Через 5 минут приём: {medName}";
                string advanceBody = string.IsNullOrWhiteSpace(details)
                    ? $"В {item.LocalTime:HH:mm} запланирован приём"
                    : $"В {item.LocalTime:HH:mm} · {details}";
                _ = _notifications.ScheduleAsync($"{item.DoseEventId}_advance", advanceTitle, advanceBody, advanceTime, cancellationToken);
            }

            // 2. Уведомление в момент приёма
            if (item.ScheduledAt > now)
            {
                string title = $"Время принять {medName}";
                _ = _notifications.ScheduleAsync(item.DoseEventId.ToString(), title, body, item.ScheduledAt, cancellationToken);
            }
        }
    }

    private void RebuildVisibleItems(DateTimeOffset now)
    {
        Items.Clear();
        foreach (DoseAgendaItem item in _rawAgendaItems)
        {
            if (IsItemVisible(item, now))
            {
                var row = new DoseRowViewModel(item, ConfirmRowAsync, SkipRowAsync, UndoRowAsync, now);
                Items.Add(row);
            }
        }

        IsEmpty = Items.Count == 0;
        UpdateProgress();
    }

    private static bool IsItemVisible(DoseAgendaItem item, DateTimeOffset now)
    {
        // 1. Принятые и пропущенные карточки остаются в списке
        if (item.State is DoseEventState.Taken or DoseEventState.Skipped)
        {
            return true;
        }

        // 2. Правило: после 3 часов если на напоминание не было ответа, оно не выводится в повестку
        if (now - item.ScheduledAt > TimeSpan.FromHours(3))
        {
            return false;
        }

        // 3. Правило: показывать напоминание ТОЛЬКО за 1 час до времени приёма (или если уже пора)
        if (item.ScheduledAt - now > TimeSpan.FromHours(1))
        {
            return false;
        }

        return true;
    }

    private void UpdateRawItemState(Guid doseId, DoseEventState state, DateTimeOffset? actionAt)
    {
        _rawAgendaItems = _rawAgendaItems
            .Select(i => i.DoseEventId == doseId ? i with { State = state, TakenAt = actionAt } : i)
            .ToList();
    }

    private void UpdateProgress()
    {
        int total = _totalDosesToday;
        int taken = Items.Count(static x => x.IsTaken);
        ProgressText = total > 0 ? $"Принято {taken} из {total}" : string.Empty;
        ProgressValue = total > 0 ? (double)taken / total : 0;
    }

    internal Task? LastReloadTask { get; private set; }

    private CancellationTokenSource? _timerCts;

    private void StartPeriodicTimingCheck()
    {
        _timerCts?.Cancel();
        _timerCts?.Dispose();
        _timerCts = new CancellationTokenSource();
        var token = _timerCts.Token;
        var weakRef = new WeakReference<TodayViewModel>(this);

        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await timer.WaitForNextTickAsync(token).ConfigureAwait(false);
                    if (!weakRef.TryGetTarget(out var target))
                    {
                        break;
                    }
                    target._ui.Post(target.UpdateRowTimings);
                }
                catch (OperationCanceledException)
                {
                    break;
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

        var eligibleIds = _rawAgendaItems.Where(i => IsItemVisible(i, now)).Select(i => i.DoseEventId).ToHashSet();
        var currentIds = Items.Select(i => i.Id).ToHashSet();

        if (!eligibleIds.SetEquals(currentIds))
        {
            RebuildVisibleItems(now);
            return;
        }

        foreach (DoseRowViewModel item in Items)
        {
            item.UpdateTiming(now);
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
}
