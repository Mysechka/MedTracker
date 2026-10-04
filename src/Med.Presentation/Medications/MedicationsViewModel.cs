using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Med.Application.Abstractions;
using Med.Application.UseCases;
using Med.Domain;
using Med.Domain.Abstractions;
using Med.Domain.Entities;
using Med.Domain.Enums;
using Med.Domain.Scheduling;
using Med.Domain.ValueObjects;
using Med.Presentation.Abstractions;
using Med.Presentation.Feedback;
using Med.Presentation.Messaging;
using Med.Presentation.Sync;

namespace Med.Presentation.Medications;

public sealed partial class MedicationsViewModel : ViewModelBase,
    IRecipient<EntitySavedMessage<Medication>>,
    IRecipient<EntityDeletedMessage<Medication>>,
    IRecipient<ScheduleUpdatedMessage>,
    IRecipient<DoseEventStatusChangedMessage>,
    IDisposable
{
    private readonly IMedicationRepository _medications;
    private readonly IInventoryRepository _inventory;
    private readonly ICourseRepository _courses;
    private readonly IScheduleRepository _schedules;
    private readonly IAuthService _auth;
    private readonly RestockInventoryUseCase _restock;
    private readonly UserFeedback _feedback;
    private readonly IUiDispatcher _ui;
    private readonly IMessenger _messenger;
    private readonly EntityChangeDeduplicator _deduplicator;
    private readonly IDoseEventRepository? _doseEvents;
    private readonly IProfileRepository? _profiles;
    private readonly ISystemClock? _clock;
    private readonly Timer? _dayResetTimer;
    private DateOnly _lastLocalDate;

    public MedicationsViewModel(
        IMedicationRepository medications,
        IInventoryRepository inventory,
        ICourseRepository courses,
        IScheduleRepository schedules,
        IAuthService auth,
        RestockInventoryUseCase restock,
        UserFeedback feedback,
        IUiDispatcher? ui = null,
        IMessenger? messenger = null,
        EntityChangeDeduplicator? deduplicator = null,
        IDoseEventRepository? doseEvents = null,
        IProfileRepository? profiles = null,
        ISystemClock? clock = null)
    {
        _medications = medications;
        _inventory = inventory;
        _courses = courses;
        _schedules = schedules;
        _auth = auth;
        _restock = restock;
        _feedback = feedback;
        _ui = ui ?? new ImmediateUiDispatcher();
        _messenger = messenger ?? WeakReferenceMessenger.Default;
        _deduplicator = deduplicator ?? new EntityChangeDeduplicator();
        _doseEvents = doseEvents;
        _profiles = profiles;
        _clock = clock;

        _messenger.RegisterAll(this);

        _dayResetTimer = new Timer(_ => _ui.Post(CheckDayReset), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public ObservableCollection<MedicationCardViewModel> Items { get; } = [];

    [ObservableProperty]
    private Medication? _selected;

    [ObservableProperty]
    private int _checkboxCount = 2;

    public IReadOnlyList<int> AvailableCheckboxCounts { get; } = [1, 2, 3, 4, 5, 6];

    [ObservableProperty]
    private bool _isEmpty = true;

    [ObservableProperty]
    private string _name = string.Empty;

    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(CanSave));
        SaveCommand.NotifyCanExecuteChanged();
    }

    public bool CanSave => !string.IsNullOrWhiteSpace(Name);

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
    private bool _isCourseMode;

    [ObservableProperty]
    private DateOnly? _courseEndsOn;

    public DateTime? CourseEndsOnDateTime
    {
        get => CourseEndsOn.HasValue ? CourseEndsOn.Value.ToDateTime(TimeOnly.MinValue) : null;
        set
        {
            CourseEndsOn = value.HasValue ? DateOnly.FromDateTime(value.Value) : null;
            OnPropertyChanged();
        }
    }

    public DateTimeOffset? CourseEndsOnOffset
    {
        get => CourseEndsOn.HasValue ? new DateTimeOffset(CourseEndsOn.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null;
        set
        {
            CourseEndsOn = value.HasValue ? DateOnly.FromDateTime(value.Value.DateTime) : null;
            OnPropertyChanged();
        }
    }

    partial void OnCourseEndsOnChanged(DateOnly? value)
    {
        OnPropertyChanged(nameof(CourseEndsOnDateTime));
        OnPropertyChanged(nameof(CourseEndsOnOffset));
    }

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
        IReadOnlyList<string> tags = MedicationCardViewModel.ParseTags(value.Barcode);
        TimeMorning = tags.Any(t => string.Equals(t, "Во время завтрака", StringComparison.OrdinalIgnoreCase) || string.Equals(t, "Утром", StringComparison.OrdinalIgnoreCase) || string.Equals(t, "morning", StringComparison.OrdinalIgnoreCase));
        TimeAfternoon = tags.Any(t => string.Equals(t, "Во время обеда", StringComparison.OrdinalIgnoreCase) || string.Equals(t, "Днем", StringComparison.OrdinalIgnoreCase) || string.Equals(t, "Днём", StringComparison.OrdinalIgnoreCase) || string.Equals(t, "afternoon", StringComparison.OrdinalIgnoreCase));
        TimeEvening = tags.Any(t => string.Equals(t, "Во время ужина", StringComparison.OrdinalIgnoreCase) || string.Equals(t, "Вечером", StringComparison.OrdinalIgnoreCase) || string.Equals(t, "evening", StringComparison.OrdinalIgnoreCase));
        _ = LoadInventoryAsync(value.Id);
    }

    partial void OnCheckboxCountChanged(int value)
    {
        if (value < 1)
        {
            _checkboxCount = 1;
        }
        else if (value > 6)
        {
            _checkboxCount = 6;
        }
    }

    [RelayCommand]
    private void IncrementCheckboxes()
    {
        if (CheckboxCount < 6)
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
        CheckboxCount = Math.Clamp(count, 1, 6);
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
            Name = card.Name;
            Form = card.Form;
            Dosage = card.Dosage;
            Unit = card.Unit;
            Barcode = card.Medication.Barcode ?? string.Empty;
            Notes = card.Notes ?? string.Empty;
            CheckboxCount = card.CheckboxCount;
            TimeMorning = card.Tags.Any(t => string.Equals(t, "Во время завтрака", StringComparison.OrdinalIgnoreCase) || string.Equals(t, "Утром", StringComparison.OrdinalIgnoreCase) || string.Equals(t, "morning", StringComparison.OrdinalIgnoreCase));
            TimeAfternoon = card.Tags.Any(t => string.Equals(t, "Во время обеда", StringComparison.OrdinalIgnoreCase) || string.Equals(t, "Днем", StringComparison.OrdinalIgnoreCase) || string.Equals(t, "Днём", StringComparison.OrdinalIgnoreCase) || string.Equals(t, "afternoon", StringComparison.OrdinalIgnoreCase));
            TimeEvening = card.Tags.Any(t => string.Equals(t, "Во время ужина", StringComparison.OrdinalIgnoreCase) || string.Equals(t, "Вечером", StringComparison.OrdinalIgnoreCase) || string.Equals(t, "evening", StringComparison.OrdinalIgnoreCase));
            _ = LoadInventoryAsync(card.Id);
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
            await LoadItemsAsync(cancellationToken);
        });
    }

    private async Task LoadItemsAsync(CancellationToken cancellationToken)
    {
        Profile? profile = _profiles is not null ? await _profiles.GetCurrentAsync(cancellationToken) : null;
        TimeZoneInfo tz = profile?.ResolveTimeZone() ?? TimeZoneInfo.Utc;
        DateTimeOffset now = _clock?.UtcNow ?? DateTimeOffset.UtcNow;
        DateOnly today = LocalTimeConverter.ToLocalDate(now, tz);

        if (_lastLocalDate != default && today != _lastLocalDate)
        {
            foreach (MedicationCardViewModel card in Items)
            {
                card.ResetSlots();
            }
        }
        _lastLocalDate = today;

        IReadOnlyList<Course> courses = await _courses.ListAsync(cancellationToken);
        var coursesByMed = courses.Where(c => c.IsActive).ToLookup(c => c.MedicationId);
        var allCoursesByMed = courses.ToLookup(c => c.MedicationId);
        var coursesById = courses.ToDictionary(c => c.Id);

        Dictionary<Guid, int> takenCountByMed = [];
        if (_doseEvents is not null)
        {
            IReadOnlyList<DoseEvent> todayEvents = await _doseEvents.ListForLocalDateAsync(today, cancellationToken);
            foreach (DoseEvent evt in todayEvents.Where(e => e.State == DoseEventState.Taken))
            {
                if (coursesById.TryGetValue(evt.CourseId, out Course? c))
                {
                    takenCountByMed[c.MedicationId] = takenCountByMed.GetValueOrDefault(c.MedicationId, 0) + 1;
                }
            }
        }

        Items.Clear();
        foreach (Medication med in await _medications.ListAsync(cancellationToken))
        {
            var medCourses = allCoursesByMed[med.Id].ToList();
            if (!MedicationVisibility.IsVisibleOnDate(medCourses, today))
            {
                continue;
            }

            List<string> tags = [..MedicationCardViewModel.ParseTags(med.Barcode)];
            if (tags.Count == 0 && coursesByMed.Contains(med.Id))
            {
                foreach (Course course in coursesByMed[med.Id])
                {
                    var schedules = await _schedules.ListByCourseAsync(course.Id, cancellationToken);
                    foreach (var schedule in schedules)
                    {
                        if (schedule.FixedTimes is not null)
                        {
                            foreach (var t in schedule.FixedTimes)
                            {
                                if (t.Hour < 12 && !tags.Contains("Во время завтрака")) tags.Add("Во время завтрака");
                                else if (t.Hour is >= 12 and < 17 && !tags.Contains("Во время обеда")) tags.Add("Во время обеда");
                                else if (t.Hour >= 17 && !tags.Contains("Во время ужина")) tags.Add("Во время ужина");
                            }
                        }
                        if (schedule.MealKind == MealKind.Breakfast && !tags.Contains("Во время завтрака")) tags.Add("Во время завтрака");
                        if (schedule.MealKind == MealKind.Lunch && !tags.Contains("Во время обеда")) tags.Add("Во время обеда");
                        if (schedule.MealKind == MealKind.Dinner && !tags.Contains("Во время ужина")) tags.Add("Во время ужина");
                    }
                }
            }
            var card = new MedicationCardViewModel(med, tags);
            int taken = takenCountByMed.GetValueOrDefault(med.Id, 0);
            card.SetCheckedSlotsCount(taken);
            Items.Add(card);
        }

        IsEmpty = Items.Count == 0;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        await RunAsync(async () =>
        {
            Guid userId = _auth.CurrentUserId
                ?? throw new InvalidOperationException("Нужна сессия.");

            bool isNew = Selected is null;
            Guid id = Selected?.Id ?? Guid.NewGuid();
            List<string> tags = [];
            if (TimeMorning) tags.Add("Во время завтрака");
            if (TimeAfternoon) tags.Add("Во время обеда");
            if (TimeEvening) tags.Add("Во время ужина");

            int safeSlots = Math.Clamp(CheckboxCount, 1, MedicationCardViewModel.MaxCheckboxes);
            string barcodeValue = tags.Count > 0
                ? $"slots:{safeSlots};tags:{string.Join(",", tags)}"
                : $"slots:{safeSlots}";
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
            else if (!isNew && HasScheduleTags())
            {
                await SyncCourseSchedulesAsync(userId, medication, cancellationToken);
            }

            _deduplicator.RecordLocalChange<Medication>(medication.Id);
            _deduplicator.RecordLocalChange<Inventory>(inventory.Id);
            _messenger.Send(new EntitySavedMessage<Medication>(medication, ChangeSource.Local));
            _messenger.Send(new EntitySavedMessage<Inventory>(inventory, ChangeSource.Local));

            Selected = medication;
            IsAdding = false;
            await LoadItemsAsync(cancellationToken);
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
            Guid id = Selected.Id;
            await _medications.DeleteAsync(id, cancellationToken);
            _deduplicator.RecordLocalChange<Medication>(id);
            _messenger.Send(new EntityDeletedMessage<Medication>(id, ChangeSource.Local));

            Selected = null;
            ClearForm();
            await LoadItemsAsync(cancellationToken);
            _feedback.Notify("Лекарство удалено.");
        });
    }

    [RelayCommand]
    private async Task DeleteCardAsync(MedicationCardViewModel card, CancellationToken cancellationToken)
    {
        Selected = card.Medication;
        await DeleteAsync(cancellationToken);
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
        DateOnly? endsOn = null;
        int? durationDays = null;

        if (IsCourseMode)
        {
            endsOn = CourseEndsOn ?? startsOn.AddDays(14);
            if (endsOn < startsOn)
            {
                endsOn = startsOn;
            }

            durationDays = endsOn.Value.DayNumber - startsOn.DayNumber + 1;
        }

        Course course = Course.Create(
            Guid.NewGuid(),
            userId,
            medication.Id,
            startsOn,
            endsOn: endsOn,
            durationDays: durationDays,
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

        if (TimeMorning || MealBreakfast)
        {
            await SaveMealScheduleAsync(course.Id, dose, MealKind.Breakfast, cancellationToken);
        }

        if (TimeAfternoon || MealLunch)
        {
            await SaveMealScheduleAsync(course.Id, dose, MealKind.Lunch, cancellationToken);
        }

        if (TimeEvening || MealDinner)
        {
            await SaveMealScheduleAsync(course.Id, dose, MealKind.Dinner, cancellationToken);
        }
    }

    private async Task SyncCourseSchedulesAsync(
        Guid userId,
        Medication medication,
        CancellationToken cancellationToken)
    {
        var courses = await _courses.ListAsync(cancellationToken);
        Course? activeCourse = courses.FirstOrDefault(c => c.MedicationId == medication.Id && c.IsActive);
        if (activeCourse is null)
        {
            await CreateInitialCourseAndSchedulesAsync(userId, medication, cancellationToken);
            return;
        }

        var oldSchedules = await _schedules.ListByCourseAsync(activeCourse.Id, cancellationToken);
        foreach (var s in oldSchedules)
        {
            await _schedules.DeleteAsync(s.Id, cancellationToken);
        }

        if (!decimal.TryParse(Dosage, out decimal dose) || dose <= 0)
        {
            dose = 1;
        }

        if (UseFixedTime)
        {
            await SaveFixedTimesScheduleAsync(activeCourse.Id, dose, FixedTimes, cancellationToken);
        }

        if (TimeMorning || MealBreakfast)
        {
            await SaveMealScheduleAsync(activeCourse.Id, dose, MealKind.Breakfast, cancellationToken);
        }

        if (TimeAfternoon || MealLunch)
        {
            await SaveMealScheduleAsync(activeCourse.Id, dose, MealKind.Lunch, cancellationToken);
        }

        if (TimeEvening || MealDinner)
        {
            await SaveMealScheduleAsync(activeCourse.Id, dose, MealKind.Dinner, cancellationToken);
        }

        _messenger.Send(new ScheduleUpdatedMessage(activeCourse.Id, medication.Id, ChangeSource.Local));
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
        IsCourseMode = false;
        CourseEndsOn = null;
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

    public void Receive(EntitySavedMessage<Medication> message)
    {
        if (message.Source != ChangeSource.Realtime)
        {
            return;
        }

        _ui.Post(() =>
        {
            Medication med = message.Value;
            MedicationCardViewModel? existing = Items.FirstOrDefault(i => i.Id == med.Id);
            List<string> tags = [.. MedicationCardViewModel.ParseTags(med.Barcode)];
            var newCard = new MedicationCardViewModel(med, tags);

            if (existing is not null)
            {
                int index = Items.IndexOf(existing);
                Items[index] = newCard;
            }
            else
            {
                Items.Add(newCard);
            }

            IsEmpty = Items.Count == 0;
            if (Selected?.Id == med.Id)
            {
                Selected = med;
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
            MedicationCardViewModel? existing = Items.FirstOrDefault(i => i.Id == message.Value);
            if (existing is not null)
            {
                Items.Remove(existing);
            }

            IsEmpty = Items.Count == 0;
            if (Selected?.Id == message.Value)
            {
                Selected = null;
                ClearForm();
            }
        });
    }

    public void Receive(ScheduleUpdatedMessage message)
    {
        _ui.Post(() => _ = RefreshAsync(CancellationToken.None));
    }

    public void Receive(DoseEventStatusChangedMessage message)
    {
        if (message.Value.State != DoseEventState.Taken)
        {
            return;
        }

        _ui.Post(async () =>
        {
            Guid? medId = message.Value.MedicationId;
            if (medId is null && _doseEvents is not null)
            {
                DoseEvent? evt = await _doseEvents.GetAsync(message.Value.Id);
                if (evt is not null)
                {
                    Course? course = await _courses.GetAsync(evt.CourseId);
                    medId = course?.MedicationId;
                }
            }

            if (medId is not null)
            {
                MedicationCardViewModel? card = Items.FirstOrDefault(i => i.Id == medId.Value);
                card?.CheckNextSlot();
            }
        });
    }

    private void CheckDayReset()
    {
        if (_auth.CurrentUserId is null)
        {
            return;
        }

        DateTimeOffset now = _clock?.UtcNow ?? DateTimeOffset.UtcNow;
        DateOnly today = DateOnly.FromDateTime(now.LocalDateTime);
        if (_lastLocalDate != default && today != _lastLocalDate)
        {
            _lastLocalDate = today;
            foreach (MedicationCardViewModel card in Items)
            {
                card.ResetSlots();
            }
            _ = RefreshAsync(CancellationToken.None);
        }
    }

    public void Dispose()
    {
        _dayResetTimer?.Dispose();
        _messenger.UnregisterAll(this);
    }
}
