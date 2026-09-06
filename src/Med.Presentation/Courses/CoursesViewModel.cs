using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Med.Application.Abstractions;
using Med.Domain.Entities;
using Med.Domain.Enums;
using Med.Presentation.Abstractions;
using Med.Presentation.Messaging;
using Med.Presentation.Sync;

namespace Med.Presentation.Courses;

public sealed partial class CoursesViewModel : ViewModelBase,
    IRecipient<EntitySavedMessage<Medication>>,
    IRecipient<EntityDeletedMessage<Medication>>,
    IRecipient<EntitySavedMessage<Course>>,
    IRecipient<EntityDeletedMessage<Course>>,
    IRecipient<ScheduleUpdatedMessage>,
    IDisposable
{
    private readonly ICourseRepository _courses;
    private readonly IScheduleRepository _schedules;
    private readonly IMedicationRepository _medications;
    private readonly IAuthService _auth;
    private readonly IUiDispatcher _ui;
    private readonly IMessenger _messenger;
    private readonly EntityChangeDeduplicator _deduplicator;

    public CoursesViewModel(
        ICourseRepository courses,
        IScheduleRepository schedules,
        IMedicationRepository medications,
        IAuthService auth,
        IUiDispatcher? ui = null,
        IMessenger? messenger = null,
        EntityChangeDeduplicator? deduplicator = null)
    {
        _courses = courses;
        _schedules = schedules;
        _medications = medications;
        _auth = auth;
        _ui = ui ?? new ImmediateUiDispatcher();
        _messenger = messenger ?? WeakReferenceMessenger.Default;
        _deduplicator = deduplicator ?? new EntityChangeDeduplicator();

        _messenger.RegisterAll(this);
    }

    public ObservableCollection<Course> Courses { get; } = [];

    public ObservableCollection<Schedule> Schedules { get; } = [];

    public ObservableCollection<Medication> Medications { get; } = [];

    [ObservableProperty]
    private Course? _selectedCourse;

    [ObservableProperty]
    private Schedule? _selectedSchedule;

    [ObservableProperty]
    private Medication? _selectedMedication;

    [ObservableProperty]
    private string _startsOn = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

    [ObservableProperty]
    private string _durationDays = "14";

    [ObservableProperty]
    private string _doseAmount = "1";

    [ObservableProperty]
    private string _fixedTimes = "08:00,20:00";

    [ObservableProperty]
    private string _intervalHours = "8";

    [ObservableProperty]
    private string _intervalAnchor = "08:00";

    [ObservableProperty]
    private string _mealKind = "Breakfast";

    [ObservableProperty]
    private string _mealRelation = "Before";

    [ObservableProperty]
    private string _offsetMinutes = "30";

    [ObservableProperty]
    private string _scheduleType = "FixedTimes";

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    partial void OnSelectedCourseChanged(Course? value)
    {
        if (value is not null)
        {
            _ = LoadSchedulesSafelyAsync(value.Id);
        }
    }

    private async Task LoadSchedulesSafelyAsync(Guid courseId)
    {
        try
        {
            await LoadSchedulesAsync(courseId);
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await RunAsync(async () =>
        {
            Courses.Clear();
            Medications.Clear();
            foreach (Course course in await _courses.ListAsync(cancellationToken))
            {
                Courses.Add(course);
            }

            foreach (Medication med in await _medications.ListAsync(cancellationToken))
            {
                Medications.Add(med);
            }

            Message = $"Курсов: {Courses.Count}";
        });
    }

    [RelayCommand]
    private async Task SaveCourseAsync(CancellationToken cancellationToken)
    {
        if (SelectedMedication is null)
        {
            Message = "Выберите лекарство.";
            return;
        }

        if (!DateOnly.TryParse(StartsOn, out DateOnly startsOn))
        {
            Message = "Некорректная дата начала.";
            return;
        }

        if (!int.TryParse(DurationDays, out int days) || days <= 0)
        {
            Message = "Длительность должна быть > 0.";
            return;
        }

        await RunAsync(async () =>
        {
            Guid userId = _auth.CurrentUserId
                ?? throw new InvalidOperationException("Нужна сессия.");

            Course course = Course.Create(
                SelectedCourse?.Id ?? Guid.NewGuid(),
                userId,
                SelectedMedication.Id,
                startsOn,
                endsOn: startsOn.AddDays(days - 1),
                durationDays: days,
                isActive: true);

            await _courses.UpsertAsync(course, cancellationToken);
            _deduplicator.RecordLocalChange<Course>(course.Id);
            _messenger.Send(new EntitySavedMessage<Course>(course, ChangeSource.Local));
            _messenger.Send(new ScheduleUpdatedMessage(course.Id, course.MedicationId, ChangeSource.Local));

            SelectedCourse = course;
            await RefreshAsync(cancellationToken);
            Message = "Курс сохранён.";
        });
    }

    [RelayCommand]
    private async Task SaveScheduleAsync(CancellationToken cancellationToken)
    {
        if (SelectedCourse is null)
        {
            Message = "Выберите курс.";
            return;
        }

        if (!decimal.TryParse(DoseAmount, out decimal dose) || dose <= 0)
        {
            Message = "Доза должна быть > 0.";
            return;
        }

        await RunAsync(async () =>
        {
            Guid id = SelectedSchedule?.Id ?? Guid.NewGuid();
            Schedule schedule = ScheduleType switch
            {
                "Interval" => CreateInterval(id, SelectedCourse.Id, dose),
                "MealRelative" => CreateMealRelative(id, SelectedCourse.Id, dose),
                "AsNeeded" => Schedule.CreateAsNeeded(id, SelectedCourse.Id, dose),
                _ => CreateFixedTimes(id, SelectedCourse.Id, dose),
            };

            await _schedules.UpsertAsync(schedule, cancellationToken);
            _deduplicator.RecordLocalChange<Schedule>(schedule.Id);
            _messenger.Send(new EntitySavedMessage<Schedule>(schedule, ChangeSource.Local));
            _messenger.Send(new ScheduleUpdatedMessage(schedule.CourseId, Guid.Empty, ChangeSource.Local));

            await LoadSchedulesAsync(SelectedCourse.Id, cancellationToken);
            Message = $"Расписание {schedule.Type} сохранено.";
        });
    }

    [RelayCommand]
    private async Task DeleteScheduleAsync(CancellationToken cancellationToken)
    {
        if (SelectedSchedule is null || SelectedCourse is null)
        {
            Message = "Выберите расписание.";
            return;
        }

        await RunAsync(async () =>
        {
            Guid scheduleId = SelectedSchedule.Id;
            Guid courseId = SelectedCourse.Id;
            await _schedules.DeleteAsync(scheduleId, cancellationToken);
            _deduplicator.RecordLocalChange<Schedule>(scheduleId);
            _messenger.Send(new EntityDeletedMessage<Schedule>(scheduleId, ChangeSource.Local));
            _messenger.Send(new ScheduleUpdatedMessage(courseId, Guid.Empty, ChangeSource.Local));

            await LoadSchedulesAsync(courseId, cancellationToken);
            Message = "Расписание удалено.";
        });
    }

    private Schedule CreateFixedTimes(Guid id, Guid courseId, decimal dose)
    {
        List<TimeOnly> times = [];
        foreach (string part in FixedTimes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!TimeOnly.TryParse(part, out TimeOnly time))
            {
                throw new InvalidOperationException($"Некорректное время: {part}");
            }

            times.Add(time);
        }

        return Schedule.CreateFixedTimes(id, courseId, WeekDays.All, dose, times);
    }

    private Schedule CreateInterval(Guid id, Guid courseId, decimal dose)
    {
        if (!int.TryParse(IntervalHours, out int hours) || hours <= 0)
        {
            throw new InvalidOperationException("interval_hours должен быть > 0.");
        }

        if (!TimeOnly.TryParse(IntervalAnchor, out TimeOnly anchor))
        {
            throw new InvalidOperationException("Некорректный якорь Interval.");
        }

        return Schedule.CreateInterval(id, courseId, WeekDays.All, dose, hours, anchor);
    }

    private Schedule CreateMealRelative(Guid id, Guid courseId, decimal dose)
    {
        if (!Enum.TryParse(MealKind, ignoreCase: true, out MealKind mealKind))
        {
            throw new InvalidOperationException("Некорректный MealKind.");
        }

        if (!Enum.TryParse(MealRelation, ignoreCase: true, out MealRelation relation))
        {
            throw new InvalidOperationException("Некорректный MealRelation.");
        }

        if (!int.TryParse(OffsetMinutes, out int offset))
        {
            throw new InvalidOperationException("Некорректный offset_minutes.");
        }

        return Schedule.CreateMealRelative(id, courseId, WeekDays.All, dose, mealKind, relation, offset);
    }

    private async Task LoadSchedulesAsync(Guid courseId, CancellationToken cancellationToken = default)
    {
        Schedules.Clear();
        foreach (Schedule schedule in await _schedules.ListByCourseAsync(courseId, cancellationToken))
        {
            Schedules.Add(schedule);
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

    public void Receive(EntitySavedMessage<Medication> message)
    {
        if (message.Source != ChangeSource.Realtime)
        {
            return;
        }

        _ui.Post(() =>
        {
            Medication med = message.Value;
            Medication? existing = Medications.FirstOrDefault(m => m.Id == med.Id);
            if (existing is not null)
            {
                int index = Medications.IndexOf(existing);
                Medications[index] = med;
            }
            else
            {
                Medications.Add(med);
            }

            if (SelectedMedication?.Id == med.Id)
            {
                SelectedMedication = med;
            }
        });
    }

    public void Receive(EntityDeletedMessage<Medication> message)
    {
        if (message.Source != ChangeSource.Realtime)
        {
            return;
        }

        _ui.Post(() =>
        {
            Medication? existing = Medications.FirstOrDefault(m => m.Id == message.Value);
            if (existing is not null)
            {
                Medications.Remove(existing);
            }

            if (SelectedMedication?.Id == message.Value)
            {
                SelectedMedication = null;
            }
        });
    }

    public void Receive(EntitySavedMessage<Course> message)
    {
        if (message.Source != ChangeSource.Realtime)
        {
            return;
        }

        _ui.Post(() =>
        {
            Course course = message.Value;
            Course? existing = Courses.FirstOrDefault(c => c.Id == course.Id);
            if (existing is not null)
            {
                int index = Courses.IndexOf(existing);
                Courses[index] = course;
            }
            else
            {
                Courses.Add(course);
            }

            if (SelectedCourse?.Id == course.Id)
            {
                SelectedCourse = course;
            }
        });
    }

    public void Receive(EntityDeletedMessage<Course> message)
    {
        if (message.Source != ChangeSource.Realtime)
        {
            return;
        }

        _ui.Post(() =>
        {
            Course? existing = Courses.FirstOrDefault(c => c.Id == message.Value);
            if (existing is not null)
            {
                Courses.Remove(existing);
            }

            if (SelectedCourse?.Id == message.Value)
            {
                SelectedCourse = null;
                Schedules.Clear();
            }
        });
    }

    public void Receive(ScheduleUpdatedMessage message)
    {
        if (message.Source != ChangeSource.Realtime)
        {
            return;
        }

        if (SelectedCourse is not null && (message.Value.CourseId == Guid.Empty || message.Value.CourseId == SelectedCourse.Id))
        {
            _ui.Post(() => _ = LoadSchedulesSafelyAsync(SelectedCourse.Id));
        }
    }

    public void Dispose()
    {
        _messenger.UnregisterAll(this);
    }
}
