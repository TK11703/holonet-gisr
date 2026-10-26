using Microsoft.AspNetCore.Authentication;
using Gisr.Api.Accounts;
using Gisr.Api.Search;
using Gisr.Api.Search.Ocr;
using Gisr.Api.Tests.Search;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Gisr.Api.Tests.Common;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private readonly string _blobConnectionString;

    private ApiFactory(string connectionString, string blobConnectionString)
    {
        _connectionString = connectionString;
        _blobConnectionString = blobConnectionString;
    }

    public FakeTenantDirectory Directory { get; } = new();

    public FakeAccountIdentity Identity { get; } = new();

    public CapturingLogSink Logs { get; } = new();

    // The embedding service and OCR cost money and are non-deterministic, so only they are faked; the database and blobs are real.
    public FakeEmbeddingGenerator Embeddings { get; } = new();

    public FakeOcrClient Ocr { get; } = new();

    public TestClock Clock { get; } = new(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));

    // Set before the first request; the host is built lazily.
    public Action<SearchOptions>? SearchOptionsOverride { get; set; }

    // Tests that never touch artifacts skip Azurite; the store only connects when it is first used.
    public static async Task<ApiFactory> CreateAsync(PostgresFixture postgres, AzuriteFixture? azurite = null) =>
        new(await postgres.CreateDatabaseAsync(), azurite is null ? "UseDevelopmentStorage=true" : await azurite.GetConnectionStringAsync());

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Gisr", _connectionString);
        builder.UseSetting("ConnectionStrings:blobs", _blobConnectionString);
        builder.UseSetting("AzureAd:Instance", "https://login.microsoftonline.com/");
        builder.UseSetting("AzureAd:TenantId", "11111111-1111-1111-1111-111111111111");
        builder.UseSetting("AzureAd:ClientId", "22222222-2222-2222-2222-222222222222");
        builder.UseSetting("Search:MaxDistance", "0.9");

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

            services.RemoveAll<ITenantDirectory>();
            services.AddSingleton<ITenantDirectory>(Directory);
            services.RemoveAll<IAccountIdentityDirectory>();
            services.AddSingleton<IAccountIdentityDirectory>(Identity);
            services.AddSingleton<Serilog.Core.ILogEventSink>(Logs);

            services.RemoveAll<IEmbeddingGenerator<string, Embedding<float>>>();
            services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(Embeddings);
            services.RemoveAll<IOcrClient>();
            services.AddSingleton<IOcrClient>(Ocr);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.PostConfigure<SearchOptions>(options => SearchOptionsOverride?.Invoke(options));
        });
    }

    public HttpClient CreateUserClient(Guid? oid = null, bool officer = false, Guid? tid = null, string name = "Test User")
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.OidHeader, (oid ?? Guid.NewGuid()).ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TidHeader, (tid ?? TestTenant).ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.NameHeader, name);
        if (officer)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, "Officer");
        }

        return client;
    }

    public static readonly Guid TestTenant = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public async Task<T?> ScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? default : (T)Convert.ChangeType(result, typeof(T));
    }
}
