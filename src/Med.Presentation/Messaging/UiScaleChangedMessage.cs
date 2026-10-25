using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Med.Presentation.Messaging;

/// <summary>Сообщение об изменении масштаба интерфейса (UI Scale).</summary>
public sealed class UiScaleChangedMessage(double scale) : ValueChangedMessage<double>(scale);
