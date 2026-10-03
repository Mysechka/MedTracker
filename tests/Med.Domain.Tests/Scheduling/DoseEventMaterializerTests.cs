using FluentAssertions;
using Med.Domain.Entities;
using Med.Domain.Enums;
using Med.Domain.Scheduling;
using Med.Domain.Tests.Fakes;
using Med.Domain.Tests.Support;
using Med.Domain.ValueObjects;
using Xunit;

namespace Med.Domain.Tests.Scheduling;

public sealed class DoseEventMaterializerTests
{
    [Fact]
    public void FixedTimes_идемпотентен_по_dedupe_key()
    {
        Profile profile = DomainFixtures.BerlinProfile();
        Course course = DomainFixtures.Course(new DateOnly(2026, 8, 25), new DateOnly(2026, 9, 10));
        Schedule schedule = Schedule.CreateFixedTimes(
            DomainFixtures.ScheduleId,
            course.Id,
            WeekDays.All,
            doseAmount: 1,
            [new TimeOnly(8, 0), new TimeOnly(20, 0)]);

        DateTimeOffset now = FakeClock.At("2026-08-25T05:00:00Z").UtcNow;

        IReadOnlyList<DoseEvent> first = DoseEventMaterializer.Materialize(course, schedule, profile, now);
        IReadOnlyList<DoseEvent> second = DoseEventMaterializer.Materialize(course, schedule, profile, now);

        first.Should().NotBeEmpty();
        first.Select(e => e.DedupeKey).Should().OnlyHaveUniqueItems();
        second.Select(e => e.DedupeKey).Should().Equal(first.Select(e => e.DedupeKey));
    }

    [Fact]
    public void FixedTimes_пропускает_невалидный_час_DST()
    {
        Profile profile = DomainFixtures.BerlinProfile();
        Course course = DomainFixtures.Course(new DateOnly(2026, 3, 28), new DateOnly(2026, 3, 30));
        Schedule schedule = Schedule.CreateFixedTimes(
            DomainFixtures.ScheduleId,
            course.Id,
            WeekDays.All,
            1,
            [new TimeOnly(2, 30)]);

        DateTimeOffset now = FakeClock.At("2026-03-28T00:00:00Z").UtcNow;
        IReadOnlyList<DoseEvent> events = DoseEventMaterializer.Materialize(course, schedule, profile, now);

        events.Select(e => e.LocalDate).Should().NotContain(new DateOnly(2026, 3, 29));
        events.Should().Contain(e => e.LocalDate == new DateOnly(2026, 3, 28));
        events.Should().Contain(e => e.LocalDate == new DateOnly(2026, 3, 30));
    }

    [Fact]
    public void Смена_таймзоны_меняет_UTC_инстанты_будущих_событий()
    {
        Course course = DomainFixtures.Course(new DateOnly(2026, 8, 25), new DateOnly(2026, 8, 25));
        Schedule schedule = Schedule.CreateFixedTimes(
            DomainFixtures.ScheduleId,
            course.Id,
            WeekDays.All,
            1,
            [new TimeOnly(12, 0)]);

        Profile berlin = DomainFixtures.BerlinProfile();
        Profile moscow = berlin.WithTimeZoneId("Europe/Moscow");
        DateTimeOffset now = FakeClock.At("2026-08-25T05:00:00Z").UtcNow;

        DateTimeOffset berlinUtc = DoseEventMaterializer.Materialize(course, schedule, berlin, now).Single().ScheduledAt;
        DateTimeOffset moscowUtc = DoseEventMaterializer.Materialize(course, schedule, moscow, now).Single().ScheduledAt;

        berlinUtc.Should().Be(new DateTimeOffset(2026, 8, 25, 10, 0, 0, TimeSpan.Zero));
        moscowUtc.Should().Be(new DateTimeOffset(2026, 8, 25, 9, 0, 0, TimeSpan.Zero));
        berlinUtc.Should().NotBe(moscowUtc);
    }

