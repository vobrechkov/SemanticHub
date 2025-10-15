namespace SemanticHub.IngestionService.Domain.Results;

/// <summary>
/// Represents a failure that occurred during ingestion.
/// </summary>
public sealed record IngestionError(
    IngestionErrorCode Code,
    string Message,
    Exception? Exception = null,
    IReadOnlyDictionary<string, object>? Details = null)
{
    public IReadOnlyDictionary<string, object> Details { get; } =
        Details ?? new Dictionary<string, object>();
}

public enum IngestionErrorCode
{
    Unknown = 0,
    ValidationFailed = 1,
    ScrapeFailed = 2,
    ContentMissing = 3,
    ProcessingFailed = 4,
    IndexingFailed = 5,
    ExternalDependency = 6,
    Timeout = 7
}
