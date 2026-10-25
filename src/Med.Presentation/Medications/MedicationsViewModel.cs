using System.Collections.ObjectModel;
using System.Globalization;
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
using Med.Presentation.Abstractions;
using Med.Presentation.Feedback;
using Med.Presentation.Messaging;
using Med.Presentation.Sync;

namespace Med.Presentation.Medications;

/// <summary>Ячейка сетки календаря Material 3 DatePicker (Стр. 2 PDF).</summary>
public sealed partial class CalendarDayCellViewModel : ViewModelBase
{
    public DateOnly? Date { get; }
    public string Label { get; }
    public bool IsEmpty { get; }
    public bool IsWeekend { get; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isToday;

    public CalendarDayCellViewModel(DateOnly? date, bool isSelected = false, bool isToday = false)
    {
        Date = date;
        IsEmpty = date is null;
        Label = date?.Day.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        IsWeekend = date?.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        _isSelected = isSelected;
        _isToday = isToday && !isSelected;
    }
}

public sealed partial class MedicationsViewModel : ViewModelBase,
    IRecipient<EntitySavedMessage<Medication>>,
    IRecipient<EntityDeletedMessage<Medication>>,
    IRecipient<ScheduleUpdatedMessage>,
    IRecipient<DoseEventStatusChangedMessage>,
    IDisposable
{
    private const string TagBreakfast = "Во время завтрака";
    private const string TagLunch = "Во время обеда";
    private const string TagDinner = "Во время ужина";

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
    private TimeZoneInfo _activeTimeZone = TimeZoneInfo.Utc;
    private DateOnly _displayedMonth;

    private DateTimeOffset Now => _clock?.UtcNow ?? DateTimeOffset.UtcNow;
    private DateOnly TodayDate => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(Now.UtcDateTime, _activeTimeZone));

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

        DateOnly today = TodayDate;
        _displayedMonth = new DateOnly(today.Year, today.Month, 1);
        RebuildCalendarDays();

        _messenger.RegisterAll(this);

