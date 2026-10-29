using Azure.Core;
using Azure.Identity;
using Gisr.Database.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Gisr.Database.Common;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGisrDatabase(this IServiceCollection services, string connectionString, bool useEntraAuthentication = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // DefaultAzureCredential selects a user-assigned managed identity from AZURE_CLIENT_ID.
        TokenCredential? credential = useEntraAuthentication ? new DefaultAzureCredential() : null;
        services.AddSingleton<NpgsqlDataSource>(_ => GisrDataSource.Create(connectionString, credential));
        services.AddSingleton<ISqlScriptLoader, SqlScriptLoader>();
        services.AddSingleton<IMigrationSource, EmbeddedMigrationSource>();
        services.AddSingleton<MigrationRunner>();
        services.AddSingleton<Accounts.AccountRepository>();
        services.AddSingleton<Accounts.ChainCodeRepository>();
        services.AddSingleton<Accounts.AccountAffiliationRepository>();
        services.AddSingleton<Records.RecordRepository>();
        services.AddSingleton<Records.RevisionRepository>();
        services.AddSingleton<Records.AccessLogRepository>();
        services.AddSingleton<Records.ViewGrantRepository>();
        services.AddSingleton<Records.OverrideAttemptRepository>();
        services.AddSingleton<Artifacts.ArtifactRepository>();
        services.AddSingleton<Gallery.GalleryRepository>();
        services.AddSingleton<Search.SearchRepository>();
        services.AddSingleton<Search.IndexJobRepository>();
        services.AddSingleton<Search.OcrUsageRepository>();
        services.AddSingleton<Notifications.NotificationRepository>();
        return services;
    }

    public static IServiceCollection AddGisrArtifactStore(this IServiceCollection services, string? blobsConnectionString, string? blobEndpoint)
    {
        var container = Artifacts.BlobArtifactStore.CreateContainerClient(blobsConnectionString, blobEndpoint, Artifacts.BlobArtifactStore.ContainerName);
        services.AddSingleton<Artifacts.IArtifactStore>(new Artifacts.BlobArtifactStore(container));
        return services;
    }
}
