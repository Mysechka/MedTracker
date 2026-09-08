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

            double safeZoom = Math.Max(0.1, zoom);

            // Базовый масштаб: вписывание длинной стороны (Stretch=Uniform), чтобы вся картинка была видна полностью
            double baseScale = Math.Min(ViewportDiameter / srcW, ViewportDiameter / srcH);
            double totalScale = baseScale * safeZoom;

            double viewportToTarget = (double)targetSize / ViewportDiameter;
            double scaleToTarget = totalScale * viewportToTarget;

            double destCenterX = (targetSize / 2.0) + (panX * viewportToTarget);
            double destCenterY = (targetSize / 2.0) + (panY * viewportToTarget);

            double destW = srcW * scaleToTarget;
            double destH = srcH * scaleToTarget;

            float destLeft = (float)(destCenterX - (destW / 2.0));
            float destTop = (float)(destCenterY - (destH / 2.0));

            SKRect sourceRect = new(0, 0, srcW, srcH);
            SKRect destRect = new(destLeft, destTop, (float)(destLeft + destW), (float)(destTop + destH));

            using SKBitmap targetBitmap = new(targetSize, targetSize, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (SKCanvas canvas = new(targetBitmap))
            {
                canvas.Clear(SKColors.Transparent);
                using SKPath clipPath = new();
                clipPath.AddCircle(targetSize / 2f, targetSize / 2f, targetSize / 2f);
                canvas.ClipPath(clipPath, antialias: true);

                using SKPaint paint = new()
                {
                    IsAntialias = true,
                };
                canvas.DrawBitmap(sourceBitmap, sourceRect, destRect, paint);
            }

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string avatarDir = Path.Combine(appData, "MedTracker", "avatars");
            Directory.CreateDirectory(avatarDir);

            // Удаляем старые версии аватара с другими расширениями, чтобы не было конфликтов
            foreach (string ext in new[] { ".jpg", ".jpeg", ".webp" })
            {
                string oldFile = Path.Combine(avatarDir, $"{userId}{ext}");
                if (File.Exists(oldFile))
                {
                    try { File.Delete(oldFile); } catch { }
                }
            }

            string destPath = Path.Combine(avatarDir, $"{userId}.png");

            using SKImage image = SKImage.FromBitmap(targetBitmap);
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
            using FileStream stream = File.Open(destPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            data.SaveTo(stream);

            return destPath;
        }, cancellationToken);
    }
}
