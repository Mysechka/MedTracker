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

        // Use-cases появятся на стадии 1+. Метод существует уже сейчас, чтобы
        // head-проекты собирали контейнер одинаково и не менялись позже.
        return services;
    }
}
