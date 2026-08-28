namespace Med.Presentation.Abstractions;

/// <summary>
/// Перенос продолжения в поток UI. Нужен потому, что Realtime и события Auth приходят
/// из фоновых потоков, а привязанные коллекции и свойства менять оттуда нельзя.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>Выполнить действие в потоке UI. Вызов не блокирует текущий поток.</summary>
    void Post(Action action);
}
