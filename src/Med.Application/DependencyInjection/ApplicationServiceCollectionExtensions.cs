using Med.Application.UseCases;
using Microsoft.Extensions.DependencyInjection;

namespace Med.Application.DependencyInjection;

/// <summary>
/// Регистрация use-cases прикладного слоя. Реализации инфраструктуры сюда не попадают:
/// прикладной слой знает только интерфейсы.
/// </summary>
public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddMedApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<MaterializeUpcomingDosesUseCase>();
        services.AddTransient<GetDayAgendaUseCase>();
        services.AddTransient<ConfirmDoseUseCase>();
        services.AddTransient<SkipDoseUseCase>();
        services.AddTransient<UndoConfirmDoseUseCase>();
        services.AddTransient<RestockInventoryUseCase>();
        services.AddTransient<UpdateProfileUseCase>();
        services.AddTransient<UploadDocumentUseCase>();

        return services;
    }
}
