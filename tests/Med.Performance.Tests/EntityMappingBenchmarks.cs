using BenchmarkDotNet.Attributes;
using Med.Domain.Entities;
using Med.Domain.Enums;
using Med.Domain.ValueObjects;
using Med.Infrastructure.Mapping;
using Med.Infrastructure.Models;

namespace Med.Performance.Tests;

[MemoryDiagnoser]
public class EntityMappingBenchmarks
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid CourseId = Guid.NewGuid();
    private static readonly Guid MedicationId = Guid.NewGuid();
    private static readonly Guid ScheduleId = Guid.NewGuid();
    private static readonly Guid DoseId = Guid.NewGuid();

    private Profile _profile = null!;
    private ProfileRow _profileRow = null!;
    private Medication _medication = null!;
    private MedicationRow _medicationRow = null!;
    private Course _course = null!;
    private CourseRow _courseRow = null!;
    private Schedule _schedule = null!;
    private ScheduleRow _scheduleRow = null!;
    private DoseEvent _doseEvent = null!;
    private DoseEventRow _doseEventRow = null!;

    [GlobalSetup]
    public void Setup()
    {
        _profile = Profile.Create(
            UserId,
            "john_doe",
            "Europe/Moscow",
            new MealWindows(new TimeOnly(8, 0), new TimeOnly(13, 0), new TimeOnly(19, 0)));
        _profileRow = EntityMappers.ToRow(_profile);

        _medication = Medication.Create(
            MedicationId,
            UserId,
            "Амоксициллин",
            "капсула",
            "500 мг",
            "капс",
            "Принимать после еды");
        _medicationRow = EntityMappers.ToRow(_medication);

        _course = Course.Create(
            CourseId,
            UserId,
            MedicationId,
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 14),
            durationDays: 14,
            isActive: true);
        _courseRow = EntityMappers.ToRow(_course);

        _schedule = Schedule.CreateFixedTimes(
            ScheduleId,
            CourseId,
            WeekDays.All,
            1.0m,
            [new TimeOnly(8, 0), new TimeOnly(20, 0)]);
        _scheduleRow = EntityMappers.ToRow(_schedule, UserId);

        _doseEvent = DoseEvent.CreateScheduled(
            DoseId,
            CourseId,
            ScheduleId,
            DateTimeOffset.UtcNow,
            new DateOnly(2026, 8, 27));
        _doseEventRow = EntityMappers.ToRow(_doseEvent, UserId);
    }

    [Benchmark]
    public ProfileRow Profile_ToRow() => EntityMappers.ToRow(_profile);

    [Benchmark]
    public Profile Profile_ToDomain() => EntityMappers.ToDomain(_profileRow);

    [Benchmark]
    public MedicationRow Medication_ToRow() => EntityMappers.ToRow(_medication);

    [Benchmark]
    public Medication Medication_ToDomain() => EntityMappers.ToDomain(_medicationRow);

    [Benchmark]
    public CourseRow Course_ToRow() => EntityMappers.ToRow(_course);

    [Benchmark]
    public Course Course_ToDomain() => EntityMappers.ToDomain(_courseRow);

    [Benchmark]
    public ScheduleRow Schedule_ToRow() => EntityMappers.ToRow(_schedule, UserId);

    [Benchmark]
    public Schedule Schedule_ToDomain() => EntityMappers.ToDomain(_scheduleRow);

    [Benchmark]
    public DoseEventRow DoseEvent_ToRow() => EntityMappers.ToRow(_doseEvent, UserId);

    [Benchmark]
    public DoseEvent DoseEvent_ToDomain() => EntityMappers.ToDomain(_doseEventRow);
}
