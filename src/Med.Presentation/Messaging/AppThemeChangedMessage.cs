using CommunityToolkit.Mvvm.Messaging.Messages;
using Med.Presentation.Settings;

namespace Med.Presentation.Messaging;

/// <summary>Сообщение об изменении активной темы оформления.</summary>
public sealed class AppThemeChangedMessage(AppThemeInfo theme) : ValueChangedMessage<AppThemeInfo>(theme);
