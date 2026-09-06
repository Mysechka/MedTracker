using CommunityToolkit.Mvvm.Messaging;
using Med.Application.Abstractions;
using Med.Domain.Entities;
using Med.Presentation.Messaging;

namespace Med.Presentation.Sync;

/// <summary>
/// Мост между IEntityRealtimeSync (инфраструктурный слой Realtime) и In-App Messenger (CommunityToolkit.Mvvm).
/// Фильтрует эхо-события через EntityChangeDeduplicator и транслирует внешние изменения
/// в типизированные сообщения для ViewModels.
/// </summary>
public sealed class RealtimeStateSynchronizer : IDisposable
{
    private readonly IEntityRealtimeSync _realtime;
    private readonly EntityChangeDeduplicator _deduplicator;
    private readonly IMessenger _messenger;

    public RealtimeStateSynchronizer(
        IEntityRealtimeSync realtime,
        EntityChangeDeduplicator deduplicator,
        IMessenger? messenger = null)
    {
        _realtime = realtime;
        _deduplicator = deduplicator;
        _messenger = messenger ?? WeakReferenceMessenger.Default;

        _realtime.EntityChanged += OnEntityChanged;
    }

    public Task StartAsync(CancellationToken cancellationToken = default) =>
        _realtime.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken = default) =>
        _realtime.StopAsync(cancellationToken);

    public void Dispose()
    {
        _realtime.EntityChanged -= OnEntityChanged;
    }

    private void OnEntityChanged(object? sender, EntityChangeNotification notification)
    {
        Type entityType = ResolveEntityType(notification.TableName);

        // Проверяем дедупликатор: если изменение было порождено этим же клиентом доли секунды назад,
        // игнорируем его, чтобы не вызывать паразитных ререндеров и race conditions.
        if (_deduplicator.IsDuplicateOrRecentLocal(entityType, notification.EntityId))
        {
            return;
        }

        switch (notification.TableName)
        {
            case "dose_events":
                if (notification.Payload is DoseEventChange doseChange)
                {
                    _messenger.Send(new DoseEventStatusChangedMessage(doseChange, ChangeSource.Realtime));
                }
                break;

            case "medications":
                if (notification.ChangeType == EntityChangeType.Delete)
                {
                    _messenger.Send(new EntityDeletedMessage<Medication>(notification.EntityId, ChangeSource.Realtime));
                }
                else if (notification.Payload is Medication medication)
                {
                    _messenger.Send(new EntitySavedMessage<Medication>(medication, ChangeSource.Realtime));
                }
                break;

            case "courses":
                if (notification.ChangeType == EntityChangeType.Delete)
                {
                    _messenger.Send(new EntityDeletedMessage<Course>(notification.EntityId, ChangeSource.Realtime));
                    _messenger.Send(new ScheduleUpdatedMessage(notification.EntityId, Guid.Empty, ChangeSource.Realtime));
                }
                else if (notification.Payload is Course course)
                {
                    _messenger.Send(new EntitySavedMessage<Course>(course, ChangeSource.Realtime));
                    _messenger.Send(new ScheduleUpdatedMessage(course.Id, course.MedicationId, ChangeSource.Realtime));
                }
                break;

            case "schedules":
                if (notification.ChangeType == EntityChangeType.Delete)
                {
                    _messenger.Send(new EntityDeletedMessage<Schedule>(notification.EntityId, ChangeSource.Realtime));
                    _messenger.Send(new ScheduleUpdatedMessage(Guid.Empty, Guid.Empty, ChangeSource.Realtime));
                }
                else if (notification.Payload is Schedule schedule)
                {
                    _messenger.Send(new EntitySavedMessage<Schedule>(schedule, ChangeSource.Realtime));
                    _messenger.Send(new ScheduleUpdatedMessage(schedule.CourseId, Guid.Empty, ChangeSource.Realtime));
                }
                else
                {
                    _messenger.Send(new ScheduleUpdatedMessage(Guid.Empty, Guid.Empty, ChangeSource.Realtime));
                }
                break;
        }
    }

    private static Type ResolveEntityType(string tableName) => tableName switch
    {
        "medications" => typeof(Medication),
        "courses" => typeof(Course),
        "schedules" => typeof(Schedule),
        "dose_events" => typeof(DoseEvent),
        _ => typeof(object),
    };
}
