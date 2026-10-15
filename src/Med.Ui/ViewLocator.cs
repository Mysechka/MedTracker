using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Med.Presentation;
using Med.Presentation.Account;
using Med.Presentation.Courses;
using Med.Presentation.Diagnostics;
using Med.Presentation.MedicalCard;
using Med.Presentation.Medications;
using Med.Presentation.Settings;
using Med.Presentation.Shell;
using Med.Presentation.Today;
using Med.Ui.Views;
using Med.Ui.Views.Screens;

namespace Med.Ui;

/// <summary>
/// Навигация каркаса: кэширует созданные View по экземпляру ViewModel,
/// чтобы переключение вкладок не пересоздавало визуальное дерево заново.
/// </summary>
public sealed class ViewLocator : IDataTemplate
{
    private readonly ConditionalWeakTable<object, Control> _viewCache = new();

    public Control? Build(object? param)
    {
        if (param is null)
        {
            return null;
        }

        Control view = _viewCache.GetValue(param, static vm => vm switch
        {
            ShellViewModel => new MainView(),
            AuthViewModel => new AuthView(),
            AccountViewModel => new AccountView(),
            TodayViewModel => new TodayView(),
            MedicationsViewModel => new MedicationsView(),
            CoursesViewModel => new CoursesView(),
            MedicalCardViewModel => new MedicalCardView(),
            SettingsViewModel => new SettingsView(),
            DiagnosticsViewModel => new DiagnosticsView(),
            _ => new TextBlock { Text = $"Нет View для {vm.GetType().Name}" },
        });

        view.DataContext = param;
        return view;
    }

    public bool Match(object? data) => data is ViewModelBase;
}
