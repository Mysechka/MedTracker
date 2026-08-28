using Android.App;
using Android.Content.Res;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Med.Application.DependencyInjection;
using Med.Infrastructure.DependencyInjection;
using Med.Presentation.DependencyInjection;
using Med.Ui;
using Med.Ui.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Med.Android;

/// <summary>
/// Композиция контейнера для Android-головы. Конфигурация читается из ассета,
/// потому что переменных окружения на устройстве нет; в ассете лежит только anon key.
/// </summary>
[global::Android.App.Application]
public sealed class MedAndroidApplication : AvaloniaAndroidApplication<App>
{
    private const string ConfigAssetName = "appsettings.json";

    public MedAndroidApplication(nint javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        App.UseServices(BuildServices());

        return base.CustomizeAppBuilder(builder);
    }

    private IServiceProvider BuildServices()
    {
        IConfiguration configuration = BuildConfiguration();

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

    private IConfiguration BuildConfiguration()
    {
        ConfigurationBuilder builder = new();

        using Stream? asset = TryOpenConfigAsset();
        if (asset is not null)
        {
            builder.AddJsonStream(asset);
        }

        return builder.Build();
    }

    private Stream? TryOpenConfigAsset()
    {
        AssetManager? assets = Assets;
        if (assets is null)
        {
            return null;
        }

        return assets.List(string.Empty)?.Contains(ConfigAssetName) == true
            ? assets.Open(ConfigAssetName)
            : null;
    }
}
