using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Med.Application.Agenda;
using Med.Domain.Enums;

namespace Med.Presentation.Today;

/// <summary>
/// Строка расписания дня. Доступность действий считается по состоянию дозы здесь,
/// а не в разметке: View только скрывает кнопку по флагу.
/// </summary>
public sealed partial class DoseRowViewModel : ObservableObject
{
    private const string UnknownMedication = "Лекарство недоступно";

    private readonly Func<DoseRowViewModel, Task> _confirm;
    private readonly Func<DoseRowViewModel, Task> _skip;
    private readonly Func<DoseRowViewModel, Task> _undo;

    public DoseRowViewModel(
        DoseAgendaItem item,
        Func<DoseRowViewModel, Task> confirm,
        Func<DoseRowViewModel, Task> skip,
        Func<DoseRowViewModel, Task> undo)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(confirm);
        ArgumentNullException.ThrowIfNull(skip);
        ArgumentNullException.ThrowIfNull(undo);

        _confirm = confirm;
        _skip = skip;
        _undo = undo;

        Id = item.DoseEventId;
        CourseId = item.CourseId;
        ScheduleId = item.ScheduleId;
        MedicationId = item.MedicationId;
        ScheduledAt = item.ScheduledAt;
        _state = item.State;
        Time = item.LocalTime.ToString("HH:mm", CultureInfo.InvariantCulture);
        Title = string.IsNullOrWhiteSpace(item.MedicationName) ? UnknownMedication : item.MedicationName;
        Details = FormatDetails(item);
        TakenAt = item.State == DoseEventState.Taken ? item.TakenAt : null;
        SkippedAt = item.State == DoseEventState.Skipped ? item.TakenAt : null;
    }

    public Guid Id { get; }

    public Guid CourseId { get; }

    public Guid ScheduleId { get; }

    public Guid? MedicationId { get; }

    public DateTimeOffset ScheduledAt { get; }

    public DateTimeOffset? TakenAt { get; set; }

    public DateTimeOffset? SkippedAt { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    [NotifyPropertyChangedFor(nameof(CanSkip))]
    [NotifyPropertyChangedFor(nameof(CanUndo))]
    [NotifyPropertyChangedFor(nameof(IsTaken))]
    [NotifyPropertyChangedFor(nameof(IsSkipped))]
    [NotifyPropertyChangedFor(nameof(StateText))]
    private DoseEventState _state;

    partial void OnStateChanged(DoseEventState value)
    {
        ConfirmCommand.NotifyCanExecuteChanged();
        SkipCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
    }

    public string Time { get; }

    public string Title { get; }

    /// <summary>Доза и дозировка одной строкой; пусто, если данных лекарства нет.</summary>
    public string Details { get; }

    public string StateText => Describe(State);

    public bool CanConfirm => State is DoseEventState.Scheduled or DoseEventState.Notified;

    public bool CanSkip => CanConfirm;

    public bool CanUndo => State is DoseEventState.Taken or DoseEventState.Skipped;

    public bool IsTaken => State == DoseEventState.Taken;

    public bool IsSkipped => State == DoseEventState.Skipped;

    public bool HasDetails => Details.Length > 0;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private Task ConfirmAsync() => _confirm(this);

    [RelayCommand(CanExecute = nameof(CanSkip))]
    private Task SkipAsync() => _skip(this);

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private Task UndoAsync() => _undo(this);

    private static string FormatDetails(DoseAgendaItem item)
    {
        List<string> parts = [];

        if (item.DoseAmount is { } amount && !string.IsNullOrWhiteSpace(item.Unit))
        {
            parts.Add($"{amount.ToString("0.##", CultureInfo.CurrentCulture)} {item.Unit}");
        }

        if (!string.IsNullOrWhiteSpace(item.Dosage))
        {
            parts.Add(item.Dosage);
        }

        return string.Join(" · ", parts);
    }

    private static string Describe(DoseEventState state) => state switch
    {
        DoseEventState.Scheduled => "Запланировано",
        DoseEventState.Notified => "Напоминание отправлено",
        DoseEventState.Taken => "Принято",
        DoseEventState.Skipped => "Пропущено",
        DoseEventState.Missed => "Просрочено",
        DoseEventState.Cancelled => "Отменено",
        _ => state.ToString(),
    };
}
