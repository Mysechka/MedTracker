using Med.Application.Abstractions;
using Med.Domain.Entities;
using Med.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Med.Infrastructure.Supabase.Storage;

public sealed class SupabaseFileStorage : IFileStorage
{
    private const string BucketName = "medical-files";

    private readonly ISupabaseClientAccessor _accessor;
    private readonly IOptions<SupabaseOptions> _options;

    public SupabaseFileStorage(ISupabaseClientAccessor accessor, IOptions<SupabaseOptions> options)
    {
        _accessor = accessor;
        _options = options;
    }

    public async Task<string> UploadAsync(
        Guid userId,
        Guid documentId,
        string fileName,
        ReadOnlyMemory<byte> content,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        global::Supabase.Client client = await _accessor.GetClientAsync(cancellationToken).ConfigureAwait(false);

        string path = Document.BuildStoragePath(userId, documentId, fileName);
        var fileOptions = new global::Supabase.Storage.FileOptions { ContentType = mimeType };

        await client.Storage
            .From(BucketName)
            .Upload(content.ToArray(), path, fileOptions, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return path;
    }

    public async Task<Uri> CreateSignedUrlAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        global::Supabase.Client client = await _accessor.GetClientAsync(cancellationToken).ConfigureAwait(false);

        int ttlSeconds = _options.Value.SignedUrlTtlSeconds;
        string signedUrl = await client.Storage
            .From(BucketName)
            .CreateSignedUrl(path, ttlSeconds)
            .ConfigureAwait(false);

        return new Uri(signedUrl, UriKind.Absolute);
    }

    public async Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        global::Supabase.Client client = await _accessor.GetClientAsync(cancellationToken).ConfigureAwait(false);
        await client.Storage.From(BucketName).Remove(path).ConfigureAwait(false);
    }
}
