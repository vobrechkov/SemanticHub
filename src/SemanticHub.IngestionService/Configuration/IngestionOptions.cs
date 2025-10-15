namespace SemanticHub.IngestionService.Configuration;

/// <summary>
/// Root options for the ingestion service.
/// </summary>
public class IngestionOptions
{
    public const string SectionName = "Ingestion";

    public AzureOpenAIOptions AzureOpenAI { get; set; } = new();

    public AzureSearchOptions AzureSearch { get; set; } = new();

    public ChunkingOptions Chunking { get; set; } = new();

    public AzureBlobStorageOptions BlobStorage { get; set; } = new();

    public SitemapIngestionOptions Sitemap { get; set; } = new();

    public OpenApiIngestionOptions OpenApi { get; set; } = new();
}

/// <summary>
/// Azure Blob Storage configuration for reading files.
/// </summary>
public class AzureBlobStorageOptions
{
    public string Endpoint { get; set; } = string.Empty;

    public string DefaultContainer { get; set; } = "documents";

    public string? ConnectionString { get; set; }
}

/// <summary>
/// Azure OpenAI configuration for generating embeddings.
/// </summary>
public class AzureOpenAIOptions
{
    public string Endpoint { get; set; } = string.Empty;

    public string EmbeddingDeployment { get; set; } = string.Empty;

    public string? ApiKey { get; set; }
}

/// <summary>
/// Azure AI Search configuration used for index management.
/// </summary>
public class AzureSearchOptions
{
    public string Endpoint { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string IndexName { get; set; } = string.Empty;

    public bool EnableSemanticRanker { get; set; } = false;

    public string KeyField { get; set; } = "id";

    public string ContentField { get; set; } = "content";

    public string TitleField { get; set; } = "title";

    public string SummaryField { get; set; } = "summary";

    public string SemanticConfiguration { get; set; } = "semantic-config";

    public string VectorField { get; set; } = "contentVector";

    public int VectorDimensions { get; set; } = 1536;

    public int VectorKNearestNeighbors { get; set; } = 8;

    public string ParentDocumentField { get; set; } = "parentDocumentId";

    public string ChunkTitleField { get; set; } = "chunkTitle";

    public string ChunkIndexField { get; set; } = "chunkIndex";

    public string MetadataField { get; set; } = "metadataJson";
}

/// <summary>
/// Controls how documents are chunked prior to embedding.
/// </summary>
public class ChunkingOptions
{
    public int TargetTokenCount { get; set; } = 512;

    public int MaxTokenCount { get; set; } = 1024;

    public double OverlapPercentage { get; set; } = 0.1;
}

/// <summary>
/// Controls behaviour for sitemap-driven ingestion.
/// </summary>
public class SitemapIngestionOptions
{
    public int MaxPages { get; set; } = 200;

    public int MaxDepth { get; set; } = 2;

    public int MaxConcurrency { get; set; } = 3;

    public int ThrottleMilliseconds { get; set; } = 250;

    public bool RespectRobotsTxt { get; set; } = true;

    public string UserAgent { get; set; } = "SemanticHubIngestionBot/1.0";

    public int FetchTimeoutSeconds { get; set; } = 30;

    public int MaxSitemapBytes { get; set; } = 2_000_000;

    public double RecencyHalfLifeDays { get; set; } = 30;

    public double ChangeFrequencyWeight { get; set; } = 0.75;
}

/// <summary>
/// Controls behaviour specific to OpenAPI ingestion.
/// </summary>
public class OpenApiIngestionOptions
{
    /// <summary>
    /// Maximum length of Markdown segments generated per OpenAPI endpoint before splitting.
    /// </summary>
    public int MaxMarkdownSegmentLength { get; set; } = 8000;
}
