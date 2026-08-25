using FluentAssertions;
using Med.Application.Abstractions;
using Med.Application.DependencyInjection;
using Med.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Med.Infrastructure.Tests.Integration;

/// <summary>
/// Живые проверки против Supabase. Запуск:
/// MEDTRACKER_INTEGRATION=1
/// MEDTRACKER_Supabase__Url=...
/// MEDTRACKER_Supabase__AnonKey=...
/// (опционально email/password тестового пользователя)
/// </summary>
public sealed class SupabaseIntegrationTests
{
    private static bool IntegrationEnabled =>
        string.Equals(
            Environment.GetEnvironmentVariable("MEDTRACKER_INTEGRATION"),
            "1",
            StringComparison.Ordinal);

    private static string? Url =>
        Environment.GetEnvironmentVariable("MEDTRACKER_Supabase__Url")
        ?? Environment.GetEnvironmentVariable("SUPABASE_URL");

    private static string? AnonKey =>
        Environment.GetEnvironmentVariable("MEDTRACKER_Supabase__AnonKey")
        ?? Environment.GetEnvironmentVariable("SUPABASE_ANON_KEY");

    [Fact]
    public async Task Клиент_инициализируется_и_Auth_доступен()
    {
        if (!IntegrationEnabled || string.IsNullOrWhiteSpace(Url) || string.IsNullOrWhiteSpace(AnonKey))
        {
            Assert.Skip("Integration выключен: задайте MEDTRACKER_INTEGRATION=1 и URL/AnonKey.");
        }

        CancellationToken ct = TestContext.Current.CancellationToken;

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Supabase:Url"] = Url,
                ["Supabase:AnonKey"] = AnonKey,
                ["Supabase:SignedUrlTtlSeconds"] = "300",
            })
            .Build();

        await using ServiceProvider provider = new ServiceCollection()
            .AddMedApplication()
            .AddMedInfrastructure(configuration)
            .BuildServiceProvider(validateScopes: true);

        IAuthService auth = provider.GetRequiredService<IAuthService>();
        auth.CurrentSession.Should().BeNull();

        string? email = Environment.GetEnvironmentVariable("MEDTRACKER_TEST_EMAIL");
        string? password = Environment.GetEnvironmentVariable("MEDTRACKER_TEST_PASSWORD");
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            Assert.Skip("Нет MEDTRACKER_TEST_EMAIL/PASSWORD — пропуск sign-in.");
        }

        AuthSession session = await auth.SignInWithPasswordAsync(email, password, ct);
        session.UserId.Should().NotBeEmpty();
        auth.CurrentUserId.Should().Be(session.UserId);

        IProfileRepository profiles = provider.GetRequiredService<IProfileRepository>();
        Domain.Entities.Profile? profile = await profiles.GetCurrentAsync(ct);
        profile.Should().NotBeNull();
        profile!.UserId.Should().Be(session.UserId);

        await auth.SignOutAsync(ct);
        auth.CurrentSession.Should().BeNull();
    }

    [Fact]
    public async Task MagicLink_не_падает_на_валидном_email_формате()
    {
        if (!IntegrationEnabled || string.IsNullOrWhiteSpace(Url) || string.IsNullOrWhiteSpace(AnonKey))
        {
            Assert.Skip("Integration выключен.");
        }

        CancellationToken ct = TestContext.Current.CancellationToken;

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Supabase:Url"] = Url,
                ["Supabase:AnonKey"] = AnonKey,
                ["Supabase:SignedUrlTtlSeconds"] = "300",
            })
            .Build();

        await using ServiceProvider provider = new ServiceCollection()
            .AddMedApplication()
            .AddMedInfrastructure(configuration)
            .BuildServiceProvider(validateScopes: true);

        IAuthService auth = provider.GetRequiredService<IAuthService>();
        try
        {
            await auth.SendMagicLinkAsync("integration-test@example.com", ct);
        }
        catch (Exception ex)
        {
            ex.Should().NotBeOfType<NullReferenceException>();
            ex.Message.Should().NotBeNullOrWhiteSpace();
        }
    }
}
