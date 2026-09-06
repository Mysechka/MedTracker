using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Med.Presentation.Messaging;

/// <summary>
/// Сообщение об удалении сущности T по её идентификатору.
/// </summary>
public sealed class EntityDeletedMessage<T> : ValueChangedMessage<Guid>
{
    public ChangeSource Source { get; }

    public EntityDeletedMessage(Guid entityId, ChangeSource source = ChangeSource.Local)
        : base(entityId)
    {
        Source = source;
    }
}
