using Testcontainers.Azurite;

namespace Gisr.Api.Tests.Common;

// Starts lazily so test runs that never touch blobs do not pay for the container.
public sealed class AzuriteFixture : IAsyncLifetime
{
    private readonly AzuriteContainer _container = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.35.0")
        .WithCommand("--skipApiVersionCheck")
        .Build();

    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _started;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_started)
        {
            await _container.DisposeAsync();
        }
    }

    public async Task<string> GetConnectionStringAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (!_started)
            {
                await _container.StartAsync();
                _started = true;
            }
        }
        finally
        {
            _gate.Release();
        }

        return _container.GetConnectionString();
    }
}
