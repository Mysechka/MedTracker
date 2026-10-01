using Med.Presentation.Shell;

namespace Med.Presentation.Messaging;

/// <summary>Сообщение запроса переключения секции навигации в ShellViewModel.</summary>
public sealed record NavigateToSectionMessage(ShellNav Section);
