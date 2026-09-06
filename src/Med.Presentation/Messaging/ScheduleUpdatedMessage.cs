using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Med.Presentation.Messaging;

public sealed record ScheduleUpdatePayload(Guid CourseId, Guid MedicationId);

/// <summary>
/// Сообщение об изменении курсов/расписаний приёма (требует рематериализации или обновления тегов).
/// </summary>
public sealed class ScheduleUpdatedMessage : ValueChangedMessage<ScheduleUpdatePayload>
{
    public ChangeSource Source { get; }

    public ScheduleUpdatedMessage(ScheduleUpdatePayload payload, ChangeSource source = ChangeSource.Local)
        : base(payload)
    {
        Source = source;
    }

    public ScheduleUpdatedMessage(Guid courseId, Guid medicationId, ChangeSource source = ChangeSource.Local)
        : this(new ScheduleUpdatePayload(courseId, medicationId), source)
    {
    }
}
