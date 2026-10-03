using CommunityToolkit.Mvvm.Messaging;
using FluentAssertions;
using Med.Application.Abstractions;
using Med.Application.UseCases;
using Med.Domain.Abstractions;
using Med.Domain.Entities;
using Med.Domain.Enums;
using Med.Domain.ValueObjects;
using Med.Presentation.Abstractions;
using Med.Presentation.Messaging;
using Med.Presentation.Today;
using Xunit;

namespace Med.Presentation.Tests.Today;

public sealed class TodayViewModelTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid MedicationId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid CourseId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ScheduleId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid DoseId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTimeOffset Noon = DateTimeOffset.Parse("2026-08-27T09:00:00Z");

    [Fact]
    public async Task Не_авторизованный_пользователь_не_загружает_день_и_показывает_требование_входа()
    {
        TodayViewModel vm = NewViewModel(isAuthenticated: false);

        vm.IsAuthenticated.Should().BeFalse();

        await vm.RefreshCommand.ExecuteAsync(null);

        vm.IsAuthenticated.Should().BeFalse();
        vm.Items.Should().BeEmpty();
        vm.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public async Task День_без_напоминаний_выставляет_флаг_IsEmpty_true()
    {
        TodayViewModel vm = NewViewModel(state: null);

        await vm.RefreshCommand.ExecuteAsync(null);

        vm.Items.Should().BeEmpty();
        vm.IsEmpty.Should().BeTrue();
        vm.ProgressText.Should().BeEmpty();
    }

    [Fact]
    public async Task Маппинг_данных_карточки_DoseRowViewModel_заполняет_все_поля()
    {
        TodayViewModel vm = NewViewModel(state: DoseEventState.Scheduled);

        await vm.RefreshCommand.ExecuteAsync(null);

        vm.Items.Should().ContainSingle();
        DoseRowViewModel row = vm.Items[0];
        row.Id.Should().Be(DoseId);
        row.Title.Should().Be("Магний B6");
        row.Time.Should().Be("12:00");
        row.Details.Should().Be("1 таб · 500 мг");
        row.HasDetails.Should().BeTrue();
        row.State.Should().Be(DoseEventState.Scheduled);
        row.StateText.Should().Be("Запланировано");
        row.CanConfirm.Should().BeTrue();
        row.CanSkip.Should().BeTrue();
        row.CanUndo.Should().BeFalse();
    }

    [Fact]
    public async Task Прогресс_дня_корректно_считает_принятые_из_всех()
    {
        DateOnly localDate = new(2026, 8, 27);
        List<DoseEvent> doses =
        [
            DoseEvent.CreateScheduled(Guid.NewGuid(), CourseId, ScheduleId, DateTimeOffset.Parse("2026-08-27T05:00:00Z"), localDate) with { State = DoseEventState.Taken },
            DoseEvent.CreateScheduled(Guid.NewGuid(), CourseId, ScheduleId, DateTimeOffset.Parse("2026-08-27T09:00:00Z"), localDate) with { State = DoseEventState.Taken },
            DoseEvent.CreateScheduled(Guid.NewGuid(), CourseId, ScheduleId, DateTimeOffset.Parse("2026-08-27T13:00:00Z"), localDate) with { State = DoseEventState.Scheduled },
            DoseEvent.CreateScheduled(Guid.NewGuid(), CourseId, ScheduleId, DateTimeOffset.Parse("2026-08-27T17:00:00Z"), localDate) with { State = DoseEventState.Skipped },
        ];

        DateTimeOffset now = DateTimeOffset.Parse("2026-08-27T12:30:00Z");
        TodayViewModel vm = NewViewModel(customDoses: doses, now: now);

        await vm.RefreshCommand.ExecuteAsync(null);

        vm.Items.Should().HaveCount(4);
        vm.ProgressText.Should().Be("Принято 2 из 4");
    }

    [Fact]
    public async Task Сортировка_напоминаний_строго_по_возрастанию_времени()
    {
        DateOnly localDate = new(2026, 8, 27);
        List<DoseEvent> unsortedDoses =
        [
            DoseEvent.CreateScheduled(Guid.NewGuid(), CourseId, ScheduleId, DateTimeOffset.Parse("2026-08-27T09:40:00Z"), localDate), // 12:40 MSK
            DoseEvent.CreateScheduled(Guid.NewGuid(), CourseId, ScheduleId, DateTimeOffset.Parse("2026-08-27T09:10:00Z"), localDate), // 12:10 MSK
            DoseEvent.CreateScheduled(Guid.NewGuid(), CourseId, ScheduleId, DateTimeOffset.Parse("2026-08-27T09:25:00Z"), localDate), // 12:25 MSK
        ];

        TodayViewModel vm = NewViewModel(customDoses: unsortedDoses);

        await vm.RefreshCommand.ExecuteAsync(null);

        vm.Items.Should().HaveCount(3);
        vm.Items.Select(x => x.Time).Should().ContainInOrder("12:10", "12:25", "12:40");
    }

    [Fact]
    public async Task Реактивность_Realtime_обновляет_статус_строки_без_сбоя()
    {
        DateOnly localDate = new(2026, 8, 27);
        DoseEvent dose = DoseEvent.CreateScheduled(
            DoseId,
            CourseId,
            ScheduleId,
            DateTimeOffset.Parse("2026-08-27T09:00:00Z"),
            localDate);

        FakeDoseEvents repo = new([dose]);
        FakeRealtime realtime = new();
        RecordingUiDispatcher ui = new();
        TodayViewModel vm = NewViewModel(doseRepo: repo, realtime: realtime, ui: ui);

        await vm.RefreshCommand.ExecuteAsync(null);
        vm.Items.Should().ContainSingle();
        vm.Items[0].State.Should().Be(DoseEventState.Scheduled);
        vm.Items[0].CanConfirm.Should().BeTrue();

        // Имитируем подтверждение дозы через Telegram / внешнее Realtime событие
        repo.UpdateState(DoseId, DoseEventState.Taken);
        realtime.Raise(new DoseEventChange(DoseId, DoseEventState.Taken, Noon, DoseEventChangeType.Update));

        if (vm.LastReloadTask is not null)
        {
            await vm.LastReloadTask;
        }

        ui.PostCount.Should().BeGreaterThanOrEqualTo(1);
        vm.Items.Should().ContainSingle();
        vm.Items[0].State.Should().Be(DoseEventState.Taken);
        vm.Items[0].StateText.Should().Be("Принято");
        vm.Items[0].CanConfirm.Should().BeFalse();
        vm.Items[0].CanUndo.Should().BeTrue();
    }

    [Fact]
    public async Task Confirm_строки_вызывает_use_case_и_перезагружает_день()
    {
        FakeTransitions transitions = new();
        TodayViewModel vm = NewViewModel(transitions: transitions, state: DoseEventState.Scheduled);

        await vm.RefreshCommand.ExecuteAsync(null);

        vm.Items.Should().ContainSingle();
        vm.IsEmpty.Should().BeFalse();
        vm.Items[0].Title.Should().Be("Магний B6");
        vm.Items[0].Time.Should().Be("12:00");

        await vm.Items[0].ConfirmCommand.ExecuteAsync(null);

        transitions.ConfirmCalls.Should().ContainSingle().Which.Should().Be(DoseId);
    }

    [Theory]
    [InlineData(DoseEventState.Scheduled, true, true, false)]
    [InlineData(DoseEventState.Notified, true, true, false)]
    [InlineData(DoseEventState.Taken, false, false, true)]
    [InlineData(DoseEventState.Skipped, false, false, true)]
    [InlineData(DoseEventState.Missed, false, false, false)]
    [InlineData(DoseEventState.Cancelled, false, false, false)]
    public async Task Доступность_действий_считается_по_состоянию_дозы(
        DoseEventState state,
        bool canConfirm,
        bool canSkip,
        bool canUndo)
    {
        TodayViewModel vm = NewViewModel(state: state);

        await vm.RefreshCommand.ExecuteAsync(null);

        DoseRowViewModel row = vm.Items.Should().ContainSingle().Subject;
        row.CanConfirm.Should().Be(canConfirm);
        row.CanSkip.Should().Be(canSkip);
        row.CanUndo.Should().Be(canUndo);
        row.ConfirmCommand.CanExecute(null).Should().Be(canConfirm);
        row.UndoCommand.CanExecute(null).Should().Be(canUndo);
    }

    [Fact]
    public async Task Напоминание_старше_3_часов_без_ответа_автоматически_удаляется_из_повестки_дня()
    {
        DateOnly localDate = new(2026, 8, 27);
        List<DoseEvent> doses =
        [
            // Запланировано в 04:00Z (07:00 MSK) -> при Noon = 09:00Z прошло 5 часов (> 3h)
            DoseEvent.CreateScheduled(Guid.NewGuid(), CourseId, ScheduleId, DateTimeOffset.Parse("2026-08-27T04:00:00Z"), localDate),
            // Запланировано в 08:00Z (11:00 MSK) -> при Noon = 09:00Z прошел 1 час (актуально)
            DoseEvent.CreateScheduled(Guid.NewGuid(), CourseId, ScheduleId, DateTimeOffset.Parse("2026-08-27T08:00:00Z"), localDate),
        ];

        TodayViewModel vm = NewViewModel(customDoses: doses);
        await vm.RefreshCommand.ExecuteAsync(null);

        vm.Items.Should().ContainSingle();
        vm.Items[0].Time.Should().Be("11:00");
    }

    [Fact]
    public async Task Подтверждение_приёма_выставляет_IsTaken_true()
    {
        FakeTransitions transitions = new();
        TodayViewModel vm = NewViewModel(transitions: transitions, state: DoseEventState.Scheduled);
        await vm.RefreshCommand.ExecuteAsync(null);

        vm.Items.Should().ContainSingle();
        DoseRowViewModel row = vm.Items[0];
        row.IsTaken.Should().BeFalse();

        await row.ConfirmCommand.ExecuteAsync(null);

        row.IsTaken.Should().BeTrue();
        row.State.Should().Be(DoseEventState.Taken);
        row.CanConfirm.Should().BeFalse();
    }

    [Fact]
    public async Task Пропуск_приёма_выставляет_IsSkipped_true()
    {
        FakeTransitions transitions = new();
        TodayViewModel vm = NewViewModel(transitions: transitions, state: DoseEventState.Scheduled);
        await vm.RefreshCommand.ExecuteAsync(null);

        vm.Items.Should().ContainSingle();
        DoseRowViewModel row = vm.Items[0];
        row.IsSkipped.Should().BeFalse();

        await row.SkipCommand.ExecuteAsync(null);

        row.IsSkipped.Should().BeTrue();
        row.State.Should().Be(DoseEventState.Skipped);
        row.CanConfirm.Should().BeFalse();
        row.CanSkip.Should().BeFalse();
    }

    [Fact]
    public async Task Подтверждение_приёма_не_удаляет_карточку_а_выставляет_IsCompleted()
    {
        FakeTransitions transitions = new();
        TodayViewModel vm = NewViewModel(transitions: transitions, state: DoseEventState.Scheduled);
        await vm.RefreshCommand.ExecuteAsync(null);

        vm.Items.Should().ContainSingle();
        DoseRowViewModel row = vm.Items[0];

        await row.ConfirmCommand.ExecuteAsync(null);

        row.IsTaken.Should().BeTrue();
        row.IsCompleted.Should().BeTrue();
        vm.Items.Should().Contain(row);
    }

    [Fact]
    public async Task Пропуск_приёма_выставляет_IsCompleted_и_позволяет_отмену_через_Undo()
    {
        FakeTransitions transitions = new();
        TodayViewModel vm = NewViewModel(transitions: transitions, state: DoseEventState.Scheduled);
        await vm.RefreshCommand.ExecuteAsync(null);

        vm.Items.Should().ContainSingle();
        DoseRowViewModel row = vm.Items[0];

        await row.SkipCommand.ExecuteAsync(null);

        row.IsSkipped.Should().BeTrue();
        row.IsCompleted.Should().BeTrue();
        row.CanUndo.Should().BeTrue();
        row.UndoCommand.CanExecute(null).Should().BeTrue();
        vm.Items.Should().Contain(row);

        // Нажатие кнопки Назад (Undo) возвращает карточку в Scheduled и сбрасывает IsCompleted
        await row.UndoCommand.ExecuteAsync(null);

        row.State.Should().Be(DoseEventState.Scheduled);
        row.IsCompleted.Should().BeFalse();
        row.CanConfirm.Should().BeTrue();
        row.CanSkip.Should().BeTrue();
        vm.Items.Should().Contain(row);
    }

    private static TodayViewModel NewViewModel(
        FakeTransitions? transitions = null,
        DoseEventState? state = null,
        IDoseEventRealtime? realtime = null,
        IUiDispatcher? ui = null,
        bool isAuthenticated = true,
        Guid? authUserId = null,
        IReadOnlyList<DoseEvent>? customDoses = null,
        FakeDoseEvents? doseRepo = null,
        INotificationService? notifications = null,
        DateTimeOffset? now = null)
    {
        DateOnly localDate = new(2026, 8, 27);
        List<DoseEvent> doses = [];
        if (customDoses is not null)
        {
            doses.AddRange(customDoses);
        }
        else if (state is { } value)
        {
            DoseEvent dose = DoseEvent.CreateScheduled(
                DoseId,
                CourseId,
                ScheduleId,
                DateTimeOffset.Parse("2026-08-27T09:00:00Z"),
                localDate);
            doses.Add(dose with { State = value });
        }

        FakeDoseEvents repo = doseRepo ?? new FakeDoseEvents(doses);

        ISystemClock clock = new FakeClock(now ?? Noon);
        GetDayAgendaUseCase agenda = new(
            repo,
            new FakeSchedules(Schedule()),
            new FakeCourses(Course()),
            new FakeMedications(Medication()),
            new FakeProfiles(MoscowProfile()),
            clock);

        Guid userId = authUserId ?? UserId;
        IMessenger messenger = new StrongReferenceMessenger();
        IDoseEventRealtime actualRealtime = realtime ?? new FakeRealtime();
        actualRealtime.Changed += (_, change) =>
            messenger.Send(new DoseEventStatusChangedMessage(change, ChangeSource.Realtime));

        return new TodayViewModel(
            agenda,
            new ConfirmDoseUseCase(transitions ?? new FakeTransitions()),
            new SkipDoseUseCase(transitions ?? new FakeTransitions()),
            new UndoConfirmDoseUseCase(transitions ?? new FakeTransitions()),
            new MaterializeUpcomingDosesUseCase(new FakeMaterializer()),
            actualRealtime,
            new FakeAuth(userId, hasAuth: isAuthenticated),
            ui ?? new ImmediateUiDispatcher(),
            TestFeedback.Instance,
            messenger: messenger,
            notifications: notifications,
            clock: clock);
    }

    private sealed class FakeAuth(Guid userId, bool hasAuth = true) : IAuthService
    {
        public AuthSession? CurrentSession => hasAuth ? new(userId, "a@b.c", "token", "refresh", DateTimeOffset.UtcNow.AddHours(1)) : null;

        public Guid? CurrentUserId => hasAuth ? userId : null;

        public event EventHandler<AuthSession?>? AuthStateChanged
        {
            add { }
            remove { }
        }

        public Task<AuthSession> SignInWithPasswordAsync(string email, string password, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SendMagicLinkAsync(string email, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SignOutAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task UpdatePasswordAsync(string newPassword, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<AuthSession> SignUpWithPasswordAsync(
            string email,
            string password,
            string? username = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static Profile MoscowProfile() =>
        Profile.Create(
            UserId,
            "brenda",
            "Europe/Moscow",
            new MealWindows(new TimeOnly(8, 0), new TimeOnly(13, 0), new TimeOnly(19, 0)));

    private static Course Course() =>
        Med.Domain.Entities.Course.Create(
            CourseId,
            UserId,
            MedicationId,
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 9, 1),
            durationDays: 32,
            isActive: true);

    private static Medication Medication() =>
        Med.Domain.Entities.Medication.Create(MedicationId, UserId, "Магний B6", "таблетка", "500 мг", "таб");

    private static Schedule Schedule() =>
        Med.Domain.Entities.Schedule.CreateFixedTimes(
            ScheduleId,
            CourseId,
            WeekDays.All,
            doseAmount: 1m,
            [new TimeOnly(12, 0)]);

    private sealed class FakeMaterializer : IDoseEventMaterializer
    {
        public Task<int> MaterializeAsync(int horizonDays = 14, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }

    private sealed class FakeClock(DateTimeOffset utcNow) : Domain.Abstractions.ISystemClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }

    private sealed class FakeProfiles(Profile profile) : IProfileRepository
    {
        public Task<Profile?> GetCurrentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<Profile?>(profile);

        public Task UpdateAsync(Profile profile, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeDoseEvents(IEnumerable<DoseEvent> seed) : IDoseEventRepository
    {
        private readonly List<DoseEvent> _items = [.. seed];

        public void UpdateState(Guid id, DoseEventState newState)
        {
            int index = _items.FindIndex(e => e.Id == id);
            if (index >= 0)
            {
                _items[index] = _items[index] with { State = newState };
            }
        }

        public Task<IReadOnlyList<DoseEvent>> ListForLocalDateAsync(
            DateOnly localDate,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DoseEvent>>(_items.Where(e => e.LocalDate == localDate).ToArray());

        public Task<IReadOnlyList<DoseEvent>> ListByScheduleAsync(
            Guid scheduleId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DoseEvent>>(_items.Where(e => e.ScheduleId == scheduleId).ToArray());

        public Task UpsertManyAsync(IReadOnlyList<DoseEvent> events, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<DoseEvent?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_items.FirstOrDefault(e => e.Id == id));
    }

    private sealed class FakeSchedules(Schedule schedule) : IScheduleRepository
    {
        public Task<IReadOnlyList<Schedule>> ListByCourseAsync(
            Guid courseId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Schedule>>(schedule.CourseId == courseId ? [schedule] : []);

        public Task<Schedule?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Schedule?>(schedule.Id == id ? schedule : null);

        public Task UpsertAsync(Schedule schedule, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeCourses(Course course) : ICourseRepository
    {
        public Task<IReadOnlyList<Course>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Course>>([course]);

        public Task<Course?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Course?>(course.Id == id ? course : null);

        public Task UpsertAsync(Course course, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeMedications(Medication medication) : IMedicationRepository
    {
        public Task<IReadOnlyList<Medication>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Medication>>([medication]);

        public Task<Medication?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Medication?>(medication.Id == id ? medication : null);

        public Task UpsertAsync(Medication medication, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeTransitions : IDoseTransitionService
    {
        public string Outcome { get; init; } = "Applied";

        public string? Reason { get; init; }

        public List<Guid> ConfirmCalls { get; } = [];

        public Task<DoseTransitionResult> ConfirmAsync(
            Guid doseEventId,
            DoseEventSource source,
            DateTimeOffset? takenAt = null,
            CancellationToken cancellationToken = default)
        {
            ConfirmCalls.Add(doseEventId);
            return Result(doseEventId, DoseEventState.Taken);
        }

        public Task<DoseTransitionResult> SkipAsync(
            Guid doseEventId,
            DoseEventSource source,
            DateTimeOffset? skippedAt = null,
            CancellationToken cancellationToken = default) =>
            Result(doseEventId, DoseEventState.Skipped);

        public Task<DoseTransitionResult> UndoConfirmAsync(
            Guid doseEventId,
            DateTimeOffset? undoneAt = null,
            CancellationToken cancellationToken = default) =>
            Result(doseEventId, DoseEventState.Scheduled);

        private Task<DoseTransitionResult> Result(Guid doseEventId, DoseEventState state) =>
            Task.FromResult(new DoseTransitionResult(Outcome, doseEventId, state, Reason));
    }

    private sealed class FakeRealtime : IDoseEventRealtime
    {
        public event EventHandler<DoseEventChange>? Changed;

        public void Raise(DoseEventChange change) => Changed?.Invoke(this, change);

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingUiDispatcher : IUiDispatcher
    {
        public int PostCount { get; private set; }

        public void Post(Action action)
        {
            PostCount++;
            action();
        }
    }

    private sealed class FakeNotificationService : INotificationService
    {
        public List<(string Id, string Title, string Body, DateTimeOffset At)> Scheduled { get; } = [];
        public List<string> Cancelled { get; } = [];
        public List<(string Title, string Body)> Shown { get; } = [];

        public Task ShowAsync(string title, string body, CancellationToken cancellationToken = default)
        {
            Shown.Add((title, body));
            return Task.CompletedTask;
        }

        public Task ScheduleAsync(string id, string title, string body, DateTimeOffset at, CancellationToken cancellationToken = default)
        {
            Scheduled.Add((id, title, body, at));
            return Task.CompletedTask;
        }

        public Task CancelAsync(string id, CancellationToken cancellationToken = default)
        {
            Cancelled.Add(id);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Загрузка_дня_планирует_локальные_уведомления_для_будущих_доз()
    {
        var notifications = new FakeNotificationService();
        // Устанавливаем текущее время до 12:00 MSK (например, 08:00 UTC = 11:00 MSK)
        DateTimeOffset morning = DateTimeOffset.Parse("2026-08-27T08:00:00Z");

        TodayViewModel vm = NewViewModel(
            state: DoseEventState.Scheduled,
            notifications: notifications,
            now: morning);

        await vm.RefreshCommand.ExecuteAsync(null);

        notifications.Scheduled.Should().HaveCount(2);
        notifications.Scheduled[0].Id.Should().Be($"{DoseId}_advance");
        notifications.Scheduled[0].Title.Should().Contain("Через 5 минут");
        notifications.Scheduled[0].At.Should().Be(DateTimeOffset.Parse("2026-08-27T08:55:00Z"));

        notifications.Scheduled[1].Id.Should().Be(DoseId.ToString());
        notifications.Scheduled[1].Title.Should().Contain("Магний B6");
        notifications.Scheduled[1].At.Should().Be(DateTimeOffset.Parse("2026-08-27T09:00:00Z"));
    }

    [Fact]
    public async Task Подтверждение_дозы_отменяет_локальное_уведомление()
    {
        var notifications = new FakeNotificationService();
        TodayViewModel vm = NewViewModel(state: DoseEventState.Scheduled, notifications: notifications);
        await vm.RefreshCommand.ExecuteAsync(null);

        DoseRowViewModel row = vm.Items[0];
        await row.ConfirmCommand.ExecuteAsync(null);

        notifications.Cancelled.Should().Contain(DoseId.ToString());
    }

    [Fact]
    public async Task Пропуск_дозы_отменяет_локальное_уведомление()
    {
        var notifications = new FakeNotificationService();
        TodayViewModel vm = NewViewModel(state: DoseEventState.Scheduled, notifications: notifications);
        await vm.RefreshCommand.ExecuteAsync(null);

        DoseRowViewModel row = vm.Items[0];
        await row.SkipCommand.ExecuteAsync(null);

        notifications.Cancelled.Should().Contain(DoseId.ToString());
    }

    [Fact]
    public async Task Отмена_приёма_снова_планирует_уведомление_если_время_в_будущем()
    {
        var notifications = new FakeNotificationService();
        DateTimeOffset morning = DateTimeOffset.Parse("2026-08-27T08:00:00Z");
        TodayViewModel vm = NewViewModel(
            state: DoseEventState.Taken,
            notifications: notifications,
            now: morning);

        await vm.RefreshCommand.ExecuteAsync(null);

        DoseRowViewModel row = vm.Items[0];
        await row.UndoCommand.ExecuteAsync(null);

        notifications.Scheduled.Should().Contain(s => s.Id == DoseId.ToString());
    }

    [Fact]
    public async Task Загрузка_дня_планирует_предварительное_уведомление_за_5_минут_и_уведомление_в_момент_приема()
    {
        var notifications = new FakeNotificationService();
        // Устанавливаем время за 3 часа до приёма: приём в 09:00Z, текущее время 06:00Z
        DateTimeOffset earlyMorning = DateTimeOffset.Parse("2026-08-27T06:00:00Z");

        TodayViewModel vm = NewViewModel(
            state: DoseEventState.Scheduled,
            notifications: notifications,
            now: earlyMorning);

        await vm.RefreshCommand.ExecuteAsync(null);

        notifications.Scheduled.Should().HaveCount(2);

        // Предварительное за 5 минут (08:55Z)
        var advance = notifications.Scheduled.Should().ContainSingle(s => s.Id == $"{DoseId}_advance").Subject;
        advance.Title.Should().Contain("Через 5 минут");
        advance.Title.Should().Contain("Магний B6");
        advance.At.Should().Be(DateTimeOffset.Parse("2026-08-27T08:55:00Z"));

        // Основное в момент приёма (09:00Z)
        var exact = notifications.Scheduled.Should().ContainSingle(s => s.Id == DoseId.ToString()).Subject;
        exact.Title.Should().Contain("Время принять");
        exact.At.Should().Be(DateTimeOffset.Parse("2026-08-27T09:00:00Z"));
    }

    [Fact]
    public async Task Напоминание_не_показывается_если_до_приема_больше_1_часа()
    {
        // 06:00Z при запланированном 09:00Z -> разница 3 часа (> 1 часа)
        DateTimeOffset earlyMorning = DateTimeOffset.Parse("2026-08-27T06:00:00Z");

        TodayViewModel vm = NewViewModel(
            state: DoseEventState.Scheduled,
            now: earlyMorning);

        await vm.RefreshCommand.ExecuteAsync(null);

        vm.Items.Should().BeEmpty();
        vm.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task Напоминание_показывается_если_до_приема_меньше_или_равно_1_часу()
    {
        // 08:30Z при запланированном 09:00Z -> разница 30 минут (<= 1 час)
        DateTimeOffset closeToTime = DateTimeOffset.Parse("2026-08-27T08:30:00Z");

        TodayViewModel vm = NewViewModel(
            state: DoseEventState.Scheduled,
            now: closeToTime);

        await vm.RefreshCommand.ExecuteAsync(null);

        DoseRowViewModel row = vm.Items.Should().ContainSingle().Subject;
        row.StatusBadgeText.Should().Be("Уже скоро");
        row.IsApproaching.Should().BeTrue();
        row.ApproachingText.Should().Be("Приближается время для приёма!");
    }

    [Fact]
    public async Task Подтверждение_и_пропуск_отменяют_оба_уведомления()
    {
        var notifications = new FakeNotificationService();
        DateTimeOffset closeToTime = DateTimeOffset.Parse("2026-08-27T08:30:00Z");

        TodayViewModel vm = NewViewModel(
            state: DoseEventState.Scheduled,
            notifications: notifications,
            now: closeToTime);

        await vm.RefreshCommand.ExecuteAsync(null);

        DoseRowViewModel row = vm.Items[0];
        await row.ConfirmCommand.ExecuteAsync(null);

        notifications.Cancelled.Should().Contain(DoseId.ToString());
        notifications.Cancelled.Should().Contain($"{DoseId}_advance");
    }
}

