using Med.Domain.Abstractions;
using Med.Infrastructure.Configuration;
using Med.Infrastructure.Time;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Med.Infrastructure.DependencyInjection;

/// <summary>
/// Единственная точка, где интерфейсы прикладного слоя связываются с реальными
/// реализациями: Supabase, HttpClient, системное время.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddMedInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<SupabaseOptions>()
            .Bind(configuration.GetSection(SupabaseOptions.SectionName))
            .ValidateDataAnnotations();

        services.AddSingleton<IValidateOptions<SupabaseOptions>, SupabaseOptionsValidator>();

        services.TryAddSingletonTimeProvider();
        services.AddSingleton<ISystemClock, SystemClock>();

        // Репозитории Supabase, Storage, Realtime и клиенты мессенджеров — стадия 3.
        return services;
    }

    private static void TryAddSingletonTimeProvider(this IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(TimeProvider)))
        {
            return;
        }

        services.AddSingleton(TimeProvider.System);
    }
}
