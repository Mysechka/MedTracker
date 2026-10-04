using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Med.Presentation.Abstractions;

namespace Med.Ui;

public sealed class AvaloniaFilePickerService : IFilePickerService
{
    public static Func<TopLevel?>? FallbackTopLevelResolver { get; set; }

    public async Task<string?> PickImageFileAsync(CancellationToken cancellationToken = default)
    {
        TopLevel? topLevel = null;
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            topLevel = desktop.MainWindow;
        }
        else if (Avalonia.Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            topLevel = TopLevel.GetTopLevel(singleView.MainView);
        }

        topLevel ??= FallbackTopLevelResolver?.Invoke();

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
                FilePickerFileTypes.ImageAll,
                new FilePickerFileType("Изображения (PNG, JPG, JPEG, WEBP)")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp"],
                    MimeTypes = ["image/*"],
                },
            ],
        });

        if (files.Count > 0)
        {
            var file = files[0];

            // 1. Прямой путь на файловой системе (Desktop)
            string? localPath = file.TryGetLocalPath();
            if (!string.IsNullOrEmpty(localPath) && File.Exists(localPath))
            {
                return localPath;
            }

            // 2. Универсальное извлечение потока (Android content:// URI, галерея, недавние фото)
            try
            {
                string ext = Path.GetExtension(file.Name);
                if (string.IsNullOrWhiteSpace(ext))
                {
                    ext = ".jpg";
                }

                string tempDir = Path.Combine(Path.GetTempPath(), "MedTracker", "Avatars");
                Directory.CreateDirectory(tempDir);
                string tempFilePath = Path.Combine(tempDir, $"picked_avatar_{Guid.NewGuid():N}{ext}");

                await using (var sourceStream = await file.OpenReadAsync())
                await using (var destStream = File.Create(tempFilePath))
                {
                    await sourceStream.CopyToAsync(destStream, cancellationToken);
                }

                if (File.Exists(tempFilePath) && new FileInfo(tempFilePath).Length > 0)
                {
                    return tempFilePath;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Ошибка чтения выбранного файла изображения: {ex}");
            }
        }

        return null;
    }
}
