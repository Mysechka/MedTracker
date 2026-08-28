using FluentAssertions;
using Med.Domain.Entities;
using Med.Domain.Enums;
using Med.Domain.Scheduling;
using Med.Domain.Tests.Fakes;
using Med.Domain.Tests.Support;
using Med.Domain.ValueObjects;
using Xunit;

namespace Med.Domain.Tests.Scheduling;

/// <summary>
/// Границы времени для схемы «Москва ± N часов»: пользователь задаёт смещение целым
/// числом часов, а материализация обязана дать корректный UTC-инстант.
/// </summary>
public sealed class MoscowOffsetSchedulingTests
{
    private static readonly DateOnly Day = new(2026, 8, 25);

    [Theory]
    [InlineData(0, 5)]   // Москва = UTC+3 → 08:00 local = 05:00Z
    [InlineData(4, 1)]   // Москва+4 = UTC+7 → 01:00Z
    [InlineData(-2, 7)]  // Москва−2 = UTC+1 → 07:00Z
    [InlineData(-3, 8)]  // Москва−3 = UTC+0 → 08:00Z
    public void FixedTimes_учитывает_смещение_от_Москвы(int offsetHours, int expectedUtcHour)
    {
        Profile profile = DomainFixtures.MoscowProfile(offsetHours);
        Course course = DomainFixtures.Course(Day, Day);
        Schedule schedule = Schedule.CreateFixedTimes(
            DomainFixtures.ScheduleId,
            course.Id,
            WeekDays.All,
            doseAmount: 1,
            [new TimeOnly(8, 0)]);

        DateTimeOffset now = FakeClock.At("2026-08-24T00:00:00Z").UtcNow;

        DoseEvent single = DoseEventMaterializer.Materialize(course, schedule, profile, now).Single();

        single.ScheduledAt.Should().Be(new DateTimeOffset(2026, 8, 25, expectedUtcHour, 0, 0, TimeSpan.Zero));
        single.LocalDate.Should().Be(Day);
    }

    [Fact]
    public void Приём_в_00_30_при_смещении_плюс_4_попадает_в_предыдущие_сутки_UTC()
    {
        Profile profile = DomainFixtures.MoscowProfile(offsetHours: 4);
        Course course = DomainFixtures.Course(Day, Day);
        Schedule schedule = Schedule.CreateFixedTimes(
            DomainFixtures.ScheduleId,
            course.Id,
            WeekDays.All,
            doseAmount: 1,
            [new TimeOnly(0, 30)]);

        DateTimeOffset now = FakeClock.At("2026-08-24T00:00:00Z").UtcNow;

        DoseEvent single = DoseEventMaterializer.Materialize(course, schedule, profile, now).Single();

        // 00:30 при UTC+7 — это 17:30Z предыдущего дня, но локальная дата остаётся датой приёма.
        single.ScheduledAt.Should().Be(new DateTimeOffset(2026, 8, 24, 17, 30, 0, TimeSpan.Zero));
        single.LocalDate.Should().Be(Day);
    }

    [Fact]
    public void Приём_в_00_30_при_смещении_минус_12_попадает_в_следующие_сутки_UTC()
    {
        Profile profile = DomainFixtures.MoscowProfile(offsetHours: MoscowOffset.MinHours);
        Course course = DomainFixtures.Course(Day, Day);
        Schedule schedule = Schedule.CreateFixedTimes(
            DomainFixtures.ScheduleId,
            course.Id,
            WeekDays.All,
            doseAmount: 1,
            [new TimeOnly(0, 30)]);

        DateTimeOffset now = FakeClock.At("2026-08-24T00:00:00Z").UtcNow;

        DoseEvent single = DoseEventMaterializer.Materialize(course, schedule, profile, now).Single();

        // Москва−12 = UTC−9 → 00:30 local = 09:30Z тех же суток.
        single.ScheduledAt.Should().Be(new DateTimeOffset(2026, 8, 25, 9, 30, 0, TimeSpan.Zero));
        single.LocalDate.Should().Be(Day);
    }

    [Fact]
    public void Смена_смещения_пересчитывает_будущие_события_и_не_трогает_Taken()
    {
        Profile moscow = DomainFixtures.MoscowProfile(offsetHours: 0);
        Course course = DomainFixtures.Course(Day, Day.AddDays(2));
        Schedule schedule = Schedule.CreateFixedTimes(
            DomainFixtures.ScheduleId,
            course.Id,
            WeekDays.All,
            doseAmount: 1,
            [new TimeOnly(12, 0)]);

        DateTimeOffset now = FakeClock.At("2026-08-24T00:00:00Z").UtcNow;

        DoseEvent first = DoseEventMaterializer.Materialize(course, schedule, moscow, now)[0];
        DoseEvent taken = DoseEvent.CreateScheduled(
            first.Id, first.CourseId, first.ScheduleId, first.ScheduledAt, first.LocalDate) with
        {
            State = DoseEventState.Taken,
            TakenAt = first.ScheduledAt,
            Source = DoseEventSource.App,
        };

        Profile moved = moscow.WithTimeZoneId(MoscowOffset.FromHours(4).ToTimeZoneId());
        IReadOnlyList<DoseEvent> rematerialized = DoseEventMaterializer.RematerializeFuture(
            course, schedule, moved, now, [taken]);

        rematerialized.Should().NotContain(e => e.DedupeKey == taken.DedupeKey);
        rematerialized.Should().OnlyContain(e => e.State == DoseEventState.Scheduled);
        // 12:00 при UTC+7 → 05:00Z.
        rematerialized.Should().Contain(e =>
            e.LocalDate == Day.AddDays(1)
            && e.ScheduledAt == new DateTimeOffset(2026, 8, 26, 5, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Смещение_не_имеет_переходов_на_летнее_время()
    {
        Profile profile = DomainFixtures.MoscowProfile(offsetHours: 4);
        Course course = DomainFixtures.Course(new DateOnly(2026, 3, 28), new DateOnly(2026, 3, 30));
        Schedule schedule = Schedule.CreateFixedTimes(
            DomainFixtures.ScheduleId,
            course.Id,
            WeekDays.All,
            doseAmount: 1,
            [new TimeOnly(2, 30)]);

        DateTimeOffset now = FakeClock.At("2026-03-27T00:00:00Z").UtcNow;

        IReadOnlyList<DoseEvent> events = DoseEventMaterializer.Materialize(course, schedule, profile, now);

        // Ни один день не выпадает: у Москвы и у фиксированных зон DST нет.
        events.Select(e => e.LocalDate).Should().Equal(
            new DateOnly(2026, 3, 28),
            new DateOnly(2026, 3, 29),
            new DateOnly(2026, 3, 30));
    }
}
