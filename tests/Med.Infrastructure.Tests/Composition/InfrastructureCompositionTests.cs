using FluentAssertions;
using Med.Application.Abstractions;
using Med.Application.DependencyInjection;
using Med.Application.UseCases;
using Med.Infrastructure.Configuration;
using Med.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Med.Infrastructure.Tests.Composition;

public sealed class InfrastructureCompositionTests
{
    [Fact]
    public void Все_инфраструктурные_сервисы_резолвятся()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Supabase:Url"] = "https://project-ref.supabase.co",
                ["Supabase:AnonKey"] = "eyJhbGciOiJIUzI1NiJ9.eyJyb2xlIjoiYW5vbiJ9.signature",
                ["Supabase:SignedUrlTtlSeconds"] = "300",
            })
            .Build();

        using ServiceProvider provider = new ServiceCollection()
            .AddMedApplication()
            .AddMedInfrastructure(configuration)
            .BuildServiceProvider(validateScopes: true);

        provider.GetRequiredService<IAuthService>().Should().NotBeNull();
        provider.GetRequiredService<IMedicationRepository>().Should().NotBeNull();
        provider.GetRequiredService<ICourseRepository>().Should().NotBeNull();
        provider.GetRequiredService<IScheduleRepository>().Should().NotBeNull();
        provider.GetRequiredService<IDoseEventRepository>().Should().NotBeNull();
        provider.GetRequiredService<IInventoryRepository>().Should().NotBeNull();
        provider.GetRequiredService<IInventoryTransactionRepository>().Should().NotBeNull();
        provider.GetRequiredService<IDiagnosisRepository>().Should().NotBeNull();
        provider.GetRequiredService<IDocumentRepository>().Should().NotBeNull();
        provider.GetRequiredService<IMessengerLinkRepository>().Should().NotBeNull();
        provider.GetRequiredService<INotificationDeliveryRepository>().Should().NotBeNull();
        provider.GetRequiredService<IProfileRepository>().Should().NotBeNull();
        provider.GetRequiredService<IDoseTransitionService>().Should().NotBeNull();
        provider.GetRequiredService<IInventoryCommandService>().Should().NotBeNull();
        provider.GetRequiredService<IFileStorage>().Should().NotBeNull();
        provider.GetRequiredService<IDoseEventRealtime>().Should().NotBeNull();
        provider.GetRequiredService<MaterializeUpcomingDosesUseCase>().Should().NotBeNull();
        provider.GetRequiredService<ConfirmDoseUseCase>().Should().NotBeNull();
        provider.GetRequiredService<IOptions<SupabaseOptions>>().Value.SignedUrlTtlSeconds.Should().Be(300);
    }
}
