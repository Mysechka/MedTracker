using Med.Application.Abstractions;
using Med.Domain.Abstractions;
using Med.Domain.Entities;
using Med.Domain.Enums;
using Med.Domain.Scheduling;

namespace Med.Application.UseCases;

/// <summary>
/// Материализует dose_events на горизонт вперёд для активных курсов.
/// Идемпотентность — через dedupe_key при UpsertMany.
/// </summary>
public sealed class MaterializeUpcomingDosesUseCase(
    ICourseRepository courses,
    IScheduleRepository schedules,
    IProfileRepository profiles,
    IDoseEventRepository doseEvents,
    ISystemClock clock)
{
    public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        Profile profile = await profiles.GetCurrentAsync(cancellationToken)
            ?? throw new InvalidOperationException("Профиль не найден.");

        DateTimeOffset utcNow = clock.UtcNow;
        IReadOnlyList<Course> allCourses = await courses.ListAsync(cancellationToken);
        List<DoseEvent> toUpsert = [];

        foreach (Course course in allCourses.Where(c => c.IsActive))
        {
            IReadOnlyList<Schedule> courseSchedules = await schedules.ListByCourseAsync(
                course.Id,
                cancellationToken);

            foreach (Schedule schedule in courseSchedules.Where(s => s.Type != ScheduleType.AsNeeded))
            {
                IReadOnlyList<DoseEvent> materialized = DoseEventMaterializer.Materialize(
                    course,
                    schedule,
                    profile,
                    utcNow);

                toUpsert.AddRange(materialized);
            }
        }

        if (toUpsert.Count == 0)
        {
            return 0;
        }

        await doseEvents.UpsertManyAsync(toUpsert, cancellationToken);
        return toUpsert.Count;
    }
}
