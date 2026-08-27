using FluentAssertions;
using Med.Application.Abstractions;
using Med.Application.DependencyInjection;
using Med.Infrastructure.DependencyInjection;
using Med.Presentation.DependencyInjection;
using Med.Presentation.Shell;
using Med.Presentation.Today;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Med.Presentation.Tests.Composition;

public sealed class PresentationCompositionTests
{
    [Fact]
    public void Shell_и_экраны_резолвятся()
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
            .AddMedPresentation()
            .BuildServiceProvider(validateScopes: true);

        provider.GetRequiredService<ShellViewModel>().Should().NotBeNull();
        provider.GetRequiredService<TodayViewModel>().Should().NotBeNull();
        provider.GetRequiredService<ITickInvoker>().Should().NotBeNull();
    }
}
