using FluentAssertions;
using Med.Application.Abstractions;
using Med.Application.Agenda;
using Med.Application.UseCases;
using Med.Domain.Entities;
using Med.Domain.Enums;
using Med.Domain.Tests.Fakes;
using Med.Domain.ValueObjects;
using Xunit;

namespace Med.Application.Tests.UseCases;

public sealed class GetDayAgendaUseCaseTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid MedicationId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid CourseId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ScheduleId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact]
    public async Task Подставляет_лекарство_и_дозу_и_сортирует_по_времени()
    {
        DateOnly localDate = new(2026, 8, 27);
        DoseEvent evening = Dose("2026-08-27T17:00:00Z", localDate, Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"));
        DoseEvent morning = Dose("2026-08-27T05:00:00Z", localDate, Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"));

        GetDayAgendaUseCase useCase = NewUseCase(
            "2026-08-27T09:00:00Z",
            [evening, morning],
            withMedication: true);

        DayAgenda agenda = await useCase.ExecuteAsync(TestContext.Current.CancellationToken);

        agenda.LocalDate.Should().Be(localDate);
        agenda.TimeZoneId.Should().Be("Europe/Moscow");
        agenda.Items.Should().HaveCount(2);

        agenda.Items[0].DoseEventId.Should().Be(morning.Id);
        agenda.Items[0].LocalTime.Should().Be(new TimeOnly(8, 0));
        agenda.Items[0].MedicationName.Should().Be("Магний B6");
        agenda.Items[0].Dosage.Should().Be("500 мг");
        agenda.Items[0].Unit.Should().Be("таб");
        agenda.Items[0].DoseAmount.Should().Be(1m);

        agenda.Items[1].DoseEventId.Should().Be(evening.Id);
        agenda.Items[1].LocalTime.Should().Be(new TimeOnly(20, 0));
    }

    [Fact]
    public async Task Событие_без_доступного_лекарства_остаётся_в_списке_без_названия()
    {
        DateOnly localDate = new(2026, 8, 27);
        DoseEvent dose = Dose("2026-08-27T05:00:00Z", localDate, Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"));

        GetDayAgendaUseCase useCase = NewUseCase(
            "2026-08-27T09:00:00Z",
            [dose],
            withMedication: false);

        DayAgenda agenda = await useCase.ExecuteAsync(TestContext.Current.CancellationToken);

        agenda.Items.Should().ContainSingle();
        agenda.Items[0].MedicationName.Should().BeNull();
        agenda.Items[0].Dosage.Should().BeNull();
        agenda.Items[0].DoseAmount.Should().Be(1m);
    }

    [Fact]
    public async Task Приём_в_00_30_попадает_в_локальный_день_а_не_в_UTC_день()
    {
        // 21:30Z — это 00:30 следующего дня по Москве.
        DateOnly localDate = new(2026, 8, 28);
        DoseEvent dose = Dose("2026-08-27T21:30:00Z", localDate, Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003"));

        GetDayAgendaUseCase useCase = NewUseCase(
            "2026-08-27T22:00:00Z",
            [dose],
            withMedication: true);

        DayAgenda agenda = await useCase.ExecuteAsync(TestContext.Current.CancellationToken);

        agenda.LocalDate.Should().Be(localDate);
        agenda.Items.Should().ContainSingle();
        agenda.Items[0].LocalTime.Should().Be(new TimeOnly(0, 30));
    }

    [Fact]
    public async Task Пустой_день_не_дёргает_справочники()
    {
        FakeCourses courses = new([Course()]);
        FakeMedications medications = new([Medication()]);

        GetDayAgendaUseCase useCase = new(
            new FakeDoseEvents([]),
            new FakeSchedules([Schedule()]),
            courses,
            medications,
            new FakeProfiles(MoscowProfile()),
            FakeClock.At("2026-08-27T09:00:00Z"));

        DayAgenda agenda = await useCase.ExecuteAsync(TestContext.Current.CancellationToken);

        agenda.Items.Should().BeEmpty();
        courses.ListCalls.Should().Be(0);
        medications.ListCalls.Should().Be(0);
    }

    private static GetDayAgendaUseCase NewUseCase(
        string utcNow,
        IReadOnlyList<DoseEvent> doses,
        bool withMedication) =>
        new(
            new FakeDoseEvents(doses),
            new FakeSchedules([Schedule()]),
            new FakeCourses([Course()]),
            new FakeMedications(withMedication ? [Medication()] : []),
            new FakeProfiles(MoscowProfile()),
            FakeClock.At(utcNow));

    private static DoseEvent Dose(string scheduledAtIso, DateOnly localDate, Guid id) =>
        DoseEvent.CreateScheduled(
            id,
            CourseId,
            ScheduleId,
            DateTimeOffset.Parse(scheduledAtIso).ToUniversalTime(),
            localDate);

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
            [new TimeOnly(8, 0), new TimeOnly(20, 0)]);

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

    private sealed class FakeSchedules(IReadOnlyList<Schedule> seed) : IScheduleRepository
    {
        public Task<IReadOnlyList<Schedule>> ListByCourseAsync(
            Guid courseId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Schedule>>(seed.Where(s => s.CourseId == courseId).ToArray());

        public Task<Schedule?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(seed.FirstOrDefault(s => s.Id == id));

        public Task UpsertAsync(Schedule schedule, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeCourses(IReadOnlyList<Course> seed) : ICourseRepository
    {
        public int ListCalls { get; private set; }

        public Task<IReadOnlyList<Course>> ListAsync(CancellationToken cancellationToken = default)
        {
            ListCalls++;
            return Task.FromResult(seed);
        }

        public Task<Course?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(seed.FirstOrDefault(c => c.Id == id));

        public Task UpsertAsync(Course course, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeMedications(IReadOnlyList<Medication> seed) : IMedicationRepository
    {
        public int ListCalls { get; private set; }

        public Task<IReadOnlyList<Medication>> ListAsync(CancellationToken cancellationToken = default)
        {
            ListCalls++;
            return Task.FromResult(seed);
        }

        public Task<Medication?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(seed.FirstOrDefault(m => m.Id == id));

        public Task UpsertAsync(Medication medication, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeProfiles(Profile profile) : IProfileRepository
    {
        public Task<Profile?> GetCurrentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<Profile?>(profile);

        public Task UpdateAsync(Profile profile, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
