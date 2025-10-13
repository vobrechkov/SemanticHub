using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace SemanticHub.IngestionService.Diagnostics;

/// <summary>
/// Centralized ActivitySource and Meter definitions for the ingestion service.
/// </summary>
public static class IngestionTelemetry
{
    public const string ActivitySourceName = "SemanticHub.IngestionService";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(ActivitySourceName);

    public static readonly Counter<long> DocumentsIngested =
        Meter.CreateCounter<long>("ingestion_documents_total", description: "Total number of documents submitted for ingestion.");

    public static readonly Counter<long> DocumentChunksIndexed =
        Meter.CreateCounter<long>("ingestion_chunks_total", description: "Total number of chunks indexed in Azure AI Search.");

    public static readonly Counter<long> IngestionFailures =
        Meter.CreateCounter<long>("ingestion_failures_total", description: "Number of ingestion operations that failed.");

    public static readonly Histogram<double> IngestionDurationSeconds =
        Meter.CreateHistogram<double>("ingestion_duration_seconds", unit: "s", description: "Duration of document ingestion operations.");

    public static readonly Histogram<double> EmbeddingGenerationSeconds =
        Meter.CreateHistogram<double>("embedding_generation_seconds", unit: "s", description: "Time spent generating embeddings.");

    public static readonly Counter<long> EmbeddingsGenerated =
        Meter.CreateCounter<long>("embedding_vectors_total", description: "Number of embedding vectors generated.");

    public static readonly Histogram<double> SearchUploadSeconds =
        Meter.CreateHistogram<double>("search_upload_seconds", unit: "s", description: "Time spent uploading chunks to Azure AI Search.");

    public static readonly Counter<long> WebPagesScraped =
        Meter.CreateCounter<long>("webpages_scraped_total", description: "Number of web pages successfully scraped.");

    public static readonly Counter<long> OpenApiEndpointsProcessed =
        Meter.CreateCounter<long>("openapi_endpoints_processed_total", description: "Number of OpenAPI endpoints processed for ingestion.");
}
