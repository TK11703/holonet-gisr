using Azure.AI.DocumentIntelligence;
using Azure.AI.OpenAI;
using Azure.Identity;
using Gisr.Api.Search.Extraction;
using Gisr.Api.Search.Ocr;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Gisr.Api.Search;

public static class SearchServiceCollectionExtensions
{
    // Indexing needs embeddings; it can also be switched off explicitly.
    public static bool IndexingEnabled(SearchOptions options) =>
        options.Indexing.Enabled ?? !string.IsNullOrWhiteSpace(options.OpenAiEndpoint);

    public static IServiceCollection AddSearchServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SearchOptions>(configuration.GetSection(SearchOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<PlainTextExtractor>();
        services.AddSingleton<DocxExtractor>();
        services.AddSingleton<PdfExtractor>();
        services.AddSingleton<OcrFallbackExtractor>();
        services.AddSingleton<ITextExtractor>(sp => sp.GetRequiredService<PlainTextExtractor>());
        services.AddSingleton<ITextExtractor>(sp => sp.GetRequiredService<DocxExtractor>());
        services.AddSingleton<ITextExtractor>(sp => sp.GetRequiredService<OcrFallbackExtractor>());
        services.AddSingleton<DocumentTextExtractor>();

        // Managed identity only: no keys are configured for either Azure service.
        services.AddSingleton<IOcrClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<SearchOptions>>().Value.OcrFallback;
            return options.Enabled && !string.IsNullOrWhiteSpace(options.Endpoint)
                ? new DocumentIntelligenceOcrClient(new DocumentIntelligenceClient(new Uri(options.Endpoint), new DefaultAzureCredential()))
                : new NoOcrClient();
        });
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<SearchOptions>>().Value;
            return string.IsNullOrWhiteSpace(options.OpenAiEndpoint)
                ? new UnavailableEmbeddingGenerator()
                : new AzureOpenAIClient(new Uri(options.OpenAiEndpoint), new DefaultAzureCredential())
                    .GetEmbeddingClient(options.EmbeddingDeployment)
                    .AsIEmbeddingGenerator(Gisr.Database.Search.SearchRepository.Dimensions);
        });

        services.AddSingleton<SearchIndexingWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<SearchIndexingWorker>());
        services.AddSingleton<MonthlyRequeueService>();
        services.AddHostedService(sp => sp.GetRequiredService<MonthlyRequeueService>());
        services.AddScoped<SearchService>();
        return services;
    }
}
