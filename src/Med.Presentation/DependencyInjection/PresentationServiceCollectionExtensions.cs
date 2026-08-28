using Med.Presentation.Abstractions;
using Med.Presentation.Courses;
using Med.Presentation.Diagnostics;
using Med.Presentation.MedicalCard;
using Med.Presentation.Medications;
using Med.Presentation.Settings;
using Med.Presentation.Shell;
using Med.Presentation.Today;
using Microsoft.Extensions.DependencyInjection;

namespace Med.Presentation.DependencyInjection;

public static class PresentationServiceCollectionExtensions
{
    public static IServiceCollection AddMedPresentation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Head-проект с Avalonia перекрывает диспетчер вызовом AddMedUi().
        services.AddSingleton<IUiDispatcher, ImmediateUiDispatcher>();

        services.AddSingleton<AuthViewModel>();
        services.AddSingleton<TodayViewModel>();
        services.AddSingleton<MedicationsViewModel>();
        services.AddSingleton<CoursesViewModel>();
        services.AddSingleton<MedicalCardViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<DiagnosticsViewModel>();
        services.AddSingleton<ShellViewModel>();

        return services;
    }
}
