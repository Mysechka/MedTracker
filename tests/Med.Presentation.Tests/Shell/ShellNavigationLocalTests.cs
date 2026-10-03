using FluentAssertions;
using Med.Application.Abstractions;
using Med.Application.DependencyInjection;
using Med.Infrastructure.DependencyInjection;
using Med.Infrastructure.LocalStorage;
using Med.Presentation.DependencyInjection;
using Med.Presentation.Shell;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Med.Presentation.Tests.Shell;

public sealed class ShellNavigationLocalTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IAuthService _authService;
    private readonly LocalDatabase _db;

    public ShellNavigationLocalTests()
    {
        string memConn = $"Data Source=InMemoryNavTest_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _db = new LocalDatabase(memConn);

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Supabase:Url"] = "https://project-ref.supabase.co",
                ["Supabase:AnonKey"] = "eyJhbGciOiJIUzI1NiJ9.eyJyb2xlIjoiYW5vbiJ9.signature",
                ["Supabase:SignedUrlTtlSeconds"] = "300",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddMedApplication();
        services.AddMedInfrastructure(configuration);
        // Override with isolated in-memory db
        services.AddSingleton(_db);
        services.AddMedPresentation();

        _provider = services.BuildServiceProvider();
        _authService = _provider.GetRequiredService<IAuthService>();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _db.Dispose();
    }

    [Fact]
    public void NoSession_ShowsOnboarding()
    {
        // When there is no active session on startup, ShellViewModel redirects to Onboarding/Auth
        ShellViewModel shell = _provider.GetRequiredService<ShellViewModel>();

        shell.IsAuthenticated.Should().BeFalse();
        shell.ActiveNav.Should().Be(ShellNav.Auth);
    }

    [Fact]
    public async Task LocalUser_CanNavigateToMedications()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await _authService.SignUpWithPasswordAsync(string.Empty, string.Empty, "LocalEgor", ct);

        ShellViewModel shell = _provider.GetRequiredService<ShellViewModel>();
        shell.IsAuthenticated.Should().BeTrue();
        shell.IsLocalAccount.Should().BeTrue();

        shell.GoMedicationsCommand.Execute(null);

        shell.ActiveNav.Should().Be(ShellNav.Medications);
        shell.IsMedicationsSelected.Should().BeTrue();
    }

    [Fact]
    public async Task LocalUser_CanNavigateToMedicalCard()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await _authService.SignUpWithPasswordAsync(string.Empty, string.Empty, "LocalEgor", ct);

        ShellViewModel shell = _provider.GetRequiredService<ShellViewModel>();

        shell.GoMedicalCardCommand.Execute(null);

        shell.ActiveNav.Should().Be(ShellNav.MedicalCard);
        shell.IsMedicalCardSelected.Should().BeTrue();
    }

    [Fact]
    public async Task LocalUser_CanNavigateToSettings()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await _authService.SignUpWithPasswordAsync(string.Empty, string.Empty, "LocalEgor", ct);

        ShellViewModel shell = _provider.GetRequiredService<ShellViewModel>();

        shell.GoSettingsCommand.Execute(null);

        shell.ActiveNav.Should().Be(ShellNav.Settings);
        shell.IsSettingsSelected.Should().BeTrue();
    }

    [Fact]
    public async Task LocalUser_CanNavigateToToday()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await _authService.SignUpWithPasswordAsync(string.Empty, string.Empty, "LocalEgor", ct);

        ShellViewModel shell = _provider.GetRequiredService<ShellViewModel>();

        shell.GoMedicationsCommand.Execute(null);
        shell.GoTodayCommand.Execute(null);

        shell.ActiveNav.Should().Be(ShellNav.Today);
        shell.IsTodaySelected.Should().BeTrue();
    }

    [Fact]
    public async Task LocalUser_DisplaysUsernameAndInitialInShell()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await _authService.SignUpWithPasswordAsync(string.Empty, string.Empty, "муся", ct);

        ShellViewModel shell = _provider.GetRequiredService<ShellViewModel>();

        shell.IsAuthenticated.Should().BeTrue();
        shell.IsLocalAccount.Should().BeTrue();
        shell.AccountName.Should().Be("муся");
        shell.AvatarInitial.Should().Be("М");
    }
}
