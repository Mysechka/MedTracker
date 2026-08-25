using Med.Domain.Enums;

namespace Med.Domain.Entities;

/// <summary>Метаданные файла в Storage. Путь: {userId}/{documentId}/{fileName}.</summary>
public sealed record Document(
    Guid Id,
    Guid UserId,
    string StoragePath,
    string MimeType,
    long SizeBytes,
    DocumentType DocType,
    Guid? DiagnosisId,
    Guid? CourseId)
{
    public static Document Create(
        Guid id,
        Guid userId,
        string storagePath,
        string mimeType,
        long sizeBytes,
        DocumentType docType,
        Guid? diagnosisId = null,
        Guid? courseId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);

        if (sizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeBytes));
        }

        if (diagnosisId is null && courseId is null)
        {
            throw new ArgumentException("Нужна ссылка на диагноз и/или курс.");
        }

        return new Document(
            id,
            userId,
            storagePath.Trim(),
            mimeType.Trim(),
            sizeBytes,
            docType,
            diagnosisId,
            courseId);
    }

    public static string BuildStoragePath(Guid userId, Guid documentId, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        return $"{userId}/{documentId}/{fileName.Trim()}";
    }
}
