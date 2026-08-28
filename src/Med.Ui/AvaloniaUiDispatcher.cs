using Avalonia.Threading;
using Med.Presentation.Abstractions;

namespace Med.Ui;

/// <summary>
/// Единственное место, где UI-слой отдаёт что-то наверх: перенос продолжения
/// в поток Avalonia. Доменных правил здесь нет.
/// </summary>
public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Post(action);
    }
}
