using Med.Application.Abstractions;
using Med.Domain.Abstractions;
using Med.Domain.Entities;
using Med.Domain.Scheduling;

namespace Med.Infrastructure.LocalStorage;

public sealed class LocalDoseEventMaterializer : IDoseEventMaterializer
{
    private readonly IProfileRepository _profiles;
    private readonly ICourseRepository _courses;
    private readonly IScheduleRepository _schedules;
    private readonly LocalDoseEventRepository _doseEvents;
    private readonly ISystemClock _clock;

    public LocalDoseEventMaterializer(
        IProfileRepository profiles,
        ICourseRepository courses,
        IScheduleRepository schedules,
        LocalDoseEventRepository doseEvents,
        ISystemClock? clock = null)
    {
        _profiles = profiles;
        _courses = courses;
        _schedules = schedules;
        _doseEvents = doseEvents;
        _clock = clock ?? new DefaultSystemClock();
    }

    private sealed class DefaultSystemClock : ISystemClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }

    public async Task<int> MaterializeAsync(int horizonDays = 14, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Profile? profile = await _profiles.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return 0;
        }

        var courses = await _courses.ListAsync(cancellationToken).ConfigureAwait(false);
        var allEvents = new List<DoseEvent>();
        TimeSpan horizon = TimeSpan.FromDays(horizonDays);
        DateTimeOffset now = _clock.UtcNow;

        var activeCourses = courses.Where(c => c.IsActive).ToList();
        foreach (var course in activeCourses)
        {
            var schedules = await _schedules.ListByCourseAsync(course.Id, cancellationToken).ConfigureAwait(false);
            foreach (var schedule in schedules)
            {
                var events = DoseEventMaterializer.Materialize(course, schedule, profile, now, horizon);
                allEvents.AddRange(events);
            }
        }

        if (allEvents.Count > 0)
        {
            await _doseEvents.InsertMissingAsync(allEvents, cancellationToken).ConfigureAwait(false);
        }

        if (activeCourses.Count > 0)
        {
            TimeZoneInfo tz = profile.ResolveTimeZone();
            DateOnly today = LocalTimeConverter.ToLocalDate(now, tz);
            var activeCourseIds = activeCourses.Select(c => c.Id).ToList();
            var validKeys = allEvents.Select(e => e.DedupeKey).ToHashSet(StringComparer.Ordinal);
            await _doseEvents.DeleteObsoleteScheduledAsync(activeCourseIds, today, validKeys, cancellationToken).ConfigureAwait(false);
        }

        return allEvents.Count;
    }
}
