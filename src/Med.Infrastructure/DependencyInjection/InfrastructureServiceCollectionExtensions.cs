using Med.Application.Abstractions;
using Med.Domain.Abstractions;
using Med.Infrastructure.Configuration;
using Med.Infrastructure.LocalStorage;
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
/// реализациями: Supabase, SQLite, HttpClient, системное время.
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

        // Local SQLite Storage
        services.AddSingleton<LocalDatabase>();
        services.AddSingleton<RepositoryModeProvider>();

        services.AddSingleton<LocalProfileRepository>();
        services.AddSingleton<LocalMedicationRepository>();
        services.AddSingleton<LocalCourseRepository>();
        services.AddSingleton<LocalScheduleRepository>();
        services.AddSingleton<LocalDoseEventRepository>();
        services.AddSingleton<LocalInventoryRepository>();

        // Supabase Infrastructure
        services.AddSingleton<ISupabaseClientAccessor, SupabaseClientAccessor>();
        services.AddSingleton<SupabaseAuthService>();

        services.AddSingleton<ProfileRepository>();
        services.AddSingleton<MedicationRepository>();
        services.AddSingleton<CourseRepository>();
        services.AddSingleton<ScheduleRepository>();
        services.AddSingleton<DoseEventRepository>();
        services.AddSingleton<InventoryRepository>();
        services.AddSingleton<IInventoryTransactionRepository, InventoryTransactionRepository>();
        services.AddSingleton<IDiagnosisRepository, DiagnosisRepository>();
        services.AddSingleton<IDocumentRepository, DocumentRepository>();
        services.AddSingleton<IMessengerLinkRepository, MessengerLinkRepository>();
        services.AddSingleton<INotificationDeliveryRepository, NotificationDeliveryRepository>();

        // Switching Repositories (routed by RepositoryModeProvider)
        services.AddSingleton<SwitchingMedicationRepository>();
        services.AddSingleton<SwitchingCourseRepository>();
        services.AddSingleton<SwitchingScheduleRepository>();
        services.AddSingleton<SwitchingDoseEventRepository>();
        services.AddSingleton<SwitchingInventoryRepository>();
        services.AddSingleton<SwitchingProfileRepository>();

        services.AddSingleton<IMedicationRepository>(sp => sp.GetRequiredService<SwitchingMedicationRepository>());
        services.AddSingleton<ICourseRepository>(sp => sp.GetRequiredService<SwitchingCourseRepository>());
        services.AddSingleton<IScheduleRepository>(sp => sp.GetRequiredService<SwitchingScheduleRepository>());
        services.AddSingleton<IDoseEventRepository>(sp => sp.GetRequiredService<SwitchingDoseEventRepository>());
        services.AddSingleton<IInventoryRepository>(sp => sp.GetRequiredService<SwitchingInventoryRepository>());
        services.AddSingleton<IProfileRepository>(sp => sp.GetRequiredService<SwitchingProfileRepository>());

        // Migration & Auth Services
        services.AddSingleton<DataMigrationService>(sp => new DataMigrationService(
            sp.GetRequiredService<SupabaseAuthService>(),
            sp.GetRequiredService<LocalMedicationRepository>(),
            sp.GetRequiredService<LocalCourseRepository>(),
            sp.GetRequiredService<LocalScheduleRepository>(),
            sp.GetRequiredService<LocalDoseEventRepository>(),
            sp.GetRequiredService<LocalInventoryRepository>(),
            sp.GetRequiredService<LocalProfileRepository>(),
            sp.GetRequiredService<MedicationRepository>(),
            sp.GetRequiredService<CourseRepository>(),
            sp.GetRequiredService<ScheduleRepository>(),
            sp.GetRequiredService<DoseEventRepository>(),
            sp.GetRequiredService<InventoryRepository>(),
            sp.GetRequiredService<ProfileRepository>(),
            sp.GetRequiredService<RepositoryModeProvider>(),
            sp.GetRequiredService<LocalDatabase>()));

        services.AddSingleton<LocalAuthService>(sp => new LocalAuthService(
            sp.GetRequiredService<LocalProfileRepository>(),
            sp.GetRequiredService<RepositoryModeProvider>(),
            sp.GetRequiredService<LocalDatabase>(),
            sp,
            sp.GetRequiredService<SupabaseAuthService>()));

        services.AddSingleton<IAuthService>(sp => sp.GetRequiredService<LocalAuthService>());

        services.AddSingleton<LocalDoseEventMaterializer>(sp => new LocalDoseEventMaterializer(
            sp.GetRequiredService<LocalProfileRepository>(),
            sp.GetRequiredService<LocalCourseRepository>(),
            sp.GetRequiredService<LocalScheduleRepository>(),
            sp.GetRequiredService<LocalDoseEventRepository>(),
            sp.GetRequiredService<ISystemClock>()));

        services.AddSingleton<LocalDoseTransitionService>(sp => new LocalDoseTransitionService(
            sp.GetRequiredService<LocalDoseEventRepository>(),
            sp.GetRequiredService<ISystemClock>()));

        services.AddSingleton<DoseEventMaterializerService>();
        services.AddSingleton<DoseTransitionService>();

        services.AddSingleton<SwitchingDoseEventMaterializer>(sp => new SwitchingDoseEventMaterializer(
            sp.GetRequiredService<LocalDoseEventMaterializer>(),
            sp.GetRequiredService<DoseEventMaterializerService>(),
            sp.GetRequiredService<RepositoryModeProvider>()));

        services.AddSingleton<SwitchingDoseTransitionService>(sp => new SwitchingDoseTransitionService(
            sp.GetRequiredService<LocalDoseTransitionService>(),
            sp.GetRequiredService<DoseTransitionService>(),
            sp.GetRequiredService<RepositoryModeProvider>()));

        services.AddSingleton<IDoseTransitionService>(sp => sp.GetRequiredService<SwitchingDoseTransitionService>());
        services.AddSingleton<IInventoryCommandService, InventoryCommandService>();
        services.AddSingleton<IDoseEventMaterializer>(sp => sp.GetRequiredService<SwitchingDoseEventMaterializer>());
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
