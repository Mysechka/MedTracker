using FluentAssertions;
using Med.Domain.Entities;
using Med.Domain.ValueObjects;
using Xunit;

namespace Med.Domain.Tests.DomainEntities;

public sealed class CourseTests
{
    [Fact]
    public void EndsOn_и_DurationDays_хранятся_оба_и_согласуются()
    {
        DateOnly start = new(2026, 8, 25);
        Course course = Course.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            start,
            endsOn: new DateOnly(2026, 8, 31),
            durationDays: 7);

        course.EndsOn.Should().Be(new DateOnly(2026, 8, 31));
        course.DurationDays.Should().Be(7);
        course.EffectiveEndsOn.Should().Be(new DateOnly(2026, 8, 31));
    }

    [Fact]
    public void Несогласованные_EndsOn_и_DurationDays_отклоняются()
    {
        Action act = () => Course.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateOnly(2026, 8, 25),
            endsOn: new DateOnly(2026, 8, 31),
            durationDays: 3);

        act.Should().Throw<ArgumentException>();
    }
}

public sealed class DedupeKeyTests
{
    [Fact]
    public void Формат_стабилен()
    {
        Guid scheduleId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        DateTimeOffset at = new(2026, 8, 25, 6, 0, 0, TimeSpan.Zero);

        DedupeKey key = DedupeKey.Create(scheduleId, at);

        key.Value.Should().Be("44444444444444444444444444444444|2026-08-25T06:00:00.0000000Z");
    }
}
