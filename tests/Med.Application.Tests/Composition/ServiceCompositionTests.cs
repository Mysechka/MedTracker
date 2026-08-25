using FluentAssertions;
using Med.Application.Abstractions;
using Med.Application.DependencyInjection;
using Med.Application.UseCases;
using Med.Domain.Abstractions;
using Med.Domain.Tests.Fakes;
using Med.Infrastructure.Configuration;
using Med.Infrastructure.DependencyInjection;
using Med.Presentation.DependencyInjection;
using Med.Presentation.Shell;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Med.Application.Tests.Composition;

public sealed class ServiceCompositionTests
{
    private const string AnonKeyPayload =
        "eyJhbGciOiJIUzI1NiJ9.eyJyb2xlIjoiYW5vbiJ9.signature";

    private const string ServiceRoleKeyPayload =
        "eyJhbGciOiJIUzI1NiJ9.eyJyb2xlIjoic2VydmljZV9yb2xlIn0.signature";

    [Fact]
    public void Контейнер_собирается_и_отдаёт_ShellViewModel()
    {
        using ServiceProvider provider = BuildProvider(AnonKeyPayload);

        ShellViewModel shell = provider.GetRequiredService<ShellViewModel>();

        shell.Title.Should().NotBeEmpty();
        provider.GetRequiredService<ISystemClock>().Should().NotBeNull();
        provider.GetRequiredService<IAuthService>().Should().NotBeNull();
        provider.GetRequiredService<IMedicationRepository>().Should().NotBeNull();
        provider.GetRequiredService<MaterializeUpcomingDosesUseCase>().Should().NotBeNull();
    }

    [Fact]
    public void Ключ_service_role_в_клиенте_валит_конфигурацию()
    {
        using ServiceProvider provider = BuildProvider(ServiceRoleKeyPayload);

        Action resolve = () => _ = provider.GetRequiredService<IOptions<SupabaseOptions>>().Value;

        resolve.Should().Throw<OptionsValidationException>()
            .WithMessage("*service_role*");
    }

    [Fact]
    public void Пустой_anon_key_валит_конфигурацию()
    {
        using ServiceProvider provider = BuildProvider(anonKey: string.Empty);

        Action resolve = () => _ = provider.GetRequiredService<IOptions<SupabaseOptions>>().Value;

        resolve.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void ViewModel_берёт_время_только_из_ISystemClock()
    {
        ShellViewModel shell = new(FakeClock.At("2026-08-24T09:05:00Z"));

        shell.ReadClockCommand.Execute(null);

        shell.ClockReading.Should().Be("2026-08-24 09:05:00Z");
    }

    private static ServiceProvider BuildProvider(string anonKey)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Supabase:Url"] = "https://project-ref.supabase.co",
                ["Supabase:AnonKey"] = anonKey,
                ["Supabase:SignedUrlTtlSeconds"] = "300",
            })
            .Build();

        return new ServiceCollection()
            .AddMedApplication()
            .AddMedInfrastructure(configuration)
            .AddMedPresentation()
            .BuildServiceProvider(validateScopes: true);
    }
}
