using Med.Application.Abstractions;
using Med.Domain.Entities;
using Med.Domain.Enums;

namespace Med.Application.UseCases;

public sealed class UploadDocumentUseCase(
    IAuthService auth,
    IFileStorage storage,
    IDocumentRepository documents)
{
    public async Task<Document> ExecuteAsync(
        string fileName,
        ReadOnlyMemory<byte> content,
        string mimeType,
        DocumentType docType,
        Guid? diagnosisId = null,
        Guid? courseId = null,
        CancellationToken cancellationToken = default)
    {
        Guid userId = auth.CurrentUserId
            ?? throw new InvalidOperationException("Пользователь не аутентифицирован.");

        Guid documentId = Guid.NewGuid();
        string path = await storage.UploadAsync(
            userId,
            documentId,
            fileName,
            content,
            mimeType,
            cancellationToken);

        Document document = Document.Create(
            documentId,
            userId,
            path,
            mimeType,
            content.Length,
            docType,
            diagnosisId,
            courseId);

        await documents.UpsertAsync(document, cancellationToken);
        return document;
    }
}
