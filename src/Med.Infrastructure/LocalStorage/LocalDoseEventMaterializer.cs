using Med.Application.Abstractions;
using Med.Domain.Abstractions;
using Med.Domain.Entities;
using Med.Domain.Scheduling;

namespace Med.Infrastructure.LocalStorage;

public sealed class LocalDoseEventMaterializer(
    IProfileRepository profiles,
    ICourseRepository courses,
    IScheduleRepository schedules,
    LocalDoseEventRepository doseEvents,
    ISystemClock? clock = null) : IDoseEventMaterializer
{
    private readonly ISystemClock _clock = clock ?? DefaultSystemClock.Instance;

    private sealed class DefaultSystemClock : ISystemClock
    {
        public static readonly DefaultSystemClock Instance = new();
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }

    public async Task<int> MaterializeAsync(int horizonDays = 14, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Profile? profile = await profiles.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return 0;
        }

        IReadOnlyList<Course> allCourses = await courses.ListAsync(cancellationToken).ConfigureAwait(false);
        if (allCourses.Count == 0)
        {
            return 0;
        }

        var allEvents = new List<DoseEvent>();
        var activeCourseIds = new List<Guid>(allCourses.Count);
        var validKeys = new HashSet<string>(StringComparer.Ordinal);
        TimeSpan horizon = TimeSpan.FromDays(horizonDays);
        DateTimeOffset now = _clock.UtcNow;

        foreach (Course course in allCourses)
        {
            if (!course.IsActive)
            {
                continue;
            }

            activeCourseIds.Add(course.Id);
            IReadOnlyList<Schedule> courseSchedules = await schedules.ListByCourseAsync(course.Id, cancellationToken).ConfigureAwait(false);
            foreach (Schedule schedule in courseSchedules)
            {
                IReadOnlyList<DoseEvent> events = DoseEventMaterializer.Materialize(course, schedule, profile, now, horizon);
                for (int i = 0; i < events.Count; i++)
                {
                    DoseEvent evt = events[i];
                    allEvents.Add(evt);
                    validKeys.Add(evt.DedupeKey);
                }
            }
        }

        if (allEvents.Count > 0)
        {
            await doseEvents.InsertMissingAsync(allEvents, cancellationToken).ConfigureAwait(false);
        }

        if (activeCourseIds.Count > 0)
        {
            DateOnly today = LocalTimeConverter.ToLocalDate(now, profile.ResolveTimeZone());
            await doseEvents.DeleteObsoleteScheduledAsync(activeCourseIds, today, validKeys, cancellationToken).ConfigureAwait(false);
        }

        return allEvents.Count;
    }
}
