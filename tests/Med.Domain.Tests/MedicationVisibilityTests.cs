using FluentAssertions;
using Med.Domain.Entities;
using Xunit;

namespace Med.Domain.Tests;

public sealed class MedicationVisibilityTests
{
    [Fact]
    public void NoCourses_AlwaysVisible()
    {
        DateOnly today = new(2026, 10, 2);
        IReadOnlyList<Course> courses = [];

        bool visible = MedicationVisibility.IsVisibleOnDate(courses, today);

        visible.Should().BeTrue();
    }

    [Fact]
    public void PermanentCourse_AlwaysVisible()
    {
        DateOnly today = new(2026, 10, 2);
        Course course = Course.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateOnly(2026, 9, 1),
            endsOn: null,
            durationDays: null);

        bool visible = MedicationVisibility.IsVisibleOnDate([course], today);

        visible.Should().BeTrue();
    }

    [Fact]
    public void ExpiredCourse_NotVisible()
    {
        DateOnly today = new(2026, 10, 2);
        Course course = Course.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateOnly(2026, 9, 1),
            endsOn: new DateOnly(2026, 9, 15),
            durationDays: 15);

        bool visible = MedicationVisibility.IsVisibleOnDate([course], today);

        visible.Should().BeFalse();
    }

    [Fact]
    public void MixedCourses_VisibleIfAnyActive()
    {
        DateOnly today = new(2026, 10, 2);
        Course expired = Course.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateOnly(2026, 9, 1),
            endsOn: new DateOnly(2026, 9, 15),
            durationDays: 15);
        Course permanent = Course.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateOnly(2026, 9, 20),
            endsOn: null,
            durationDays: null);

        bool visible = MedicationVisibility.IsVisibleOnDate([expired, permanent], today);

        visible.Should().BeTrue();
    }

    [Fact]
    public void MultiplePermanentCourses_Visible()
    {
        DateOnly today = new(2026, 10, 2);
        Course permanent1 = Course.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateOnly(2026, 9, 1),
            endsOn: null,
            durationDays: null);
        Course permanent2 = Course.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateOnly(2026, 9, 10),
            endsOn: null,
            durationDays: null);

        bool visible = MedicationVisibility.IsVisibleOnDate([permanent1, permanent2], today);

        visible.Should().BeTrue();
    }
}
