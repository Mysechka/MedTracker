using FluentAssertions;
using Med.Domain.Entities;
using Xunit;

namespace Med.Domain.Tests.Entities;

public sealed class CourseVisibilityTests
{
    [Fact]
    public void PermanentCourse_EffectiveEndsOn_IsMaxValue()
    {
        DateOnly start = new(2026, 10, 1);
        Course course = Course.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            start,
            endsOn: null,
            durationDays: null);

        course.EffectiveEndsOn.Should().Be(DateOnly.MaxValue);
    }

    [Fact]
    public void ExpiredCourse_ContainsDate_ReturnsFalse()
    {
        DateOnly start = new(2026, 9, 1);
        DateOnly end = new(2026, 9, 14);
        Course course = Course.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            start,
            endsOn: end,
            durationDays: 14);

        DateOnly today = new(2026, 10, 2);
        course.ContainsDate(today).Should().BeFalse();
    }

    [Fact]
    public void ActiveCourse_ContainsDate_ReturnsTrue()
    {
        DateOnly start = new(2026, 10, 1);
        DateOnly end = new(2026, 10, 14);
        Course course = Course.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            start,
            endsOn: end,
            durationDays: 14);

        DateOnly today = new(2026, 10, 2);
        course.ContainsDate(today).Should().BeTrue();
    }

    [Fact]
    public void Course_Create_AllowsNullEndsAndDuration()
    {
        Action act = () => Course.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateOnly(2026, 10, 1),
            endsOn: null,
            durationDays: null);

        act.Should().NotThrow();
    }
}