        var weakSelf = new WeakReference<MedicationsViewModel>(this);
        _dayResetTimer = new Timer(static state =>
        {
            if (((WeakReference<MedicationsViewModel>)state!).TryGetTarget(out MedicationsViewModel? target))
            {
                target._ui.Post(target.CheckDayReset);
            }
        }, weakSelf, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public ObservableCollection<MedicationCardViewModel> Items { get; } = [];

    public ObservableCollection<CalendarDayCellViewModel> CalendarDays { get; } = [];

    [ObservableProperty]
    private Medication? _selected;

    [ObservableProperty]
    private int _checkboxCount = 2;

    public IReadOnlyList<int> AvailableCheckboxCounts { get; } = [1, 2, 3, 4, 5, 6];

    [ObservableProperty]
    private bool _isEmpty = true;

    public const int MaxNameLength = 50;
    public const int MaxNotesLength = 50;

    [ObservableProperty]
    private string _name = string.Empty;

    partial void OnNameChanged(string value)
    {
        if (value is { Length: > MaxNameLength })
        {
            _name = value[..MaxNameLength];
            OnPropertyChanged(nameof(Name));
        }

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

    partial void OnNotesChanged(string value)
    {
        if (value is { Length: > MaxNotesLength })
        {
            _notes = value[..MaxNotesLength];
            OnPropertyChanged(nameof(Notes));
        }
    }

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

    partial void OnIsCourseModeChanged(bool value)
    {
        if (value && CourseEndsOn is null)
        {
            DateOnly defaultDate = TodayDate.AddDays(14);
            CourseEndsOn = defaultDate;
            _displayedMonth = new DateOnly(defaultDate.Year, defaultDate.Month, 1);
            RebuildCalendarDays();
        }
    }

    private static readonly string[] RussianMonthsNominative =
    [
        "Январь", "Февраль", "Март", "Апрель", "Май", "Июнь",
        "Июль", "Август", "Сентябрь", "Октябрь", "Ноябрь", "Декабрь"
    ];

    private static readonly string[] RussianMonthsGenitive =
    [
        "января", "февраля", "марта", "апреля", "мая", "июня",
        "июля", "августа", "сентября", "октября", "ноября", "декабря"
    ];

    private static readonly string[] RussianShortWeekDays =
    [
        "Вс", "Пн", "Вт", "Ср", "Чт", "Пт", "Сб"
    ];

    [ObservableProperty]
    private DateOnly? _courseEndsOn;

    public string CourseEndsOnHeaderText
    {
        get
        {
            DateOnly target = CourseEndsOn ?? TodayDate.AddDays(14);
            string dayOfWeek = RussianShortWeekDays[(int)target.DayOfWeek];
            string monthGenitive = RussianMonthsGenitive[target.Month - 1];
            return $"{dayOfWeek}, {target.Day} {monthGenitive}";
        }
    }

    public string CalendarMonthYearTitle =>
        $"{RussianMonthsNominative[_displayedMonth.Month - 1]} {_displayedMonth.Year}";

    public string CourseDurationSummaryText
    {
        get
        {
            DateOnly today = TodayDate;
            DateOnly target = CourseEndsOn ?? today.AddDays(14);
            int days = Math.Max(1, target.DayNumber - today.DayNumber + 1);
            return $"Длительность курса: {days} дн.";
        }
    }

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
        OnPropertyChanged(nameof(CourseEndsOnHeaderText));
        OnPropertyChanged(nameof(CourseDurationSummaryText));
        if (value.HasValue)
        {
            _displayedMonth = new DateOnly(value.Value.Year, value.Value.Month, 1);
        }
        RebuildCalendarDays();
    }

    [RelayCommand]
    private void PreviousMonth()
    {
        _displayedMonth = _displayedMonth.AddMonths(-1);
        OnPropertyChanged(nameof(CalendarMonthYearTitle));
        RebuildCalendarDays();
    }

    [RelayCommand]
    private void NextMonth()
    {
        _displayedMonth = _displayedMonth.AddMonths(1);
        OnPropertyChanged(nameof(CalendarMonthYearTitle));
        RebuildCalendarDays();
    }

    [RelayCommand]
    private void SelectCalendarDate(CalendarDayCellViewModel? cell)
    {
        if (cell?.Date is { } date)
        {
            CourseEndsOn = date;
        }
    }

    private void RebuildCalendarDays()
    {
        CalendarDays.Clear();
        DateOnly firstOfMonth = new(_displayedMonth.Year, _displayedMonth.Month, 1);
        int daysInMonth = DateTime.DaysInMonth(firstOfMonth.Year, firstOfMonth.Month);
        int startOffset = ((int)firstOfMonth.DayOfWeek + 6) % 7; // Понедельник = 0 (Пн Вт Ср Чт Пт Сб Вс)

        DateOnly today = TodayDate;
        DateOnly selected = CourseEndsOn ?? today.AddDays(14);

        for (int i = 0; i < startOffset; i++)
        {
            CalendarDays.Add(new CalendarDayCellViewModel(null));
        }

        for (int day = 1; day <= daysInMonth; day++)
        {
            DateOnly current = new(firstOfMonth.Year, firstOfMonth.Month, day);
            bool isSel = current == selected;
            bool isTod = current == today;
            CalendarDays.Add(new CalendarDayCellViewModel(current, isSel, isTod));
        }

        OnPropertyChanged(nameof(CalendarMonthYearTitle));
        OnPropertyChanged(nameof(CourseEndsOnHeaderText));
        OnPropertyChanged(nameof(CourseDurationSummaryText));
    }

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isAdding;

    partial void OnSelectedChanged(Medication? value)
    {
        if (value is null)
        {
            return;
        }

        PopulateFormFromMedication(value, MedicationCardViewModel.ParseSlots(value.Barcode), MedicationCardViewModel.ParseTags(value.Barcode));
    }

    partial void OnCheckboxCountChanged(int value)
    {
        _checkboxCount = Math.Clamp(value, 1, MedicationCardViewModel.MaxCheckboxes);
    }

    [RelayCommand]
    private void IncrementCheckboxes()
    {
        if (CheckboxCount < MedicationCardViewModel.MaxCheckboxes)
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
    private void SetCheckboxCount(int count) =>
        CheckboxCount = Math.Clamp(count, 1, MedicationCardViewModel.MaxCheckboxes);

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
            PopulateFormFromMedication(card.Medication, card.CheckboxCount, card.Tags);
        }
        else if (item is Medication med)
        {
            Selected = med;
        }
        IsAdding = true;
    }

