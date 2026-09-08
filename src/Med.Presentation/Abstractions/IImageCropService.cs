namespace Med.Presentation.Abstractions;

/// <summary>
/// Сервис кадрирования и сохранения аватара пользователя.
/// </summary>
public interface IImageCropService
{
    Task<string> CropAndSaveAvatarAsync(
        string sourceImagePath,
        Guid userId,
        double zoom,
        double panX,
        double panY,
        int targetSize = 256,
        CancellationToken cancellationToken = default);
}

public sealed class NullImageCropService : IImageCropService
{
    public Task<string> CropAndSaveAvatarAsync(
        string sourceImagePath,
        Guid userId,
        double zoom,
        double panX,
        double panY,
        int targetSize = 256,
        CancellationToken cancellationToken = default)
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string avatarDir = Path.Combine(appData, "MedTracker", "avatars");
        Directory.CreateDirectory(avatarDir);
        string destPath = Path.Combine(avatarDir, $"{userId}.png");
        if (File.Exists(sourceImagePath))
        {
            File.Copy(sourceImagePath, destPath, overwrite: true);
        }

        return Task.FromResult(destPath);
    }
}
