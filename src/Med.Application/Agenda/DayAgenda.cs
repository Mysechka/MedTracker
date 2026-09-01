using Med.Domain.Enums;

namespace Med.Application.Agenda;

/// <summary>
/// Приём дня вместе с данными лекарства и расписания.
/// Поля лекарства и дозы — nullable: строка dose_events может ссылаться на курс,
/// лекарство или расписание, которых уже нет. Формулировку для пользователя
/// выбирает слой представления, здесь — только факт отсутствия данных.
/// </summary>
public sealed record DoseAgendaItem(
    Guid DoseEventId,
    Guid CourseId,
    Guid ScheduleId,
    DateTimeOffset ScheduledAt,
    TimeOnly LocalTime,
    DoseEventState State,
    DoseEventSource? Source,
    string? MedicationName,
    string? MedicationForm,
    string? Dosage,
    string? Unit,
    decimal? DoseAmount);

/// <summary>Расписание одного локального дня. Часовой пояс — явно, а не из окружения.</summary>
public sealed record DayAgenda(
    DateOnly LocalDate,
    string TimeZoneId,
    IReadOnlyList<DoseAgendaItem> Items);
