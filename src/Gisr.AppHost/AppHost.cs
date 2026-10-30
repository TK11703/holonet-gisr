var builder = DistributedApplication.CreateBuilder(args);

var tenantId = builder.AddParameter("azure-ad-tenant-id");
var apiClientId = builder.AddParameter("azure-ad-api-client-id");
var webClientId = builder.AddParameter("azure-ad-web-client-id");
var webClientSecret = builder.AddParameter("azure-ad-web-client-secret", secret: true);
var apiScope = builder.AddParameter("azure-ad-api-scope");

// pgvector image is required for the semantic search `vector` extension.
var postgres = builder.AddPostgres("postgres")
    .WithImage("pgvector/pgvector")
    .WithImageTag("pg17")
    .WithDataVolume();
var gisrDatabase = postgres.AddDatabase("Gisr");

var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator(emulator => emulator.WithDataVolume());
var blobs = storage.AddBlobs("blobs");

var api = builder.AddProject<Projects.Gisr_Api>("api")
    .WithReference(gisrDatabase).WaitFor(gisrDatabase)
    .WithReference(blobs).WaitFor(blobs)
    .WithEnvironment("Search__OpenAiEndpoint", "https://aif-shared-acc.openai.azure.com/")
    .WithEnvironment("Search__EmbeddingDeployment", "text-embedding-3-large")
    .WithEnvironment("SummaryEnhancement__OpenAiEndpoint", "https://aif-shared-acc.openai.azure.com/")
    .WithEnvironment("SummaryEnhancement__ChatDeployment", "gpt-4.1-mini")
    .WithEnvironment("Search__OcrFallback__Endpoint", "https://aif-shared-acc.services.ai.azure.com/")
    // Locally there is no managed identity: use the developer's `az login` rather than probing IMDS or another editor's account.
    .WithEnvironment("AZURE_TOKEN_CREDENTIALS", "AzureCliCredential")
    .WithEnvironment("AzureAd__TenantId", tenantId)
    .WithEnvironment("AzureAd__ClientId", apiClientId);

// The Api calls Microsoft Graph on behalf of the signed-in Officer; without a secret the citizen picker reports the directory as unavailable.
if (builder.Configuration["Parameters:azure-ad-api-client-secret"] is { Length: > 0 })
{
    var apiClientSecret = builder.AddParameter("azure-ad-api-client-secret", secret: true);
    api.WithEnvironment("AzureAd__ClientCredentials__0__SourceType", "ClientSecret")
        .WithEnvironment("AzureAd__ClientCredentials__0__ClientSecret", apiClientSecret);
}

builder.AddProject<Projects.Gisr_Web>("web")
    .WithExternalHttpEndpoints()
    .WaitFor(api)
    .WithEnvironment("Api__BaseUrl", api.GetEndpoint("https"))
    .WithEnvironment("Api__Scope", apiScope)
    .WithEnvironment("AzureAd__TenantId", tenantId)
    .WithEnvironment("AzureAd__ClientId", webClientId)
    .WithEnvironment("AzureAd__ClientCredentials__0__SourceType", "ClientSecret")
    .WithEnvironment("AzureAd__ClientCredentials__0__ClientSecret", webClientSecret);

builder.Build().Run();
