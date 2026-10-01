using Avalonia;
using Med.Application.DependencyInjection;
using Med.Infrastructure.DependencyInjection;
using Med.Infrastructure.Logging;
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
            });
    }

    private static IServiceProvider BuildServices()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false)
            .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables("MEDTRACKER_")
            .Build();

        ServiceCollection services = new();
        services.AddSingleton(configuration);
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddSimpleConsole(options =>
            {
                options.TimestampFormat = "[HH:mm:ss.fff] ";
                options.SingleLine = true;
                options.IncludeScopes = false;
            });
            builder.AddFile();
        });

        services.AddMedApplication();
        services.AddMedInfrastructure(configuration);
        services.AddMedPresentation();
        // Строго после AddMedPresentation: перекрывает IUiDispatcher на Avalonia-версию.
        services.AddMedUi();

        ServiceProvider provider = services.BuildServiceProvider();

        ILogger? logger = provider.GetService<ILoggerFactory>()?.CreateLogger("Program");
        logger?.LogInformation("MedTracker Desktop starting up (Environment: {Env})",
            Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production");

        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                logger?.LogCritical(ex, "[Global] Unhandled AppDomain exception (IsTerminating: {IsTerminating})", args.IsTerminating);
            }
            else
            {
                logger?.LogCritical("[Global] Unhandled AppDomain non-exception object: {Obj}", args.ExceptionObject);
            }
        };

        TaskScheduler.UnobservedTaskException += (sender, args) =>
        {
            logger?.LogError(args.Exception, "[Global] Unobserved task exception");
            args.SetObserved();
        };

        return provider;
    }
}
