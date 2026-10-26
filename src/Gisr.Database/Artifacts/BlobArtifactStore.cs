using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Gisr.Database.Artifacts;

public sealed class BlobArtifactStore(BlobContainerClient container) : IArtifactStore
{
    public const string ContainerName = "artifacts";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _containerReady;

    // `connectionString` is a storage or Aspire connection string; `blobEndpoint` is used with managed identity when it is absent.
    public static BlobContainerClient CreateContainerClient(string? connectionString, string? blobEndpoint, string containerName)
    {
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            if (TryGetEndpoint(connectionString, out var endpoint))
            {
                return FromEndpoint(endpoint, containerName);
            }

            return new BlobContainerClient(connectionString, containerName);
        }

        if (!string.IsNullOrWhiteSpace(blobEndpoint))
        {
            return FromEndpoint(blobEndpoint, containerName);
        }

        throw new InvalidOperationException("Configure ConnectionStrings:blobs or Storage:BlobEndpoint for artifact storage.");
    }

    public async Task SaveAsync(string blobName, Stream content, string contentType, CancellationToken cancellationToken)
    {
        await EnsureContainerAsync(cancellationToken);
        await container.GetBlobClient(blobName).UploadAsync(
            content,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } },
            cancellationToken);
    }

    public async Task<ArtifactBlob?> OpenReadAsync(string blobName, CancellationToken cancellationToken)
    {
        await EnsureContainerAsync(cancellationToken);
        try
        {
            var response = await container.GetBlobClient(blobName).DownloadStreamingAsync(cancellationToken: cancellationToken);
            return new ArtifactBlob(response.Value.Content, response.Value.Details.ContentType, response.Value.Details.ContentLength);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string blobName, CancellationToken cancellationToken)
    {
        await EnsureContainerAsync(cancellationToken);
        await container.GetBlobClient(blobName).DeleteIfExistsAsync(cancellationToken: cancellationToken);
    }

    private static bool TryGetEndpoint(string connectionString, out string endpoint)
    {
        endpoint = string.Empty;
        if (Uri.TryCreate(connectionString, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
        {
            endpoint = connectionString;
            return true;
        }

        var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split('=', 2))
            .Where(pair => pair.Length == 2)
            .ToDictionary(pair => pair[0], pair => pair[1], StringComparer.OrdinalIgnoreCase);

        // "Endpoint=<uri>" alone (no account name or key) means managed identity.
        if (parts.TryGetValue("Endpoint", out var value) && !parts.ContainsKey("AccountName") && !parts.ContainsKey("BlobEndpoint"))
        {
            endpoint = value;
            return true;
        }

        return false;
    }

    private static BlobContainerClient FromEndpoint(string endpoint, string containerName) =>
        new(new Uri($"{endpoint.TrimEnd('/')}/{containerName}"), new DefaultAzureCredential());

    private async Task EnsureContainerAsync(CancellationToken cancellationToken)
    {
        if (_containerReady)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_containerReady)
            {
                await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
                _containerReady = true;
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
