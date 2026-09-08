using FluentAssertions;
using Med.Application.Abstractions;
using Med.Application.UseCases;
using Med.Domain.Entities;
using Med.Domain.Enums;
using Med.Domain.ValueObjects;
using Med.Presentation.Abstractions;
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
    public async Task Confirm_строки_вызывает_use_case_и_перезагружает_день()
    {
        FakeTransitions transitions = new();
        TodayViewModel vm = NewViewModel(transitions, DoseEventState.Scheduled);

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
    [InlineData(DoseEventState.Skipped, false, false, false)]
    [InlineData(DoseEventState.Missed, false, false, false)]
    [InlineData(DoseEventState.Cancelled, false, false, false)]
    public async Task Доступность_действий_считается_по_состоянию_дозы(
        DoseEventState state,
        bool canConfirm,
        bool canSkip,
        bool canUndo)
    {
        TodayViewModel vm = NewViewModel(new FakeTransitions(), state);

        await vm.RefreshCommand.ExecuteAsync(null);

        DoseRowViewModel row = vm.Items.Should().ContainSingle().Subject;
        row.CanConfirm.Should().Be(canConfirm);
        row.CanSkip.Should().Be(canSkip);
        row.CanUndo.Should().Be(canUndo);
        row.ConfirmCommand.CanExecute(null).Should().Be(canConfirm);
        row.UndoCommand.CanExecute(null).Should().Be(canUndo);
    }

    [Fact]
    public async Task День_без_приёмов_помечается_пустым()
    {
        TodayViewModel vm = NewViewModel(new FakeTransitions(), state: null);

        await vm.RefreshCommand.ExecuteAsync(null);

        vm.Items.Should().BeEmpty();
        vm.IsEmpty.Should().BeTrue();
        vm.ProgressText.Should().BeEmpty();
    }

    [Fact]
    public async Task Отклонённый_переход_показывает_причину_и_не_ломает_экран()
    {
        FakeTransitions transitions = new() { Outcome = "Rejected", Reason = "lost race" };
        TodayViewModel vm = NewViewModel(transitions, DoseEventState.Scheduled);
        await vm.RefreshCommand.ExecuteAsync(null);

        await vm.Items[0].ConfirmCommand.ExecuteAsync(null);

        vm.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task Realtime_событие_перезагружает_день_через_диспетчер_UI()
    {
        FakeRealtime realtime = new();
        RecordingUiDispatcher ui = new();
        TodayViewModel vm = NewViewModel(new FakeTransitions(), DoseEventState.Scheduled, realtime, ui);

        realtime.Raise(new DoseEventChange(DoseId, DoseEventState.Taken, Noon, DoseEventChangeType.Update));

        if (vm.LastReloadTask is not null)
        {
            await vm.LastReloadTask;
        }

        ui.PostCount.Should().Be(1);
        vm.Items.Should().ContainSingle();
    }

    private static TodayViewModel NewViewModel(
        FakeTransitions transitions,
        DoseEventState? state,
        IDoseEventRealtime? realtime = null,
        IUiDispatcher? ui = null)
    {
        DateOnly localDate = new(2026, 8, 27);
        List<DoseEvent> doses = [];
        if (state is { } value)
        {
            DoseEvent dose = DoseEvent.CreateScheduled(
                DoseId,
                CourseId,
                ScheduleId,
                DateTimeOffset.Parse("2026-08-27T09:00:00Z"),
                localDate);
            doses.Add(dose with { State = value });
        }

        GetDayAgendaUseCase agenda = new(
            new FakeDoseEvents(doses),
            new FakeSchedules(Schedule()),
            new FakeCourses(Course()),
            new FakeMedications(Medication()),
            new FakeProfiles(MoscowProfile()),
            new FakeClock(Noon));

        return new TodayViewModel(
            agenda,
            new ConfirmDoseUseCase(transitions),
            new SkipDoseUseCase(transitions),
            new UndoConfirmDoseUseCase(transitions),
            new MaterializeUpcomingDosesUseCase(new FakeMaterializer()),
            realtime ?? new FakeRealtime(),
            new FakeAuth(UserId),
            ui ?? new ImmediateUiDispatcher(),
            TestFeedback.Instance);
    }

    private sealed class FakeAuth(Guid userId) : IAuthService
    {
        public AuthSession? CurrentSession => new(userId, "a@b.c", "token", "refresh", DateTimeOffset.UtcNow.AddHours(1));

        public Guid? CurrentUserId => userId;

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

    private sealed class FakeDoseEvents(IReadOnlyList<DoseEvent> seed) : IDoseEventRepository
    {
        public Task<IReadOnlyList<DoseEvent>> ListForLocalDateAsync(
            DateOnly localDate,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DoseEvent>>(seed.Where(e => e.LocalDate == localDate).ToArray());

        public Task<IReadOnlyList<DoseEvent>> ListByScheduleAsync(
            Guid scheduleId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DoseEvent>>(seed.Where(e => e.ScheduleId == scheduleId).ToArray());

        public Task UpsertManyAsync(IReadOnlyList<DoseEvent> events, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<DoseEvent?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(seed.FirstOrDefault(e => e.Id == id));
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
}
