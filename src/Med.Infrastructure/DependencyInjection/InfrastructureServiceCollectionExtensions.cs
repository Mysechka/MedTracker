using Med.Application.Abstractions;
using Med.Domain.Abstractions;
using Med.Infrastructure.Configuration;
using Med.Infrastructure.Notifications;
using Med.Infrastructure.Repositories;
using Med.Infrastructure.Supabase;
using Med.Infrastructure.Supabase.Auth;
using Med.Infrastructure.Supabase.Realtime;
using Med.Infrastructure.Supabase.Services;
using Med.Infrastructure.Supabase.Storage;
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

        services.AddSingleton<ISupabaseClientAccessor, SupabaseClientAccessor>();
        services.AddSingleton<IAuthService, SupabaseAuthService>();

        services.AddSingleton<IProfileRepository, ProfileRepository>();
        services.AddSingleton<IMedicationRepository, MedicationRepository>();
        services.AddSingleton<ICourseRepository, CourseRepository>();
        services.AddSingleton<IScheduleRepository, ScheduleRepository>();
        services.AddSingleton<IDoseEventRepository, DoseEventRepository>();
        services.AddSingleton<IInventoryRepository, InventoryRepository>();
        services.AddSingleton<IInventoryTransactionRepository, InventoryTransactionRepository>();
        services.AddSingleton<IDiagnosisRepository, DiagnosisRepository>();
        services.AddSingleton<IDocumentRepository, DocumentRepository>();
        services.AddSingleton<IMessengerLinkRepository, MessengerLinkRepository>();
        services.AddSingleton<INotificationDeliveryRepository, NotificationDeliveryRepository>();

        services.AddSingleton<IDoseTransitionService, DoseTransitionService>();
        services.AddSingleton<IInventoryCommandService, InventoryCommandService>();
        services.AddSingleton<IDoseEventMaterializer, DoseEventMaterializerService>();
        services.AddSingleton<IFileStorage, SupabaseFileStorage>();
        services.AddSingleton<IEntityRealtimeSync, SupabaseEntityRealtimeSync>();
        services.AddSingleton<IDoseEventRealtime, SupabaseDoseEventRealtime>();
        services.AddSingleton<INotificationService, LocalNotificationService>();
        services.AddHttpClient(nameof(TickInvoker));
        services.AddSingleton<ITickInvoker, TickInvoker>();

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
