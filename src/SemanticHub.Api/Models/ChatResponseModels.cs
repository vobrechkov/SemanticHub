namespace SemanticHub.Api.Models;

/// <summary>
/// Represents a streamed chunk of chat response data in SSE format
/// </summary>
public class StreamedChatChunk
{
    /// <summary>
    /// Unique identifier for this message
    /// </summary>
    public required string MessageId { get; set; }

    /// <summary>
    /// Optional conversation identifier for tracking multi-turn conversations
    /// </summary>
    public string? ConversationId { get; set; }

    /// <summary>
    /// The content/text of this chunk
    /// </summary>
    public required string Content { get; set; }

    /// <summary>
    /// The role of the message sender (e.g., "assistant", "user")
    /// </summary>
    public required string Role { get; set; }

    /// <summary>
    /// Citations/sources referenced in this response
    /// </summary>
    public List<CitationInfo>? Citations { get; set; }

    /// <summary>
    /// Indicates whether this is the final chunk in the stream
    /// </summary>
    public bool IsComplete { get; set; }

    /// <summary>
    /// Timestamp when this chunk was generated
    /// </summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Represents citation/source information for a knowledge base search result
/// </summary>
public class CitationInfo
{
    /// <summary>
    /// Index of this citation within the response
    /// </summary>
    public int? PartIndex { get; set; }

    /// <summary>
    /// The content/excerpt from the source document
    /// </summary>
    public required string Content { get; set; }

    /// <summary>
    /// Unique identifier for the source document
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Title of the source document
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// File path of the source document (if applicable)
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// URL of the source document (if applicable)
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// Chunk identifier within the source document
    /// </summary>
    public string? ChunkId { get; set; }

    /// <summary>
    /// Relevance score from the search (0.0 to 1.0)
    /// </summary>
    public double? Score { get; set; }
}
