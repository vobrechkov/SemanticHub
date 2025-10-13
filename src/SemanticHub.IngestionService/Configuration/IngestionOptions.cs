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
