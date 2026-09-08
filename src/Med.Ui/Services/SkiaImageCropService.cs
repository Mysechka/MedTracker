using Med.Presentation.Abstractions;
using SkiaSharp;

namespace Med.Ui.Services;

public sealed class SkiaImageCropService : IImageCropService
{
    private const double ViewportDiameter = 200.0;

    public async Task<string> CropAndSaveAvatarAsync(
        string sourceImagePath,
        Guid userId,
        double zoom,
        double panX,
        double panY,
        int targetSize = 256,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(sourceImagePath) || !File.Exists(sourceImagePath))
        {
            throw new FileNotFoundException("Файл изображения не найден.", sourceImagePath);
        }

        return await Task.Run(() =>
        {
            using SKBitmap sourceBitmap = SKBitmap.Decode(sourceImagePath)
                ?? throw new InvalidOperationException("Не удалось декодировать изображение.");

            int srcW = sourceBitmap.Width;
            int srcH = sourceBitmap.Height;

            double safeZoom = Math.Max(1.0, zoom);

            // Базовый масштаб: вписывание короткой стороны в диаметр видоискателя
            double baseScale = Math.Max(ViewportDiameter / srcW, ViewportDiameter / srcH);
            double totalScale = baseScale * safeZoom;

            // Размер видимого окна в координатах исходного изображения
            double cropW = ViewportDiameter / totalScale;
            double cropH = ViewportDiameter / totalScale;

            // Центр видимого окна с учётом смещения (панорамирования)
            double centerX = (srcW / 2.0) - (panX / totalScale);
            double centerY = (srcH / 2.0) - (panY / totalScale);

            double left = centerX - (cropW / 2.0);
            double top = centerY - (cropH / 2.0);

            // Ограничение границ, чтобы окно не выходило за пределы картинки (если размер позволяет)
            if (cropW <= srcW)
            {
                left = Math.Clamp(left, 0, srcW - cropW);
            }
            else
            {
                left = (srcW - cropW) / 2.0;
            }

            if (cropH <= srcH)
            {
                top = Math.Clamp(top, 0, srcH - cropH);
            }
            else
            {
                top = (srcH - cropH) / 2.0;
            }

            SKRect sourceRect = new((float)left, (float)top, (float)(left + cropW), (float)(top + cropH));
            SKRect destRect = new(0, 0, targetSize, targetSize);

            using SKBitmap targetBitmap = new(targetSize, targetSize, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (SKCanvas canvas = new(targetBitmap))
            {
                canvas.Clear(SKColors.Transparent);
                using SKPaint paint = new()
                {
                    IsAntialias = true,
                };
                canvas.DrawBitmap(sourceBitmap, sourceRect, destRect, paint);
            }

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string avatarDir = Path.Combine(appData, "MedTracker", "avatars");
            Directory.CreateDirectory(avatarDir);

            string destPath = Path.Combine(avatarDir, $"{userId}.png");

            using SKImage image = SKImage.FromBitmap(targetBitmap);
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
            using FileStream stream = File.Open(destPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            data.SaveTo(stream);

            return destPath;
        }, cancellationToken);
    }
}
