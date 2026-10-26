using System.Text;
using Azure.Storage.Blobs;
using Gisr.Database.Artifacts;
using Gisr.Database.Tests.Common;

namespace Gisr.Database.Tests.Artifacts;

[Collection("azurite")]
public class BlobArtifactStoreTests(AzuriteFixture azurite)
{
    private async Task<BlobArtifactStore> CreateStoreAsync() =>
        new(new BlobContainerClient(await azurite.GetConnectionStringAsync(), "artifacts-" + Guid.NewGuid().ToString("N")));

    private static MemoryStream Bytes(string text) => new(Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task SavedBlobs_CanBeReadBack_WithTheirContentTypeAndLength()
    {
        var store = await CreateStoreAsync();
        await store.SaveAsync("rec/one", Bytes("hello holonet"), "text/plain", CancellationToken.None);

        await using var blob = await store.OpenReadAsync("rec/one", CancellationToken.None);

        Assert.NotNull(blob);
        Assert.Equal("text/plain", blob.ContentType);
        Assert.Equal(13, blob.Length);
        using var reader = new StreamReader(blob.Content, Encoding.UTF8);
        Assert.Equal("hello holonet", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task ThumbnailsAreSavedNextToTheOriginal()
    {
        var store = await CreateStoreAsync();
        await store.SaveAsync("rec/one", Bytes("original"), "image/png", CancellationToken.None);
        await store.SaveAsync("rec/one.thumb.jpg", Bytes("thumb"), "image/jpeg", CancellationToken.None);

        await using var thumbnail = await store.OpenReadAsync("rec/one.thumb.jpg", CancellationToken.None);

        Assert.NotNull(thumbnail);
        Assert.Equal("image/jpeg", thumbnail.ContentType);
        Assert.Equal(5, thumbnail.Length);
    }

    [Fact]
    public async Task SavingTheSameName_ReplacesTheContent()
    {
        var store = await CreateStoreAsync();
        await store.SaveAsync("rec/one", Bytes("first"), "text/plain", CancellationToken.None);
        await store.SaveAsync("rec/one", Bytes("second!"), "text/plain", CancellationToken.None);

        await using var blob = await store.OpenReadAsync("rec/one", CancellationToken.None);

        Assert.Equal(7, blob!.Length);
    }

    [Fact]
    public async Task OpeningAMissingBlob_ReturnsNull()
    {
        var store = await CreateStoreAsync();

        Assert.Null(await store.OpenReadAsync("rec/missing", CancellationToken.None));
    }

    [Fact]
    public async Task Delete_RemovesTheBlob_AndIsIdempotent()
    {
        var store = await CreateStoreAsync();
        await store.SaveAsync("rec/one", Bytes("hello"), "text/plain", CancellationToken.None);

        await store.DeleteAsync("rec/one", CancellationToken.None);
        await store.DeleteAsync("rec/one", CancellationToken.None);
        await store.DeleteAsync("rec/never-existed", CancellationToken.None);

        Assert.Null(await store.OpenReadAsync("rec/one", CancellationToken.None));
    }

    [Fact]
    public async Task TheContainerIsPrivate()
    {
        var connection = await azurite.GetConnectionStringAsync();
        var container = new BlobContainerClient(connection, "artifacts-" + Guid.NewGuid().ToString("N"));
        var store = new BlobArtifactStore(container);
        await store.SaveAsync("rec/one", Bytes("hello"), "text/plain", CancellationToken.None);

        var properties = (await container.GetPropertiesAsync()).Value;

        Assert.Equal(Azure.Storage.Blobs.Models.PublicAccessType.None, properties.PublicAccess ?? Azure.Storage.Blobs.Models.PublicAccessType.None);
    }

    [Fact]
    public async Task ContainerClient_IsBuiltFromAConnectionString()
    {
        var connection = await azurite.GetConnectionStringAsync();
        var container = BlobArtifactStore.CreateContainerClient(connection, null, "artifacts-" + Guid.NewGuid().ToString("N"));
        var store = new BlobArtifactStore(container);
        await store.SaveAsync("rec/one", Bytes("hello"), "text/plain", CancellationToken.None);

        Assert.NotNull(await store.OpenReadAsync("rec/one", CancellationToken.None));
    }

    [Fact]
    public void ContainerClient_FallsBackToTheBlobEndpoint_WithManagedIdentity()
    {
        var container = BlobArtifactStore.CreateContainerClient(null, "https://gisr.blob.core.windows.net", "artifacts");

        Assert.Equal("artifacts", container.Name);
        Assert.Equal("gisr.blob.core.windows.net", container.Uri.Host);
    }

    [Fact]
    public void ContainerClient_AcceptsAnAspireEndpointConnectionString()
    {
        var container = BlobArtifactStore.CreateContainerClient("Endpoint=https://gisr.blob.core.windows.net/;", null, "artifacts");

        Assert.Equal("gisr.blob.core.windows.net", container.Uri.Host);
    }

    [Fact]
    public void ContainerClient_RequiresSomeConfiguration()
    {
        Assert.Throws<InvalidOperationException>(() => BlobArtifactStore.CreateContainerClient(null, null, "artifacts"));
    }
}
