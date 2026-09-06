using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Med.Presentation.Messaging;

/// <summary>
/// Сообщение о сохранении (создании или обновлении) сущности T.
/// </summary>
public sealed class EntitySavedMessage<T> : ValueChangedMessage<T>
{
    public ChangeSource Source { get; }

    public EntitySavedMessage(T entity, ChangeSource source = ChangeSource.Local)
        : base(entity)
    {
        Source = source;
    }
}
