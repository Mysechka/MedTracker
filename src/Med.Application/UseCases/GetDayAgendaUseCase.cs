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
        Profile? profile = await profiles.GetCurrentAsync(cancellationToken);
        TimeZoneInfo timeZone = profile?.ResolveTimeZone() ?? TimeZoneInfo.Utc;
        DateOnly localDate = LocalTimeConverter.ToLocalDate(clock.UtcNow, timeZone);

        return await ExecuteAsync(localDate, timeZone, cancellationToken);
    }

    public async Task<DayAgenda> ExecuteAsync(
        DateOnly localDate,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        IReadOnlyList<DoseEvent> events = await doseEvents.ListForLocalDateAsync(localDate, cancellationToken);
        if (events.Count == 0)
        {
            return new DayAgenda(localDate, timeZone.Id, []);
        }

        Dictionary<Guid, Course> courseById = (await courses.ListAsync(cancellationToken))
            .ToDictionary(static course => course.Id);
        Dictionary<Guid, Medication> medicationById = (await medications.ListAsync(cancellationToken))
            .ToDictionary(static medication => medication.Id);
        Dictionary<Guid, Schedule> scheduleById = await LoadSchedulesAsync(events, cancellationToken);

        List<DoseAgendaItem> items = new(events.Count);
        foreach (DoseEvent doseEvent in events.OrderBy(static e => e.ScheduledAt))
        {
            Medication? medication = ResolveMedication(doseEvent, courseById, medicationById);
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
                schedule?.DoseAmount));
        }

        return new DayAgenda(localDate, timeZone.Id, items);
    }

    private async Task<Dictionary<Guid, Schedule>> LoadSchedulesAsync(
        IReadOnlyList<DoseEvent> events,
        CancellationToken cancellationToken)
    {
        // Один запрос на курс, а не на событие: у курса обычно несколько приёмов в день.
        Dictionary<Guid, Schedule> scheduleById = [];
        foreach (Guid courseId in events.Select(static e => e.CourseId).Distinct())
        {
            foreach (Schedule schedule in await schedules.ListByCourseAsync(courseId, cancellationToken))
            {
                scheduleById[schedule.Id] = schedule;
            }
        }

        return scheduleById;
    }

    private static Medication? ResolveMedication(
        DoseEvent doseEvent,
        Dictionary<Guid, Course> courseById,
        Dictionary<Guid, Medication> medicationById)
    {
        if (!courseById.TryGetValue(doseEvent.CourseId, out Course? course))
        {
            return null;
        }

        return medicationById.TryGetValue(course.MedicationId, out Medication? medication)
            ? medication
            : null;
    }
}
