using CommunityToolkit.Mvvm.Messaging.Messages;
using Med.Application.Abstractions;

namespace Med.Presentation.Messaging;

/// <summary>
/// Сообщение об изменении статуса приёма (Taken, Skipped, Snoozed, Missed).
/// </summary>
public sealed class DoseEventStatusChangedMessage : ValueChangedMessage<DoseEventChange>
{
    public ChangeSource Source { get; }

    public DoseEventStatusChangedMessage(DoseEventChange change, ChangeSource source = ChangeSource.Local)
        : base(change)
    {
        Source = source;
    }
}
