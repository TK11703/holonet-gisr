namespace Gisr.Database.Artifacts;

// Disposing releases the underlying HTTP response.
public sealed class ArtifactBlob(Stream content, string contentType, long length) : IAsyncDisposable
{
    public Stream Content { get; } = content;

    public string ContentType { get; } = contentType;

    public long Length { get; } = length;

    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

public interface IArtifactStore
{
    Task SaveAsync(string blobName, Stream content, string contentType, CancellationToken cancellationToken);

    Task<ArtifactBlob?> OpenReadAsync(string blobName, CancellationToken cancellationToken);

    Task DeleteAsync(string blobName, CancellationToken cancellationToken);
}
