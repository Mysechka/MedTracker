using Med.Presentation.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace Med.Presentation.DependencyInjection;

public static class PresentationServiceCollectionExtensions
{
    public static IServiceCollection AddMedPresentation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<ShellViewModel>();

        return services;
    }
}
