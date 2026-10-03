using CommunityToolkit.Mvvm.Messaging;
using FluentAssertions;
using Med.Application.DependencyInjection;
using Med.Infrastructure.DependencyInjection;
using Med.Infrastructure.LocalStorage;
using Med.Presentation.Courses;
using Med.Presentation.DependencyInjection;
using Med.Presentation.Messaging;
using Med.Presentation.Shell;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Med.Presentation.Tests.Shell;

public sealed class ShellNavigationTests
{
    private sealed class TestContext : IDisposable
    {
        public ServiceProvider Provider { get; }
        public ShellViewModel Shell { get; }
        public IMessenger Messenger { get; }
        private readonly LocalDatabase _db;

        public TestContext(bool authenticated)
        {
            string memConn = $"Data Source=InMemoryNavCourses_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            _db = new LocalDatabase(memConn);

            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Supabase:Url"] = "https://project-ref.supabase.co",
                    ["Supabase:AnonKey"] = "eyJhbGciOiJIUzI1NiJ9.eyJyb2xlIjoiYW5vbiJ9.signature",
                    ["Supabase:SignedUrlTtlSeconds"] = "300",
                })
                .Build();

            Messenger = new StrongReferenceMessenger();

            var services = new ServiceCollection();
            services.AddMedApplication();
            services.AddMedInfrastructure(configuration);
            services.AddSingleton(_db);
            services.AddMedPresentation();
            services.AddSingleton<IMessenger>(Messenger);

            Provider = services.BuildServiceProvider();

            Shell = Provider.GetRequiredService<ShellViewModel>();
            if (authenticated)
            {
                Shell.IsAuthenticated = true;
                Shell.AccountName = "user@example.com";
            }
            else
            {
                Shell.IsAuthenticated = false;
                Shell.AccountName = string.Empty;
            }
        }

        public void Dispose()
        {
            Shell.Dispose();
            Provider.Dispose();
            _db.Dispose();
        }
    }

    [Fact]
    public void Авторизованный_пользователь_успешно_переходит_в_курсы()
    {
        using TestContext ctx = new(authenticated: true);
        ShellViewModel shell = ctx.Shell;

        shell.GoCoursesCommand.Execute(null);

        shell.ActiveNav.Should().Be(ShellNav.Courses);
        shell.IsCoursesSelected.Should().BeTrue();
        shell.Current.Should().BeOfType<CoursesViewModel>();
    }

    [Fact]
    public void Неавторизованный_пользователь_блокируется_при_попытке_перехода_в_курсы()
    {
        using TestContext ctx = new(authenticated: false);
        ShellViewModel shell = ctx.Shell;

        shell.GoCoursesCommand.Execute(null);

        shell.ActiveNav.Should().Be(ShellNav.Auth, "без авторизации происходит перенаправление на экран входа");
        shell.IsCoursesSelected.Should().BeFalse();
    }

    [Fact]
    public void Сообщение_NavigateToSectionMessage_переключает_на_курсы()
    {
        using TestContext ctx = new(authenticated: true);
        ShellViewModel shell = ctx.Shell;

        ctx.Messenger.Send(new NavigateToSectionMessage(ShellNav.Courses));

        shell.ActiveNav.Should().Be(ShellNav.Courses);
        shell.IsCoursesSelected.Should().BeTrue();
        shell.Current.Should().BeOfType<CoursesViewModel>();
    }
}
