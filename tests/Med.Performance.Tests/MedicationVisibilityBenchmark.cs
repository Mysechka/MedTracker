using BenchmarkDotNet.Attributes;
using Med.Domain;
using Med.Domain.Entities;

namespace Med.Performance.Tests;

[MemoryDiagnoser]
public class MedicationVisibilityBenchmark
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 10, 2);

    [Params(100, 500, 1000)]
    public int Count { get; set; }

    private List<(Medication Medication, IReadOnlyList<Course> Courses)> _items = null!;

    [GlobalSetup]
    public void Setup()
    {
        _items = new List<(Medication, IReadOnlyList<Course>)>(Count);

        for (int i = 0; i < Count; i++)
        {
            Guid medId = Guid.NewGuid();
            var med = Medication.Create(
                medId,
                UserId,
                $"Лекарство {i}",
                "Таблетка",
                "100",
                "мг");

            var courses = new List<Course>();

            // 25% without courses, 25% permanent, 25% active timed, 25% expired
            switch (i % 4)
            {
                case 0:
                    // No courses
                    break;
                case 1:
                    // Permanent course
                    courses.Add(Course.Create(
                        Guid.NewGuid(),
                        UserId,
                        medId,
                        new DateOnly(2026, 9, 1),
                        endsOn: null,
                        durationDays: null));
                    break;
                case 2:
                    // Active timed course
                    courses.Add(Course.Create(
                        Guid.NewGuid(),
                        UserId,
                        medId,
                        new DateOnly(2026, 9, 15),
                        new DateOnly(2026, 10, 15),
                        durationDays: 30));
                    break;
                case 3:
                    // Expired course
                    courses.Add(Course.Create(
                        Guid.NewGuid(),
                        UserId,
                        medId,
                        new DateOnly(2026, 8, 1),
                        new DateOnly(2026, 9, 1),
                        durationDays: 31));
                    break;
            }

            _items.Add((med, courses));
        }
    }

    [Benchmark]
    public int FilterVisibleMedications()
    {
        int visibleCount = 0;
        for (int i = 0; i < _items.Count; i++)
        {
            if (MedicationVisibility.IsVisibleOnDate(_items[i].Courses, Today))
            {
                visibleCount++;
            }
        }
        return visibleCount;
    }
}
