using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Med.Presentation.Abstractions;

namespace Med.Ui;

public sealed class AvaloniaFilePickerService : IFilePickerService
{
    public async Task<string?> PickImageFileAsync(CancellationToken cancellationToken = default)
    {
        TopLevel? topLevel = null;
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            topLevel = TopLevel.GetTopLevel(desktop.MainWindow);
        }
        else if (Avalonia.Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            topLevel = TopLevel.GetTopLevel(singleView.MainView);
        }

        if (topLevel?.StorageProvider is null)
        {
            return null;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Выберите изображение профиля",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Изображения (PNG, JPG, JPEG, WEBP)")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp"],
                    MimeTypes = ["image/png", "image/jpeg", "image/webp"],
                },
            ],
        });

        if (files.Count > 0)
        {
            return files[0].TryGetLocalPath();
        }

        return null;
    }
}
