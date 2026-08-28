namespace Med.Presentation.Abstractions;

/// <summary>
/// Реализация по умолчанию: выполняет действие на месте. Подходит для тестов и для
/// хостов без UI-потока. Head-проекты с Avalonia перекрывают её своей регистрацией.
/// </summary>
public sealed class ImmediateUiDispatcher : IUiDispatcher
{
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        action();
    }
}
