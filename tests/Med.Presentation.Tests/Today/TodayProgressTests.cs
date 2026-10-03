using CommunityToolkit.Mvvm.Messaging;
using FluentAssertions;
using Med.Application.Abstractions;
using Med.Application.UseCases;
using Med.Domain.Abstractions;
using Med.Domain.Entities;
using Med.Domain.Enums;
using Med.Domain.ValueObjects;
using Med.Presentation.Abstractions;
using Med.Presentation.Today;
using Xunit;

namespace Med.Presentation.Tests.Today;

public sealed class TodayProgressTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid MedicationId = Guid.NewGuid();
    private static readonly Guid CourseId = Guid.NewGuid();
    private static readonly Guid ScheduleId = Guid.NewGuid();

    [Fact]
    public async Task Progress_При_3_дозах_и_2_принятых_показывает_корректные_данные()
    {
        DateOnly localDate = new(2026, 8, 27);
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-27T12:00:00Z");

        var dose1 = DoseEvent.CreateScheduled(Guid.NewGuid(), CourseId, ScheduleId, DateTimeOffset.Parse("2026-08-27T11:00:00Z"), localDate) with
        {
            State = DoseEventState.Taken,
            TakenAt = DateTimeOffset.Parse("2026-08-27T11:05:00Z")
        };
        var dose2 = DoseEvent.CreateScheduled(Guid.NewGuid(), CourseId, ScheduleId, DateTimeOffset.Parse("2026-08-27T12:00:00Z"), localDate) with
        {
            State = DoseEventState.Taken,
            TakenAt = DateTimeOffset.Parse("2026-08-27T12:02:00Z")
        };
        var dose3 = DoseEvent.CreateScheduled(Guid.NewGuid(), CourseId, ScheduleId, DateTimeOffset.Parse("2026-08-27T12:30:00Z"), localDate);

        var repo = new FakeDoseEvents([dose1, dose2, dose3]);
        var clock = new FakeClock(now);

        var agenda = new GetDayAgendaUseCase(
            repo,
            new FakeSchedules(ScheduleId, CourseId),
            new FakeCourses(CourseId, UserId, MedicationId),
            new FakeMedications(MedicationId, UserId),
            new FakeProfiles(UserId),
            clock);

        var vm = new TodayViewModel(
            agenda,
            new ConfirmDoseUseCase(new FakeTransitions()),
            new SkipDoseUseCase(new FakeTransitions()),
            new UndoConfirmDoseUseCase(new FakeTransitions()),
            new MaterializeUpcomingDosesUseCase(new FakeMaterializer()),
            new FakeRealtime(),
            new FakeAuth(UserId),
            new ImmediateUiDispatcher(),
            TestFeedback.Instance,
            clock: clock);

        await vm.RefreshCommand.ExecuteAsync(null);

        vm.Items.Should().HaveCount(3);
        vm.ProgressText.Should().Be("Принято 2 из 3");
        vm.ProgressValue.Should().BeApproximately(2.0 / 3.0, 0.001);
    }

    private sealed class FakeAuth(Guid userId) : IAuthService
    {
        public AuthSession? CurrentSession => new(userId, "test@test.local", "token", "refresh", DateTimeOffset.UtcNow.AddHours(1));
        public Guid? CurrentUserId => userId;
        public event EventHandler<AuthSession?>? AuthStateChanged { add { } remove { } }
        public Task<AuthSession> SignInWithPasswordAsync(string email, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SendMagicLinkAsync(string email, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SignOutAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdatePasswordAsync(string newPassword, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<AuthSession> SignUpWithPasswordAsync(string email, string password, string? username = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeClock(DateTimeOffset utcNow) : Domain.Abstractions.ISystemClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }

    private sealed class FakeProfiles(Guid userId) : IProfileRepository
    {
        public Task<Profile?> GetCurrentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<Profile?>(Profile.Create(userId, "test", "UTC", new MealWindows(new TimeOnly(8, 0), new TimeOnly(13, 0), new TimeOnly(19, 0))));
        public Task UpdateAsync(Profile profile, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeDoseEvents(IEnumerable<DoseEvent> items) : IDoseEventRepository
    {
        private readonly List<DoseEvent> _items = [.. items];
        public Task<IReadOnlyList<DoseEvent>> ListForLocalDateAsync(DateOnly localDate, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DoseEvent>>(_items.Where(e => e.LocalDate == localDate).ToArray());
        public Task<IReadOnlyList<DoseEvent>> ListByScheduleAsync(Guid scheduleId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DoseEvent>>(_items.Where(e => e.ScheduleId == scheduleId).ToArray());
        public Task UpsertManyAsync(IReadOnlyList<DoseEvent> events, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<DoseEvent?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(_items.FirstOrDefault(e => e.Id == id));
    }

    private sealed class FakeSchedules(Guid scheduleId, Guid courseId) : IScheduleRepository
    {
        private readonly Schedule _schedule = Schedule.CreateFixedTimes(scheduleId, courseId, WeekDays.All, 1, [new TimeOnly(11, 0), new TimeOnly(12, 0), new TimeOnly(12, 30)]);
        public Task<IReadOnlyList<Schedule>> ListByCourseAsync(Guid courseId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Schedule>>([_schedule]);
        public Task<Schedule?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Schedule?>(_schedule);
        public Task UpsertAsync(Schedule schedule, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeCourses(Guid courseId, Guid userId, Guid medicationId) : ICourseRepository
    {
        private readonly Course _course = Course.Create(courseId, userId, medicationId, new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1), 32);
        public Task<IReadOnlyList<Course>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Course>>([_course]);
        public Task<Course?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Course?>(_course);
        public Task UpsertAsync(Course course, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeMedications(Guid medicationId, Guid userId) : IMedicationRepository
    {
        private readonly Medication _med = Medication.Create(medicationId, userId, "Тест", "таблетка", "100 мг", "таб");
        public Task<IReadOnlyList<Medication>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Medication>>([_med]);
        public Task<Medication?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Medication?>(_med);
        public Task UpsertAsync(Medication medication, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeTransitions : IDoseTransitionService
    {
        public Task<DoseTransitionResult> ConfirmAsync(Guid doseEventId, DoseEventSource source, DateTimeOffset? takenAt = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DoseTransitionResult("Applied", doseEventId, DoseEventState.Taken, null));
        public Task<DoseTransitionResult> SkipAsync(Guid doseEventId, DoseEventSource source, DateTimeOffset? skippedAt = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DoseTransitionResult("Applied", doseEventId, DoseEventState.Skipped, null));
        public Task<DoseTransitionResult> UndoConfirmAsync(Guid doseEventId, DateTimeOffset? undoneAt = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DoseTransitionResult("Applied", doseEventId, DoseEventState.Scheduled, null));
    }

    private sealed class FakeMaterializer : IDoseEventMaterializer
    {
        public Task<int> MaterializeAsync(int horizonDays = 14, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class FakeRealtime : IDoseEventRealtime
    {
        public event EventHandler<DoseEventChange>? Changed { add { } remove { } }
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ImmediateUiDispatcher : IUiDispatcher
    {
        public void Post(Action action) => action();
    }
}
