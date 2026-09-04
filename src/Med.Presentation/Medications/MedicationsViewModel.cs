using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Med.Application.Abstractions;
using Med.Application.UseCases;
using Med.Domain.Entities;
using Med.Domain.Enums;
using Med.Domain.ValueObjects;
using Med.Presentation.Feedback;

namespace Med.Presentation.Medications;

public sealed partial class MedicationsViewModel : ViewModelBase
{
    private readonly IMedicationRepository _medications;
    private readonly IInventoryRepository _inventory;
    private readonly ICourseRepository _courses;
    private readonly IScheduleRepository _schedules;
    private readonly IAuthService _auth;
    private readonly RestockInventoryUseCase _restock;
    private readonly UserFeedback _feedback;

    public MedicationsViewModel(
        IMedicationRepository medications,
        IInventoryRepository inventory,
        ICourseRepository courses,
        IScheduleRepository schedules,
        IAuthService auth,
        RestockInventoryUseCase restock,
        UserFeedback feedback)
    {
        _medications = medications;
        _inventory = inventory;
        _courses = courses;
        _schedules = schedules;
        _auth = auth;
        _restock = restock;
        _feedback = feedback;
    }

    public ObservableCollection<MedicationCardViewModel> Items { get; } = [];

    [ObservableProperty]
    private Medication? _selected;

    [ObservableProperty]
    private int _checkboxCount = 2;

    public IReadOnlyList<int> AvailableCheckboxCounts { get; } = [1, 2, 3, 4, 5, 6, 7, 8, 9];

    [ObservableProperty]
    private bool _isEmpty = true;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _form = "tablet";

    [ObservableProperty]
    private string _dosage = "1";

    [ObservableProperty]
    private string _unit = "шт";

    [ObservableProperty]
    private string _barcode = string.Empty;

    [ObservableProperty]
    private string _notes = string.Empty;

    [ObservableProperty]
    private string _quantityOnHand = "0";

    [ObservableProperty]
    private string _lowStockThreshold = "0";

    [ObservableProperty]
    private string _restockAmount = "10";

    [ObservableProperty]
    private bool _timeMorning;

    [ObservableProperty]
    private bool _timeAfternoon;

    [ObservableProperty]
    private bool _timeEvening;

    [ObservableProperty]
    private bool _mealBreakfast;

    [ObservableProperty]
    private bool _mealLunch;

    [ObservableProperty]
    private bool _mealDinner;

    [ObservableProperty]
    private bool _useFixedTime;

    [ObservableProperty]
    private string _fixedTimes = "08:00";

    [ObservableProperty]
    private bool _isBusy;

    partial void OnSelectedChanged(Medication? value)
    {
        if (value is null)
        {
            return;
        }

        Name = value.Name;
        Form = value.Form;
        Dosage = value.Dosage;
        Unit = value.Unit;
        Barcode = value.Barcode ?? string.Empty;
        Notes = value.Notes ?? string.Empty;
        CheckboxCount = MedicationCardViewModel.ParseSlots(value.Barcode);
        _ = LoadInventoryAsync(value.Id);
    }

    partial void OnCheckboxCountChanged(int value)
    {
        if (value < 1)
        {
            _checkboxCount = 1;
        }
        else if (value > 9)
        {
            _checkboxCount = 9;
        }
    }

    [RelayCommand]
    private void IncrementCheckboxes()
    {
        if (CheckboxCount < 9)
        {
            CheckboxCount++;
        }
    }

    [RelayCommand]
    private void DecrementCheckboxes()
    {
        if (CheckboxCount > 1)
        {
            CheckboxCount--;
        }
    }

    [RelayCommand]
    private void SetCheckboxCount(int count)
    {
        CheckboxCount = Math.Clamp(count, 1, 9);
    }

    [ObservableProperty]
    private bool _isAdding;

    [RelayCommand]
    private void StartNew()
    {
        Selected = null;
        ClearForm();
        IsAdding = true;
    }

    [RelayCommand]
    private void BackToList()
    {
        IsAdding = false;
    }

    [RelayCommand]
    private void EditMedication(object? item)
    {
        if (item is MedicationCardViewModel card)
        {
            Selected = card.Medication;
        }
        else if (item is Medication med)
        {
            Selected = med;
        }
        IsAdding = true;
    }

    [RelayCommand]
    private void ClearName() => Name = string.Empty;

    [RelayCommand]
    private void ClearNotes() => Notes = string.Empty;

