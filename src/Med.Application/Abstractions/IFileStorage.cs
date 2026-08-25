namespace Med.Application.Abstractions;

/// <summary>Загрузка и signed URL для приватного bucket документов.</summary>
public interface IFileStorage
{
    Task<string> UploadAsync(
        Guid userId,
        Guid documentId,
        string fileName,
        ReadOnlyMemory<byte> content,
        string mimeType,
        CancellationToken cancellationToken = default);

    Task<Uri> CreateSignedUrlAsync(string path, CancellationToken cancellationToken = default);

    Task DeleteAsync(string path, CancellationToken cancellationToken = default);
}
