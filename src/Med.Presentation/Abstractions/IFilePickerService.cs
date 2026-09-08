namespace Med.Presentation.Abstractions;

/// <summary>Сервис выбора файлов для загрузки (аватарки, документы).</summary>
public interface IFilePickerService
{
    Task<string?> PickImageFileAsync(CancellationToken cancellationToken = default);
}

public sealed class NullFilePickerService : IFilePickerService
{
    public Task<string?> PickImageFileAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);
}
