namespace SemanticHub.IngestionService.Domain.Results;

/// <summary>
/// Result returned after a document was ingested.
/// </summary>
public class DocumentIngestionResult
{
    public bool Success { get; set; }

    public string DocumentId { get; set; } = string.Empty;

    public string? IndexName { get; set; }

    public int ChunksIndexed { get; set; }

    public string? Message { get; set; }
}

/// <summary>
/// Result returned after an OpenAPI specification was ingested.
/// </summary>
public class OpenApiIngestionResult
{
    public bool Success { get; set; }

    public string SpecSource { get; set; } = string.Empty;

    public int EndpointsProcessed { get; set; }

    public int TotalEndpoints { get; set; }

    public int TotalChunksIndexed { get; set; }

    public string? Message { get; set; }

    public List<string> Errors { get; set; } = [];
}

/// <summary>
/// Result returned after blob storage ingestion.
/// </summary>
public class BlobIngestionResult
{
    public bool Success { get; set; }

    public string BlobPath { get; set; } = string.Empty;

    public int TotalFiles { get; set; }

    public int FilesProcessed { get; set; }

    public int TotalChunksIndexed { get; set; }

    public string? Message { get; set; }

    public List<string> Errors { get; set; } = [];
}
