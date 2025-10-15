using SemanticHub.IngestionService.Models;

namespace SemanticHub.IngestionService.Domain.Results;

/// <summary>
/// Represents a processed document ready for indexing.
/// </summary>
public sealed record ProcessedDocument(
    string DocumentId,
    string IndexName,
    IReadOnlyList<DocumentChunk> Chunks,
    DocumentProcessingMetrics Metrics);

public sealed record DocumentProcessingMetrics
{
    public TimeSpan Duration { get; init; }

    public int ChunkCount { get; init; }

    public int TokenCount { get; init; }

    public IReadOnlyDictionary<string, object> AdditionalProperties { get; init; } =
        new Dictionary<string, object>();
}

public sealed record IngestionOutcome
{
    public bool Success { get; init; }

    public ProcessedDocument? Document { get; init; }

    public IngestionError? Error { get; init; }

    public DocumentIngestionResult? LegacyResult { get; init; }

    public IReadOnlyDictionary<string, object> Diagnostics { get; init; } =
        new Dictionary<string, object>();

    public static IngestionOutcome FromSuccess(ProcessedDocument document) =>
        new()
        {
            Success = true,
            Document = document
        };

    public static IngestionOutcome FromLegacyResult(DocumentIngestionResult result, ProcessedDocument? document = null) =>
        new()
        {
            Success = result.Success,
            LegacyResult = result,
            Document = document
        };

    public static IngestionOutcome FromFailure(IngestionError error) =>
        new()
        {
            Success = false,
            Error = error
        };
}
