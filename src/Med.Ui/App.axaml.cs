using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Med.Presentation.Shell;
using Med.Ui.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Med.Ui;

/// <summary>
/// Точка входа Avalonia. Здесь нет ни одного доменного правила: только выбор
/// корневого представления под lifetime платформы и получение ViewModel из контейнера.
/// </summary>
public sealed partial class App : Avalonia.Application
{
    private static IServiceProvider? _services;

    /// <summary>
    /// Контейнер собирается в head-проекте и передаётся до старта Avalonia.
    /// Экземпляр <see cref="App"/> на Android создаёт сам Android-lifecycle,
    /// поэтому передать провайдер конструктором невозможно.
    /// </summary>
    public static void UseServices(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        IServiceProvider services = _services
            ?? throw new InvalidOperationException(
                $"{nameof(UseServices)} не вызван до старта Avalonia: head-проект обязан собрать контейнер.");

        ShellViewModel shell = services.GetRequiredService<ShellViewModel>();

        switch (ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop:
                desktop.MainWindow = new MainWindow { DataContext = shell };
                break;

            case IActivityApplicationLifetime activity:
                activity.MainViewFactory = () => new MainView { DataContext = shell };
                break;

            case ISingleViewApplicationLifetime singleView:
                singleView.MainView = new MainView { DataContext = shell };
                break;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
