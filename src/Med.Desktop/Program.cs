using Avalonia;
using Med.Application.DependencyInjection;
using Med.Infrastructure.DependencyInjection;
using Med.Presentation.DependencyInjection;
using Med.Ui;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Med.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
    {
        App.UseServices(BuildServices());

        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }

    private static IServiceProvider BuildServices()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables("MEDTRACKER_")
            .Build();

        ServiceCollection services = new();
        services.AddSingleton(configuration);
        services.AddLogging(builder => builder.AddSimpleConsole());
        services.AddMedApplication();
        services.AddMedInfrastructure(configuration);
        services.AddMedPresentation();

        return services.BuildServiceProvider();
    }
}
