using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Med.Presentation;
using Med.Presentation.Shell;
using Med.Ui.Views;

namespace Med.Ui;

/// <summary>
/// Навигация каркаса: простейший switch по типу ViewModel, как требует спецификация.
/// Никакой рефлексии по именам типов.
/// </summary>
public sealed class ViewLocator : IDataTemplate
{
    public Control? Build(object? param) => param switch
    {
        ShellViewModel => new MainView(),
        null => null,
        _ => new TextBlock { Text = $"Нет View для {param.GetType().Name}" },
    };

    public bool Match(object? data) => data is ViewModelBase;
}