    [Fact]
    public void MealRelative_пересчитывает_будущие_после_сдвига_завтрака_Taken_не_трогает()
    {
        Profile profile = DomainFixtures.BerlinProfile(breakfast: new TimeOnly(8, 0));
        Course course = DomainFixtures.Course(new DateOnly(2026, 8, 25), new DateOnly(2026, 8, 27));
        Schedule schedule = Schedule.CreateMealRelative(
            DomainFixtures.ScheduleId,
            course.Id,
            WeekDays.All,
            doseAmount: 1,
            MealKind.Breakfast,
            MealRelation.Before,
            offsetMinutes: 30);

        DateTimeOffset now = FakeClock.At("2026-08-25T04:00:00Z").UtcNow;
        List<DoseEvent> existing = DoseEventMaterializer.Materialize(course, schedule, profile, now).ToList();
        DoseEvent first = existing[0];
        DoseEvent taken = DoseEvent.CreateScheduled(
            first.Id, first.CourseId, first.ScheduleId, first.ScheduledAt, first.LocalDate) with
        {
            State = DoseEventState.Taken,
            TakenAt = first.ScheduledAt,
            Source = DoseEventSource.App,
        };

        Profile shifted = profile.WithMeals(profile.Meals.WithBreakfast(new TimeOnly(9, 0)));
        IReadOnlyList<DoseEvent> rematerialized = DoseEventMaterializer.RematerializeFuture(
            course, schedule, shifted, now, [taken]);

        rematerialized.Should().NotContain(e => e.DedupeKey == taken.DedupeKey);
        rematerialized.Should().OnlyContain(e => e.State == DoseEventState.Scheduled);
        // Завтрак 9:00 − 30 мин = 8:30 Berlin (UTC+2 летом) → 06:30Z
        rematerialized.Should().Contain(e =>
            e.LocalDate == new DateOnly(2026, 8, 26)
            && e.ScheduledAt == new DateTimeOffset(2026, 8, 26, 6, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Interval_не_сдвигается_при_опоздании()
    {
        Profile profile = DomainFixtures.BerlinProfile();
        Course course = DomainFixtures.Course(new DateOnly(2026, 8, 25), new DateOnly(2026, 8, 26));
        Schedule schedule = Schedule.CreateInterval(
            DomainFixtures.ScheduleId,
            course.Id,
            WeekDays.All,
            doseAmount: 1,
            intervalHours: 8,
            anchorTime: new TimeOnly(8, 0));

        // «Опоздали» к первой дозе: сейчас 10:00 Berlin = 08:00Z
        DateTimeOffset lateNow = FakeClock.At("2026-08-25T08:00:00Z").UtcNow;
        IReadOnlyList<DoseEvent> events = DoseEventMaterializer.Materialize(course, schedule, profile, lateNow);

        // Первая (08:00 local = 06:00Z) уже в прошлом — не генерируется.
        // Следующие остаются на 16:00 и 00:00 local, без сдвига от опоздания.
        events.Select(e => e.ScheduledAt).Should().Equal(
            new DateTimeOffset(2026, 8, 25, 14, 0, 0, TimeSpan.Zero), // 16:00 Berlin
            new DateTimeOffset(2026, 8, 25, 22, 0, 0, TimeSpan.Zero), // 00:00 26 Berlin
            new DateTimeOffset(2026, 8, 26, 6, 0, 0, TimeSpan.Zero),  // 08:00 Berlin
            new DateTimeOffset(2026, 8, 26, 14, 0, 0, TimeSpan.Zero)); // 16:00 Berlin
    }

    [Fact]
    public void AsNeeded_не_материализуется()
    {
        Profile profile = DomainFixtures.BerlinProfile();
        Course course = DomainFixtures.Course(new DateOnly(2026, 8, 25), new DateOnly(2026, 9, 25));
        Schedule schedule = Schedule.CreateAsNeeded(DomainFixtures.ScheduleId, course.Id, 1);
        DateTimeOffset now = FakeClock.At("2026-08-25T05:00:00Z").UtcNow;

        DoseEventMaterializer.Materialize(course, schedule, profile, now).Should().BeEmpty();
    }

    [Fact]
    public void DaysOfWeek_All_включает_все_семь_дней()
    {
        WeekDays.All.Should().HaveFlag(WeekDays.Monday);
        WeekDays.All.Should().HaveFlag(WeekDays.Sunday);
        ((byte)WeekDays.All).Should().Be(0b0111_1111);
    }

    [Fact]
    public void Interval_длительный_курс_в_прошлом_успешно_материализуется_на_текущий_горизонт()
    {
        Profile profile = DomainFixtures.MoscowProfile(0);
        // Курс начался 60 дней назад и длится ещё 60 дней:
        DateOnly start = new(2026, 6, 26);
        DateOnly end = new(2026, 10, 26);
        Course course = DomainFixtures.Course(start, end);
        Schedule schedule = Schedule.CreateInterval(
            DomainFixtures.ScheduleId,
            course.Id,
            WeekDays.All,
            doseAmount: 1,
            intervalHours: 4,
            anchorTime: new TimeOnly(8, 0));

        // Текущее время — спустя 60 дней от старта курса
        DateTimeOffset now = FakeClock.At("2026-08-25T05:00:00Z").UtcNow;
        IReadOnlyList<DoseEvent> events = DoseEventMaterializer.Materialize(course, schedule, profile, now);

        events.Should().NotBeEmpty("длительный курс должен генерировать события на текущий горизонт");
        events.All(e => e.ScheduledAt >= now).Should().BeTrue();
        events.First().ScheduledAt.Should().Be(now);
    }

    [Fact]
    public void RematerializeFuture_при_трех_дозах_в_день_после_приема_первой_остальные_не_удаляются()
    {
        Profile profile = DomainFixtures.BerlinProfile();
        DateOnly today = new(2026, 8, 25);
        Course course = DomainFixtures.Course(today, today.AddDays(1));
        Schedule schedule = Schedule.CreateFixedTimes(
            DomainFixtures.ScheduleId,
            course.Id,
            WeekDays.All,
            doseAmount: 1,
            [new TimeOnly(8, 0), new TimeOnly(13, 0), new TimeOnly(19, 0)]);

        DateTimeOffset now = FakeClock.At("2026-08-25T05:00:00Z").UtcNow;
        IReadOnlyList<DoseEvent> initial = DoseEventMaterializer.Materialize(course, schedule, profile, now);
        initial.Count.Should().BeGreaterThanOrEqualTo(3);

        DoseEvent first = initial[0];
        DoseEvent taken = DoseEvent.CreateScheduled(
            first.Id, first.CourseId, first.ScheduleId, first.ScheduledAt, first.LocalDate) with
        {
            State = DoseEventState.Taken,
            TakenAt = first.ScheduledAt,
            Source = DoseEventSource.App,
        };

        IReadOnlyList<DoseEvent> rematerialized = DoseEventMaterializer.RematerializeFuture(
            course, schedule, profile, now, [taken]);

        rematerialized.Should().NotContain(e => e.DedupeKey == taken.DedupeKey);
        var remainingToday = rematerialized.Where(e => e.LocalDate == today).ToList();
        remainingToday.Should().HaveCount(2);
        remainingToday.Should().Contain(e => e.ScheduledAt == new DateTimeOffset(2026, 8, 25, 11, 0, 0, TimeSpan.Zero));
        remainingToday.Should().Contain(e => e.ScheduledAt == new DateTimeOffset(2026, 8, 25, 17, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void RematerializeFuture_WithMultipleDailyDoses_PreservesRemainingDoses()
    {
        Profile profile = DomainFixtures.BerlinProfile();
        DateOnly today = new(2026, 8, 25);
        Course course = DomainFixtures.Course(today, today.AddDays(1));
        Schedule schedule = Schedule.CreateFixedTimes(
            DomainFixtures.ScheduleId,
            course.Id,
            WeekDays.All,
            doseAmount: 1,
            [new TimeOnly(8, 0), new TimeOnly(14, 0), new TimeOnly(20, 0)]);

        DateTimeOffset now = FakeClock.At("2026-08-25T05:00:00Z").UtcNow;
        IReadOnlyList<DoseEvent> initial = DoseEventMaterializer.Materialize(course, schedule, profile, now);

        DoseEvent morningDose = initial.First(e => e.LocalDate == today && e.ScheduledAt.Hour == 6); // 08:00 Berlin = 06:00Z
        DoseEvent taken = morningDose with
        {
            State = DoseEventState.Taken,
            TakenAt = morningDose.ScheduledAt,
            Source = DoseEventSource.App,
        };

        IReadOnlyList<DoseEvent> rematerialized = DoseEventMaterializer.RematerializeFuture(
            course, schedule, profile, now, [taken]);

        rematerialized.Should().NotContain(e => e.DedupeKey == taken.DedupeKey);
        var remainingToday = rematerialized.Where(e => e.LocalDate == today).ToList();
        remainingToday.Should().HaveCount(2);
        remainingToday.Should().Contain(e => e.ScheduledAt == new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero)); // 14:00 local
        remainingToday.Should().Contain(e => e.ScheduledAt == new DateTimeOffset(2026, 8, 25, 18, 0, 0, TimeSpan.Zero)); // 20:00 local
    }
}
