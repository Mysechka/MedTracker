using FluentAssertions;
using Med.Application.Abstractions;
using Med.Application.DependencyInjection;
using Med.Infrastructure.DependencyInjection;
using Med.Presentation.DependencyInjection;
using Med.Presentation.Shell;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Med.Presentation.Tests.Shell;

public sealed class ShellNavigationGuardTests
{
    private sealed class TestContext : IDisposable
    {
        public ServiceProvider Provider { get; }
        public ShellViewModel Shell { get; }

        public TestContext(bool authenticated)
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Supabase:Url"] = "https://project-ref.supabase.co",
                    ["Supabase:AnonKey"] = "eyJhbGciOiJIUzI1NiJ9.eyJyb2xlIjoiYW5vbiJ9.signature",
                    ["Supabase:SignedUrlTtlSeconds"] = "300",
                })
                .Build();

            Provider = new ServiceCollection()
                .AddMedApplication()
                .AddMedInfrastructure(configuration)
                .AddMedPresentation()
                .BuildServiceProvider();

            Shell = Provider.GetRequiredService<ShellViewModel>();
            if (authenticated)
            {
                Shell.IsAuthenticated = true;
                Shell.AccountName = "user@example.com";
            }
        }

        public void Dispose() => Provider.Dispose();
    }

    [Fact]
    public void Неавторизованный_пользователь_может_открыть_стартовую_страницу_и_авторизацию()
    {
        using TestContext ctx = new(authenticated: false);
        ShellViewModel shell = ctx.Shell;

        shell.GoTodayCommand.Execute(null);
        shell.ActiveNav.Should().Be(ShellNav.Today);
        shell.IsTodaySelected.Should().BeTrue();

        shell.GoAuthCommand.Execute(null);
        shell.ActiveNav.Should().Be(ShellNav.Auth);
    }

    [Fact]
    public void Неавторизованный_пользователь_блокируется_при_попытке_перехода_в_лекарства()
    {
        using TestContext ctx = new(authenticated: false);
        ShellViewModel shell = ctx.Shell;

        shell.GoTodayCommand.Execute(null);
        shell.GoMedicationsCommand.Execute(null);

        shell.ActiveNav.Should().Be(ShellNav.Auth, "без аккаунта происходит блокировка и редирект на экран авторизации/верификации");
        shell.IsMedicationsSelected.Should().BeFalse();
    }

    [Fact]
    public void Неавторизованный_пользователь_блокируется_при_попытке_перехода_в_медкарту()
    {
        using TestContext ctx = new(authenticated: false);
        ShellViewModel shell = ctx.Shell;

        shell.GoTodayCommand.Execute(null);
        shell.GoMedicalCardCommand.Execute(null);

        shell.ActiveNav.Should().Be(ShellNav.Auth, "без аккаунта происходит блокировка и редирект на экран авторизации/верификации");
        shell.IsMedicalCardSelected.Should().BeFalse();
    }

    [Fact]
    public void Неавторизованный_пользователь_блокируется_при_попытке_перехода_в_настройки()
    {
        using TestContext ctx = new(authenticated: false);
        ShellViewModel shell = ctx.Shell;

        shell.GoSettingsCommand.Execute(null);
        shell.ActiveNav.Should().Be(ShellNav.Auth, "без аккаунта происходит блокировка и редирект на экран авторизации/верификации");

        shell.GoAccountCommand.Execute(null);
        shell.ActiveNav.Should().Be(ShellNav.Auth, "без аккаунта происходит блокировка и редирект на экран авторизации/верификации");
    }

    [Fact]
    public void Авторизованный_пользователь_успешно_переходит_в_закрытые_разделы()
    {
        using TestContext ctx = new(authenticated: true);
        ShellViewModel shell = ctx.Shell;

        shell.GoMedicationsCommand.Execute(null);
        shell.ActiveNav.Should().Be(ShellNav.Medications);
        shell.IsMedicationsSelected.Should().BeTrue();

        shell.GoMedicalCardCommand.Execute(null);
        shell.ActiveNav.Should().Be(ShellNav.MedicalCard);
        shell.IsMedicalCardSelected.Should().BeTrue();

        shell.GoSettingsCommand.Execute(null);
        shell.ActiveNav.Should().Be(ShellNav.Settings);

        shell.GoAccountCommand.Execute(null);
        shell.ActiveNav.Should().Be(ShellNav.Account);
        shell.IsAccountSelected.Should().BeTrue();
    }
}
