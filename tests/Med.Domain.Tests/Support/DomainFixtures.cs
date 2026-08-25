using Med.Domain.Entities;
using Med.Domain.Enums;
using Med.Domain.ValueObjects;

namespace Med.Domain.Tests.Support;

internal static class DomainFixtures
{
    public static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid MedicationId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid CourseId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid ScheduleId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    public static Profile BerlinProfile(
        string username = "brenda",
        TimeOnly? breakfast = null,
        TimeSpan? confirmationWindow = null) =>
        Profile.Create(
            UserId,
            username,
            "Europe/Berlin",
            new MealWindows(
                breakfast ?? new TimeOnly(8, 0),
                new TimeOnly(13, 0),
                new TimeOnly(19, 0)),
            confirmationWindow);

    public static Course Course(
        DateOnly startsOn,
        DateOnly endsOn,
        bool isActive = true) =>
        Med.Domain.Entities.Course.Create(
            CourseId,
            UserId,
            MedicationId,
            startsOn,
            endsOn,
            durationDays: endsOn.DayNumber - startsOn.DayNumber + 1,
            isActive);

    public static Func<Guid> SequentialIds(params Guid[] ids)
    {
        int i = 0;
        return () => ids[i++];
    }
}
