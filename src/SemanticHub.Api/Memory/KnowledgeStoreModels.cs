namespace SemanticHub.Api.Memory;

/// <summary>
/// Represents an indexed document within a knowledge store.
/// </summary>
public sealed record KnowledgeDocument(string DocumentId, string? Title, string? Summary);

/// <summary>
/// Represents a search hit returned from a knowledge store.
/// </summary>
public sealed record KnowledgeRecord(
    KnowledgeDocument Document,
    string Content,
    double Score,
    double NormalizedScore,
    IReadOnlyDictionary<string, object?> Metadata);

/// <summary>
/// Abstraction over retrieval operations used by the agent tooling.
/// </summary>
public interface IKnowledgeStore
{
    Task<IReadOnlyList<KnowledgeRecord>> SearchAsync(
        string query,
        int limit,
        double minRelevance,
        CancellationToken cancellationToken = default);

    Task<KnowledgeDocument?> TryGetDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KnowledgeDocument>> ListDocumentsAsync(
        int limit,
        CancellationToken cancellationToken = default);
}
