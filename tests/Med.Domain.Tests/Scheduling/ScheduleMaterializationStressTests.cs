using System.Diagnostics;
using FluentAssertions;
using Med.Domain.Entities;
using Med.Domain.Enums;
using Med.Domain.Scheduling;
using Med.Domain.ValueObjects;
using Xunit;

namespace Med.Domain.Tests.Scheduling;

public sealed class ScheduleMaterializationStressTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Стресс_тест_материализации_50_сложных_курсов_на_30_дней()
    {
        Profile profile = Profile.Create(
            UserId,
            "test_user",
            "Europe/Moscow",
            new MealWindows(new TimeOnly(8, 30), new TimeOnly(13, 30), new TimeOnly(19, 30)),
            TimeSpan.FromHours(2));

        DateOnly startsOn = new(2026, 9, 1);
        DateTimeOffset utcNow = DateTimeOffset.Parse("2026-09-01T00:00:00Z");
        TimeSpan horizon = TimeSpan.FromDays(30);

        List<(Course Course, Schedule Schedule)> dataset = [];

        // 1. 20 курсов с фиксированным временем (от 1 до 4 приёмов в день)
        for (int i = 0; i < 20; i++)
        {
            Guid courseId = Guid.NewGuid();
            Course course = Course.Create(
                courseId,
                UserId,
                Guid.NewGuid(),
                startsOn,
                endsOn: startsOn.AddDays(29),
                durationDays: 30);

            TimeOnly[] times = [
                new(8, 0),
                new(13, 0),
                new(18, 0),
                new(22, 0)
            ];

            Schedule schedule = Schedule.CreateFixedTimes(
                Guid.NewGuid(),
                courseId,
                WeekDays.All,
                doseAmount: 1,
                times.Take((i % 4) + 1).ToArray());
            dataset.Add((course, schedule));
        }

        // 2. 20 курсов, привязанных к приёмам пищи (Breakfast, Lunch, Dinner, Before/After со сдвигом)
        for (int i = 0; i < 20; i++)
        {
            Guid courseId = Guid.NewGuid();
            Course course = Course.Create(
                courseId,
                UserId,
                Guid.NewGuid(),
                startsOn,
                endsOn: startsOn.AddDays(29),
                durationDays: 30);

            MealKind meal = (i % 3) switch
            {
                0 => MealKind.Breakfast,
                1 => MealKind.Lunch,
                _ => MealKind.Dinner,
            };
            MealRelation relation = (i % 2 == 0) ? MealRelation.Before : MealRelation.After;
            int offsetMinutes = 15 * ((i % 4) + 1);

            Schedule schedule = Schedule.CreateMealRelative(
                Guid.NewGuid(),
                courseId,
                WeekDays.All,
                doseAmount: 1,
                meal,
                relation,
                offsetMinutes);
            dataset.Add((course, schedule));
        }

        // 3. 15 курсов с интервалами (каждые 4, 6, 8 часов)
        for (int i = 0; i < 15; i++)
        {
            Guid courseId = Guid.NewGuid();
            Course course = Course.Create(
                courseId,
                UserId,
                Guid.NewGuid(),
                startsOn,
                endsOn: startsOn.AddDays(29),
                durationDays: 30);

            int intervalHours = 4 + (i % 3) * 2;
            Schedule schedule = Schedule.CreateInterval(
                Guid.NewGuid(),
                courseId,
                WeekDays.All,
                doseAmount: 1,
                intervalHours,
                new TimeOnly(8, 0));
            dataset.Add((course, schedule));
        }

        dataset.Count.Should().Be(55, "всего создано 55 активных курсов с разнообразными расписаниями");

        // Принудительный сбор мусора перед замером аллокаций
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        Stopwatch sw = Stopwatch.StartNew();

        int totalGeneratedEvents = 0;
        List<DoseEvent> allEvents = [];

        foreach (var (course, schedule) in dataset)
        {
            var events = DoseEventMaterializer.Materialize(course, schedule, profile, utcNow, horizon);
            totalGeneratedEvents += events.Count;
            allEvents.AddRange(events);
        }

        sw.Stop();
        long allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
        long totalAllocated = allocatedAfter - allocatedBefore;

        // Валидация производительности и памяти
        sw.ElapsedMilliseconds.Should().BeLessThan(1000, "генерация 55 курсов на 30 дней вперед обязана занимать < 1 секунды");
        totalAllocated.Should().BeLessThan(25 * 1024 * 1024, "аллокации должны быть строго ограничены (< 25 МБ)");
        totalGeneratedEvents.Should().BeGreaterThan(2000, "должно быть сгенерировано несколько тысяч приёмов");

        // Валидация идемпотентности: повторный запуск на тех же входных данных даёт точно такие же DedupeKey
        foreach (var (course, schedule) in dataset.Take(10))
        {
            var firstRun = DoseEventMaterializer.Materialize(course, schedule, profile, utcNow, horizon);
            var secondRun = DoseEventMaterializer.Materialize(course, schedule, profile, utcNow, horizon);

            secondRun.Count.Should().Be(firstRun.Count);
            secondRun.Select(e => e.DedupeKey).Should().Equal(firstRun.Select(e => e.DedupeKey));
        }

        // Валидация хронологического порядка и уникальности
        foreach (var group in allEvents.GroupBy(e => e.ScheduleId))
        {
            group.Should().BeInAscendingOrder(e => e.ScheduledAt);
            group.Select(e => e.DedupeKey).Should().OnlyHaveUniqueItems();
        }
    }
}
