using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Med.Application.Abstractions;
using Med.Domain.Entities;
using Med.Domain.Enums;

namespace Med.Presentation.Courses;

public sealed partial class CoursesViewModel : ViewModelBase
{
    private readonly ICourseRepository _courses;
    private readonly IScheduleRepository _schedules;
    private readonly IMedicationRepository _medications;
    private readonly IAuthService _auth;

    public CoursesViewModel(
        ICourseRepository courses,
        IScheduleRepository schedules,
        IMedicationRepository medications,
        IAuthService auth)
    {
        _courses = courses;
        _schedules = schedules;
        _medications = medications;
        _auth = auth;
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
            _ = LoadSchedulesAsync(value.Id);
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
            await _schedules.DeleteAsync(SelectedSchedule.Id, cancellationToken);
            await LoadSchedulesAsync(SelectedCourse.Id, cancellationToken);
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
}
