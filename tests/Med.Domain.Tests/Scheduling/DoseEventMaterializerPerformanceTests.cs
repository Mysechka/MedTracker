using System.Diagnostics;
using FluentAssertions;
using Med.Domain.Entities;
using Med.Domain.Enums;
using Med.Domain.Scheduling;
using Med.Domain.Tests.Fakes;
using Med.Domain.Tests.Support;
using Med.Domain.ValueObjects;
using Xunit;

namespace Med.Domain.Tests.Scheduling;

public sealed class DoseEventMaterializerPerformanceTests
{
    [Fact]
    public void Материализация_года_FixedTimes_укладывается_в_бюджет()
    {
        Profile profile = DomainFixtures.BerlinProfile();
        Course course = DomainFixtures.Course(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        Schedule schedule = Schedule.CreateFixedTimes(
            DomainFixtures.ScheduleId,
            course.Id,
            WeekDays.All,
            1,
            [new TimeOnly(8, 0), new TimeOnly(14, 0), new TimeOnly(20, 0)]);

        DateTimeOffset now = FakeClock.At("2026-01-01T00:00:00Z").UtcNow;

        Stopwatch sw = Stopwatch.StartNew();
        IReadOnlyList<DoseEvent> events = DoseEventMaterializer.Materialize(
            course,
            schedule,
            profile,
            now,
            horizon: TimeSpan.FromDays(365));
        sw.Stop();

        events.Should().NotBeEmpty();
        events.Select(e => e.DedupeKey).Should().OnlyHaveUniqueItems();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
    }
}
