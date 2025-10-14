namespace SemanticHub.IngestionService.Domain.Aggregates;

/// <summary>
/// Describes metadata that accompanies an ingestion request.
/// </summary>
public sealed record IngestionMetadata
{
    private static readonly IReadOnlyList<string> EmptyTags = Array.Empty<string>();

    public string? DocumentId { get; init; }

    public string Title { get; init; } = "Untitled";

    public string SourceType { get; init; } = "manual";

    public Uri? SourceUri { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = EmptyTags;

    public IReadOnlyDictionary<string, object> CustomMetadata { get; init; } = new Dictionary<string, object>();

    public static IngestionMetadata Create(
        string? documentId,
        string? title,
        string? sourceType,
        Uri? sourceUri,
        IEnumerable<string>? tags,
        IReadOnlyDictionary<string, object>? metadata)
    {
        return new IngestionMetadata
        {
            DocumentId = string.IsNullOrWhiteSpace(documentId) ? null : documentId,
            Title = string.IsNullOrWhiteSpace(title) ? "Untitled" : title!,
            SourceType = string.IsNullOrWhiteSpace(sourceType) ? "manual" : sourceType!,
            SourceUri = sourceUri,
            Tags = tags?.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToArray()
                ?? EmptyTags,
            CustomMetadata = metadata ?? new Dictionary<string, object>()
        };
    }
}
