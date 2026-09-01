using Avalonia;
using Med.Application.DependencyInjection;
using Med.Infrastructure.DependencyInjection;
using Med.Presentation.DependencyInjection;
using Med.Ui;
using Med.Ui.DependencyInjection;
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
            .LogToTrace()
            .AfterSetup(_ =>
            {
                if (!OperatingSystem.IsMacOS())
                {
                    return;
                }

                string iconPath = Path.Combine(AppContext.BaseDirectory, "app-icon.png");
                MacDockIcon.TrySetFromPng(iconPath);

                // NSApplication иногда ещё не готов в AfterSetup — повторяем после старта цикла.
                Task.Run(async () =>
                {
                    await Task.Delay(300).ConfigureAwait(false);
                    MacDockIcon.TrySetFromPng(iconPath);
                });
            });
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
        // Строго после AddMedPresentation: перекрывает IUiDispatcher на Avalonia-версию.
        services.AddMedUi();

        return services.BuildServiceProvider();
    }
}
