using BenchmarkDotNet.Attributes;
using Med.Domain.Entities;
using Med.Domain.Enums;
using Med.Domain.Scheduling;
using Med.Domain.ValueObjects;

namespace Med.Performance.Tests;

[MemoryDiagnoser]
public class ScheduleCalculationBenchmarks
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid CourseId = Guid.NewGuid();
    private static readonly DateTimeOffset UtcNow = DateTimeOffset.Parse("2026-08-27T00:00:00Z");

    private Profile _profile = null!;
    private Course _course = null!;
    private Schedule _fixedTimesSchedule = null!;
    private Schedule _intervalSchedule = null!;
    private Schedule _mealRelativeSchedule = null!;

    [GlobalSetup]
    public void Setup()
    {
        _profile = Profile.Create(
            UserId,
            "test_user",
            "Europe/Moscow",
            new MealWindows(new TimeOnly(8, 0), new TimeOnly(13, 0), new TimeOnly(19, 0)));

        _course = Course.Create(
            CourseId,
            UserId,
            Guid.NewGuid(),
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 9, 30),
            durationDays: 60,
            isActive: true);

        _fixedTimesSchedule = Schedule.CreateFixedTimes(
            Guid.NewGuid(),
            CourseId,
            WeekDays.All,
            doseAmount: 1.0m,
            [new TimeOnly(8, 0), new TimeOnly(14, 0), new TimeOnly(20, 0)]);

        _intervalSchedule = Schedule.CreateInterval(
            Guid.NewGuid(),
            CourseId,
            WeekDays.All,
            doseAmount: 1.0m,
            intervalHours: 8,
            anchorTime: new TimeOnly(8, 0));

        _mealRelativeSchedule = Schedule.CreateMealRelative(
            Guid.NewGuid(),
            CourseId,
            WeekDays.All,
            doseAmount: 1.0m,
            MealKind.Breakfast,
            MealRelation.Before,
            offsetMinutes: 15);
    }

    [Benchmark]
    public IReadOnlyList<DoseEvent> Materialize_FixedTimes_14Days() =>
        DoseEventMaterializer.Materialize(_course, _fixedTimesSchedule, _profile, UtcNow, TimeSpan.FromDays(14));

    [Benchmark]
    public IReadOnlyList<DoseEvent> Materialize_FixedTimes_30Days() =>
        DoseEventMaterializer.Materialize(_course, _fixedTimesSchedule, _profile, UtcNow, TimeSpan.FromDays(30));

    [Benchmark]
    public IReadOnlyList<DoseEvent> Materialize_Interval_14Days() =>
        DoseEventMaterializer.Materialize(_course, _intervalSchedule, _profile, UtcNow, TimeSpan.FromDays(14));

    [Benchmark]
    public IReadOnlyList<DoseEvent> Materialize_MealRelative_14Days() =>
        DoseEventMaterializer.Materialize(_course, _mealRelativeSchedule, _profile, UtcNow, TimeSpan.FromDays(14));
}