    private void PopulateFormFromMedication(Medication med, int slots, IReadOnlyList<string> tags)
    {
        Name = med.Name;
        Form = med.Form;
        Dosage = med.Dosage;
        Unit = med.Unit;
        Barcode = med.Barcode ?? string.Empty;
        Notes = med.Notes ?? string.Empty;
        CheckboxCount = slots;
        TimeMorning = HasTag(tags, TagBreakfast, "Утром", "morning");
        TimeAfternoon = HasTag(tags, TagLunch, "Днем", "Днём", "afternoon");
        TimeEvening = HasTag(tags, TagDinner, "Вечером", "evening");
        _ = LoadInventoryAndCourseAsync(med.Id);
    }

    private static bool HasTag(IReadOnlyList<string> tags, string primary, string alt1, string alt2, string? alt3 = null)
    {
        for (int i = 0; i < tags.Count; i++)
        {
            string t = tags[i];
            if (string.Equals(t, primary, StringComparison.OrdinalIgnoreCase)
                || string.Equals(t, alt1, StringComparison.OrdinalIgnoreCase)
                || string.Equals(t, alt2, StringComparison.OrdinalIgnoreCase)
                || (alt3 is not null && string.Equals(t, alt3, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }
        return false;
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

        await RunAsync(() => LoadItemsAsync(cancellationToken));
    }

    private async Task LoadItemsAsync(CancellationToken cancellationToken)
    {
        Profile? profile = _profiles is not null ? await _profiles.GetCurrentAsync(cancellationToken) : null;
        _activeTimeZone = profile?.ResolveTimeZone() ?? TimeZoneInfo.Utc;
        DateTimeOffset now = Now;
        DateOnly today = LocalTimeConverter.ToLocalDate(now, _activeTimeZone);

        if (_lastLocalDate != default && today != _lastLocalDate)
        {
            for (int i = 0; i < Items.Count; i++)
            {
                Items[i].ResetSlots();
            }
        }
        _lastLocalDate = today;

        Task<IReadOnlyList<Course>> coursesTask = _courses.ListAsync(cancellationToken);
        Task<IReadOnlyList<Medication>> medsTask = _medications.ListAsync(cancellationToken);
        Task<IReadOnlyList<DoseEvent>>? eventsTask = _doseEvents?.ListForLocalDateAsync(today, cancellationToken);

        IReadOnlyList<Course> courses = await coursesTask;
        IReadOnlyList<Medication> medications = await medsTask;

        Dictionary<Guid, List<Course>> allCoursesByMed = [];
        Dictionary<Guid, Course> coursesById = new(courses.Count);
        for (int i = 0; i < courses.Count; i++)
        {
            Course c = courses[i];
            coursesById[c.Id] = c;
            if (!allCoursesByMed.TryGetValue(c.MedicationId, out List<Course>? list))
            {
                list = [];
                allCoursesByMed[c.MedicationId] = list;
            }
            list.Add(c);
        }

        Dictionary<Guid, int> takenCountByMed = [];
        if (eventsTask is not null)
        {
            IReadOnlyList<DoseEvent> todayEvents = await eventsTask;
            for (int i = 0; i < todayEvents.Count; i++)
            {
                DoseEvent evt = todayEvents[i];
                if (evt.State == DoseEventState.Taken && coursesById.TryGetValue(evt.CourseId, out Course? c))
                {
                    takenCountByMed[c.MedicationId] = takenCountByMed.GetValueOrDefault(c.MedicationId) + 1;
                }
            }
        }

        Items.Clear();
        for (int i = 0; i < medications.Count; i++)
        {
            Medication med = medications[i];
            allCoursesByMed.TryGetValue(med.Id, out List<Course>? medCourses);
            if (medCourses is { Count: > 0 } && !MedicationVisibility.IsVisibleOnDate(medCourses, today))
            {
                continue;
            }

            List<string> tags = [.. MedicationCardViewModel.ParseTags(med.Barcode)];
            if (tags.Count == 0 && medCourses is { Count: > 0 })
            {
                foreach (Course course in medCourses)
                {
                    if (!course.IsActive) continue;
                    foreach (Schedule schedule in await _schedules.ListByCourseAsync(course.Id, cancellationToken))
                    {
                        foreach (TimeOnly t in schedule.FixedTimes)
                        {
                            string slotTag = t.Hour < 12 ? TagBreakfast : (t.Hour < 17 ? TagLunch : TagDinner);
                            if (!tags.Contains(slotTag)) tags.Add(slotTag);
                        }
                        if (schedule.MealKind == MealKind.Breakfast && !tags.Contains(TagBreakfast)) tags.Add(TagBreakfast);
                        if (schedule.MealKind == MealKind.Lunch && !tags.Contains(TagLunch)) tags.Add(TagLunch);
                        if (schedule.MealKind == MealKind.Dinner && !tags.Contains(TagDinner)) tags.Add(TagDinner);
                    }
                }
            }

            var card = new MedicationCardViewModel(med, tags);
            card.SetCheckedSlotsCount(takenCountByMed.GetValueOrDefault(med.Id));
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
            List<string> tags = new(3);
            if (TimeMorning) tags.Add(TagBreakfast);
            if (TimeAfternoon) tags.Add(TagLunch);
            if (TimeEvening) tags.Add(TagDinner);

            int safeSlots = Math.Clamp(CheckboxCount, 1, MedicationCardViewModel.MaxCheckboxes);
            string barcodeValue = tags.Count > 0
                ? $"slots:{safeSlots};tags:{string.Join(",", tags)}"
                : $"slots:{safeSlots}";
            string safeDosage = string.IsNullOrWhiteSpace(Dosage) ? "1" : Dosage.Trim();
            Medication medication = Medication.Create(
                id,
                userId,
                Name,
                Form,
                safeDosage,
                Unit,
                barcodeValue,
                string.IsNullOrWhiteSpace(Notes) ? null : Notes);

            await _medications.UpsertAsync(medication, cancellationToken);

            _ = decimal.TryParse(QuantityOnHand, out decimal qty);
            _ = decimal.TryParse(LowStockThreshold, out decimal threshold);

            Inventory? existing = await _inventory.GetByMedicationAsync(id, cancellationToken);
            Inventory inventory = Inventory.Create(
                existing?.Id ?? Guid.NewGuid(),
                userId,
                id,
                Math.Max(0, qty),
                Math.Max(0, threshold));
            await _inventory.UpsertAsync(inventory, cancellationToken);

            if (isNew && (HasScheduleTags() || IsCourseMode))
            {
                await CreateInitialCourseAndSchedulesAsync(userId, medication, cancellationToken);
            }
            else if (!isNew)
            {
                await SyncCourseSchedulesAsync(userId, medication, cancellationToken);
            }

            _deduplicator.RecordLocalChange<Medication>(medication.Id);
            _deduplicator.RecordLocalChange<Inventory>(inventory.Id);
            _messenger.Send(new EntitySavedMessage<Medication>(medication, ChangeSource.Local));
            _messenger.Send(new EntitySavedMessage<Inventory>(inventory, ChangeSource.Local));

            Selected = null;
            IsAdding = false;
            await LoadItemsAsync(cancellationToken);
            _ui.Post(() => IsAdding = false);
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
            await LoadInventoryAndCourseAsync(Selected.Id);
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
        DateOnly startsOn = TodayDate;
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
        await SaveActiveSchedulesForCourseAsync(course.Id, cancellationToken);
    }

    private async Task SyncCourseSchedulesAsync(
        Guid userId,
        Medication medication,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Course> courses = await _courses.ListAsync(cancellationToken);
        Course? activeCourse = courses.FirstOrDefault(c => c.MedicationId == medication.Id && c.IsActive);
        if (activeCourse is null)
        {
            if (HasScheduleTags() || IsCourseMode)
            {
                await CreateInitialCourseAndSchedulesAsync(userId, medication, cancellationToken);
            }
            return;
        }

        DateOnly startsOn = activeCourse.StartsOn;
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

        Course updatedCourse = Course.Create(
            activeCourse.Id,
            userId,
            medication.Id,
            startsOn,
            endsOn: endsOn,
            durationDays: durationDays,
            isActive: true,
            diagnosisId: activeCourse.DiagnosisId);
        await _courses.UpsertAsync(updatedCourse, cancellationToken);

        foreach (Schedule s in await _schedules.ListByCourseAsync(activeCourse.Id, cancellationToken))
        {
            await _schedules.DeleteAsync(s.Id, cancellationToken);
        }

        if (HasScheduleTags())
        {
            await SaveActiveSchedulesForCourseAsync(activeCourse.Id, cancellationToken);
        }
        _messenger.Send(new ScheduleUpdatedMessage(activeCourse.Id, medication.Id, ChangeSource.Local));
    }

    private async Task SaveActiveSchedulesForCourseAsync(Guid courseId, CancellationToken cancellationToken)
    {
        if (!decimal.TryParse(Dosage, out decimal dose) || dose <= 0)
        {
            dose = 1;
        }

        if (UseFixedTime)
        {
            await SaveFixedTimesScheduleAsync(courseId, dose, FixedTimes, cancellationToken);
        }

        if (TimeMorning || MealBreakfast)
        {
            await SaveMealScheduleAsync(courseId, dose, MealKind.Breakfast, cancellationToken);
        }

        if (TimeAfternoon || MealLunch)
        {
            await SaveMealScheduleAsync(courseId, dose, MealKind.Lunch, cancellationToken);
        }

        if (TimeEvening || MealDinner)
        {
            await SaveMealScheduleAsync(courseId, dose, MealKind.Dinner, cancellationToken);
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

        Schedule schedule = Schedule.CreateFixedTimes(Guid.NewGuid(), courseId, WeekDays.All, dose, times);
        await _schedules.UpsertAsync(schedule, cancellationToken);
    }

    private Task SaveMealScheduleAsync(
        Guid courseId,
        decimal dose,
        MealKind mealKind,
        CancellationToken cancellationToken) =>
        _schedules.UpsertAsync(
            Schedule.CreateMealRelative(Guid.NewGuid(), courseId, WeekDays.All, dose, mealKind, MealRelation.With, offsetMinutes: 0),
            cancellationToken);

    private void ClearForm()
    {
        Name = string.Empty;
        Form = "tablet";
        Dosage = string.Empty;
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
        DateOnly today = TodayDate;
        _displayedMonth = new DateOnly(today.Year, today.Month, 1);
        RebuildCalendarDays();
    }

    private async Task LoadInventoryAndCourseAsync(Guid medicationId)
    {
        try
        {
            Inventory? inventory = await _inventory.GetByMedicationAsync(medicationId);
            QuantityOnHand = inventory?.QuantityOnHand.ToString() ?? "0";
            LowStockThreshold = inventory?.LowStockThreshold.ToString() ?? "0";

            IReadOnlyList<Course> courses = await _courses.ListAsync();
            Course? activeCourse = courses.FirstOrDefault(c => c.MedicationId == medicationId && c.IsActive);
            if (activeCourse is not null && activeCourse.EndsOn.HasValue)
            {
                IsCourseMode = true;
                CourseEndsOn = activeCourse.EndsOn.Value;
            }
            else
            {
                IsCourseMode = false;
                CourseEndsOn = null;
            }
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
            var newCard = new MedicationCardViewModel(med, MedicationCardViewModel.ParseTags(med.Barcode));
            MedicationCardViewModel? existing = Items.FirstOrDefault(i => i.Id == med.Id);

            if (existing is not null)
            {
                Items[Items.IndexOf(existing)] = newCard;
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

    public void Receive(ScheduleUpdatedMessage message) =>
        _ui.Post(() => _ = RefreshAsync(CancellationToken.None));

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

            if (medId is { } targetId)
            {
                Items.FirstOrDefault(i => i.Id == targetId)?.CheckNextSlot();
            }
        });
    }

    private void CheckDayReset()
    {
        if (_auth.CurrentUserId is null)
        {
            return;
        }

        DateTimeOffset now = Now;
        DateOnly today = LocalTimeConverter.ToLocalDate(now, _activeTimeZone);
        if (_lastLocalDate != default && today != _lastLocalDate)
        {
            _lastLocalDate = today;
            for (int i = 0; i < Items.Count; i++)
            {
                Items[i].ResetSlots();
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
