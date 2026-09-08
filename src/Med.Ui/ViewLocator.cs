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
/// Навигация каркаса: простейший switch по типу ViewModel.
/// </summary>
public sealed class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
        {
            return null;
        }

        Control view = param switch
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
            _ => new TextBlock { Text = $"Нет View для {param.GetType().Name}" },
        };

        view.DataContext = param;
        return view;
    }

    public bool Match(object? data) => data is ViewModelBase;
}
