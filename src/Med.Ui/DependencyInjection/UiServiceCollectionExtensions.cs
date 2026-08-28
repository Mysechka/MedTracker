using Med.Presentation.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Med.Ui.DependencyInjection;

public static class UiServiceCollectionExtensions
{
    /// <summary>
    /// Регистрация UI-зависимостей. Вызывать после <c>AddMedPresentation</c>: последняя
    /// регистрация выигрывает, и <see cref="IUiDispatcher"/> становится Avalonia-версией.
    /// </summary>
    public static IServiceCollection AddMedUi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();

        return services;
    }
}