    [RelayCommand]
    private void ClearDosage() => Dosage = string.Empty;

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (_auth.CurrentUserId is null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            Items.Clear();
            foreach (Medication med in await _medications.ListAsync(cancellationToken))
            {
                Items.Add(new MedicationCardViewModel(med));
            }

            IsEmpty = Items.Count == 0;
            _feedback.Notify($"Лекарств: {Items.Count}");
        });
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        await RunAsync(async () =>
        {
            Guid userId = _auth.CurrentUserId
                ?? throw new InvalidOperationException("Нужна сессия.");

            bool isNew = Selected is null;
            Guid id = Selected?.Id ?? Guid.NewGuid();
            string barcodeValue = $"slots:{CheckboxCount}";
            Medication medication = Medication.Create(
                id,
                userId,
                Name,
                Form,
                Dosage,
                Unit,
                barcodeValue,
                string.IsNullOrWhiteSpace(Notes) ? null : Notes);

            await _medications.UpsertAsync(medication, cancellationToken);

            if (!decimal.TryParse(QuantityOnHand, out decimal qty))
            {
                qty = 0;
            }

            if (!decimal.TryParse(LowStockThreshold, out decimal threshold))
            {
                threshold = 0;
            }

            Inventory? existing = await _inventory.GetByMedicationAsync(id, cancellationToken);
            Inventory inventory = Inventory.Create(
                existing?.Id ?? Guid.NewGuid(),
                userId,
                id,
                qty,
                threshold);
            await _inventory.UpsertAsync(inventory, cancellationToken);

            if (isNew && HasScheduleTags())
            {
                await CreateInitialCourseAndSchedulesAsync(userId, medication, cancellationToken);
            }

            Selected = medication;
            IsAdding = false;
            await RefreshAsync(cancellationToken);
            _feedback.Notify("Лекарство сохранено.");
        });
    }

    [RelayCommand]
    private async Task DeleteAsync(CancellationToken cancellationToken)
    {
        if (Selected is null)
        {
            _feedback.Notify("Выберите лекарство.");
            return;
        }

        await RunAsync(async () =>
        {
            await _medications.DeleteAsync(Selected.Id, cancellationToken);
            Selected = null;
            ClearForm();
            await RefreshAsync(cancellationToken);
            _feedback.Notify("Лекарство удалено.");
        });
    }

    [RelayCommand]
    private async Task RestockAsync(CancellationToken cancellationToken)
    {
        if (Selected is null)
        {
            _feedback.Notify("Выберите лекарство.");
            return;
        }

        if (!decimal.TryParse(RestockAmount, out decimal amount) || amount <= 0)
        {
            _feedback.Notify("Количество пополнения должно быть > 0.");
            return;
        }

        await RunAsync(async () =>
        {
            InventoryCommandResult result = await _restock.ExecuteAsync(
                Selected.Id,
                amount,
                note: "UI restock",
                cancellationToken);
            _feedback.Notify($"Остаток: {result.QuantityOnHand}");
            await LoadInventoryAsync(Selected.Id);
        });
    }

    private bool HasScheduleTags() =>
        UseFixedTime || TimeMorning || TimeAfternoon || TimeEvening
        || MealBreakfast || MealLunch || MealDinner;

    private async Task CreateInitialCourseAndSchedulesAsync(
        Guid userId,
        Medication medication,
        CancellationToken cancellationToken)
    {
        DateOnly startsOn = DateOnly.FromDateTime(DateTime.UtcNow);
        Course course = Course.Create(
            Guid.NewGuid(),
            userId,
            medication.Id,
            startsOn,
            endsOn: startsOn.AddDays(13),
            durationDays: 14,
            isActive: true);
        await _courses.UpsertAsync(course, cancellationToken);

        if (!decimal.TryParse(Dosage, out decimal dose) || dose <= 0)
        {
            dose = 1;
        }

        if (UseFixedTime)
        {
            await SaveFixedTimesScheduleAsync(course.Id, dose, FixedTimes, cancellationToken);
        }

        if (TimeMorning)
        {
            await SaveFixedTimesScheduleAsync(course.Id, dose, "08:00", cancellationToken);
        }

        if (TimeAfternoon)
        {
            await SaveFixedTimesScheduleAsync(course.Id, dose, "14:00", cancellationToken);
        }

        if (TimeEvening)
        {
            await SaveFixedTimesScheduleAsync(course.Id, dose, "20:00", cancellationToken);
        }

        if (MealBreakfast)
        {
            await SaveMealScheduleAsync(course.Id, dose, MealKind.Breakfast, cancellationToken);
        }

        if (MealLunch)
        {
            await SaveMealScheduleAsync(course.Id, dose, MealKind.Lunch, cancellationToken);
        }

        if (MealDinner)
        {
            await SaveMealScheduleAsync(course.Id, dose, MealKind.Dinner, cancellationToken);
        }
    }

    private async Task SaveFixedTimesScheduleAsync(
        Guid courseId,
        decimal dose,
        string timesCsv,
        CancellationToken cancellationToken)
    {
        List<TimeOnly> times = [];
        foreach (string part in timesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!TimeOnly.TryParse(part, out TimeOnly time))
            {
                throw new InvalidOperationException($"Некорректное время: {part}");
            }

            times.Add(time);
        }

        Schedule schedule = Schedule.CreateFixedTimes(
            Guid.NewGuid(),
            courseId,
            WeekDays.All,
            dose,
            times);
        await _schedules.UpsertAsync(schedule, cancellationToken);
    }

    private async Task SaveMealScheduleAsync(
        Guid courseId,
        decimal dose,
        MealKind mealKind,
        CancellationToken cancellationToken)
    {
        Schedule schedule = Schedule.CreateMealRelative(
            Guid.NewGuid(),
            courseId,
            WeekDays.All,
            dose,
            mealKind,
            MealRelation.With,
            offsetMinutes: 0);
        await _schedules.UpsertAsync(schedule, cancellationToken);
    }

    private void ClearForm()
    {
        Name = string.Empty;
        Form = "tablet";
        Dosage = "1";
        Unit = "шт";
        Barcode = string.Empty;
        Notes = string.Empty;
        CheckboxCount = 2;
        QuantityOnHand = "0";
        LowStockThreshold = "0";
        TimeMorning = false;
        TimeAfternoon = false;
        TimeEvening = false;
        MealBreakfast = false;
        MealLunch = false;
        MealDinner = false;
        UseFixedTime = false;
        FixedTimes = "08:00";
    }

    private async Task LoadInventoryAsync(Guid medicationId)
    {
        try
        {
            Inventory? inventory = await _inventory.GetByMedicationAsync(medicationId);
            QuantityOnHand = inventory?.QuantityOnHand.ToString() ?? "0";
            LowStockThreshold = inventory?.LowStockThreshold.ToString() ?? "0";
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
}
