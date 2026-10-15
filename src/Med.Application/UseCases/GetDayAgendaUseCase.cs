using Med.Application.Abstractions;
using Med.Application.Agenda;
using Med.Domain.Abstractions;
using Med.Domain.Entities;
using Med.Domain.Scheduling;

namespace Med.Application.UseCases;

/// <summary>
/// Собирает расписание локального дня: dose_events плюс название лекарства и дозу.
/// Проекция живёт здесь, а не в ViewModel: экрану нельзя знать про репозитории,
/// а связывание событий с курсами и лекарствами — не работа представления.
/// </summary>
public sealed class GetDayAgendaUseCase(
    IDoseEventRepository doseEvents,
    IScheduleRepository schedules,
    ICourseRepository courses,
    IMedicationRepository medications,
    IProfileRepository profiles,
    ISystemClock clock)
{
    /// <summary>День и часовой пояс берутся из профиля пользователя.</summary>
    public async Task<DayAgenda> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        Profile? profile = await profiles.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        TimeZoneInfo timeZone = profile?.ResolveTimeZone() ?? TimeZoneInfo.Utc;
        DateOnly localDate = LocalTimeConverter.ToLocalDate(clock.UtcNow, timeZone);

        return await ExecuteAsync(localDate, timeZone, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DayAgenda> ExecuteAsync(
        DateOnly localDate,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        IReadOnlyList<DoseEvent> events = await doseEvents.ListForLocalDateAsync(localDate, cancellationToken).ConfigureAwait(false);
        if (events.Count == 0)
        {
            return new DayAgenda(localDate, timeZone.Id, []);
        }

        Task<IReadOnlyList<Course>> coursesTask = courses.ListAsync(cancellationToken);
        Task<IReadOnlyList<Medication>> medicationsTask = medications.ListAsync(cancellationToken);
        Task<Dictionary<Guid, Schedule>> schedulesTask = LoadSchedulesAsync(events, cancellationToken);

        await Task.WhenAll(coursesTask, medicationsTask, schedulesTask).ConfigureAwait(false);

        Dictionary<Guid, Course> courseById = (await coursesTask).ToDictionary(static c => c.Id);
        Dictionary<Guid, Medication> medicationById = (await medicationsTask).ToDictionary(static m => m.Id);
        Dictionary<Guid, Schedule> scheduleById = await schedulesTask;

        List<DoseAgendaItem> items = new(events.Count);
        foreach (DoseEvent doseEvent in events)
        {
            Medication? medication = courseById.TryGetValue(doseEvent.CourseId, out Course? course)
                && medicationById.TryGetValue(course.MedicationId, out Medication? med)
                ? med
                : null;
            scheduleById.TryGetValue(doseEvent.ScheduleId, out Schedule? schedule);

            items.Add(new DoseAgendaItem(
                doseEvent.Id,
                doseEvent.CourseId,
                doseEvent.ScheduleId,
                doseEvent.ScheduledAt,
                LocalTimeConverter.ToLocalTime(doseEvent.ScheduledAt, timeZone),
                doseEvent.State,
                doseEvent.Source,
                medication?.Name,
                medication?.Form,
                medication?.Dosage,
                medication?.Unit,
                schedule?.DoseAmount,
                medication?.Id,
                doseEvent.TakenAt));
        }

        items.Sort(static (a, b) => a.ScheduledAt.CompareTo(b.ScheduledAt));
        return new DayAgenda(localDate, timeZone.Id, items);
    }

    private async Task<Dictionary<Guid, Schedule>> LoadSchedulesAsync(
        IReadOnlyList<DoseEvent> events,
        CancellationToken cancellationToken)
    {
        HashSet<Guid> courseIds = new(events.Count);
        for (int i = 0; i < events.Count; i++)
        {
            courseIds.Add(events[i].CourseId);
        }

        Dictionary<Guid, Schedule> scheduleById = [];
        foreach (Guid courseId in courseIds)
        {
            foreach (Schedule schedule in await schedules.ListByCourseAsync(courseId, cancellationToken).ConfigureAwait(false))
            {
                scheduleById[schedule.Id] = schedule;
            }
        }

        return scheduleById;
    }
}
