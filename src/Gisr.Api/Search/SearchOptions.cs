namespace Gisr.Api.Search;

public sealed class SearchOptions
{
    public const string SectionName = "Search";

    // Azure OpenAI resource used for embeddings; without it indexing is off and search reports 503.
    public string? OpenAiEndpoint { get; set; }

    public string EmbeddingDeployment { get; set; } = "text-embedding-3-large";

    // Cosine distance above which a chunk is not a match.
    public double MaxDistance { get; set; } = 0.7;

    public IndexingOptions Indexing { get; set; } = new();

    public OcrFallbackOptions OcrFallback { get; set; } = new();

    public sealed class IndexingOptions
    {
        // Null means "on when embeddings are configured".
        public bool? Enabled { get; set; }

        public int PollIntervalSeconds { get; set; } = 5;
    }

    public sealed class OcrFallbackOptions
    {
        public bool Enabled { get; set; } = true;

        public string? Endpoint { get; set; }

        public int MonthlyPageBudget { get; set; } = 2000;
    }
}
