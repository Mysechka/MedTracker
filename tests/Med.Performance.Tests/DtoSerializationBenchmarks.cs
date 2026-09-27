using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Med.Infrastructure.Models;

namespace Med.Performance.Tests;

[MemoryDiagnoser]
public class DtoSerializationBenchmarks
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private DoseEventRow _doseEventRow = null!;
    private MedicationRow _medicationRow = null!;
    private ScheduleRow _scheduleRow = null!;
    private ProfileRow _profileRow = null!;

    private string _doseEventJson = string.Empty;
    private string _medicationJson = string.Empty;
    private string _scheduleJson = string.Empty;
    private string _profileJson = string.Empty;

    [GlobalSetup]
    public void Setup()
    {
        _doseEventRow = new DoseEventRow
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            CourseId = Guid.NewGuid(),
            ScheduleId = Guid.NewGuid(),
            ScheduledAt = DateTimeOffset.UtcNow,
            LocalDate = "2026-08-27",
            State = "scheduled",
            DedupeKey = "dedupe-12345"
        };
        _doseEventJson = JsonSerializer.Serialize(_doseEventRow, JsonOptions);

        _medicationRow = new MedicationRow
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Name = "Магний B6 Форте",
            Form = "таблетка",
            Dosage = "500 мг",
            Unit = "таб",
            Notes = "Принимать во время еды"
        };
        _medicationJson = JsonSerializer.Serialize(_medicationRow, JsonOptions);

        _scheduleRow = new ScheduleRow
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            CourseId = Guid.NewGuid(),
            Type = "fixed_times",
            DoseAmount = 1.0m,
            Monday = true,
            Tuesday = true,
            Wednesday = true,
            Thursday = true,
            Friday = true,
            Saturday = true,
            Sunday = true,
            FixedTimes = [new TimeSpan(8, 0, 0), new TimeSpan(14, 0, 0), new TimeSpan(20, 0, 0)]
        };
        _scheduleJson = JsonSerializer.Serialize(_scheduleRow, JsonOptions);

        _profileRow = new ProfileRow
        {
            Id = Guid.NewGuid(),
            Username = "brenda",
            TimeZoneId = "Europe/Moscow",
            BreakfastTime = new TimeSpan(8, 0, 0),
            LunchTime = new TimeSpan(13, 0, 0),
            DinnerTime = new TimeSpan(19, 0, 0),
            ConfirmationWindowMinutes = 180
        };
        _profileJson = JsonSerializer.Serialize(_profileRow, JsonOptions);
    }

    [Benchmark]
    public string Serialize_DoseEvent() => JsonSerializer.Serialize(_doseEventRow, JsonOptions);

    [Benchmark]
    public DoseEventRow? Deserialize_DoseEvent() => JsonSerializer.Deserialize<DoseEventRow>(_doseEventJson, JsonOptions);

    [Benchmark]
    public string Serialize_Medication() => JsonSerializer.Serialize(_medicationRow, JsonOptions);

    [Benchmark]
    public MedicationRow? Deserialize_Medication() => JsonSerializer.Deserialize<MedicationRow>(_medicationJson, JsonOptions);

    [Benchmark]
    public string Serialize_Schedule() => JsonSerializer.Serialize(_scheduleRow, JsonOptions);

    [Benchmark]
    public ScheduleRow? Deserialize_Schedule() => JsonSerializer.Deserialize<ScheduleRow>(_scheduleJson, JsonOptions);

    [Benchmark]
    public string Serialize_Profile() => JsonSerializer.Serialize(_profileRow, JsonOptions);

    [Benchmark]
    public ProfileRow? Deserialize_Profile() => JsonSerializer.Deserialize<ProfileRow>(_profileJson, JsonOptions);
}
