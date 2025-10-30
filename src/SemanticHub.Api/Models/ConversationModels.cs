namespace SemanticHub.Api.Models;

/// <summary>
/// Represents a conversation thread with its messages
/// </summary>
public class Conversation
{
    /// <summary>
    /// Unique identifier for the conversation
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Optional user ID who owns this conversation (nullable for MVP)
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Title of the conversation
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// Timestamp when the conversation was created
    /// </summary>
    public required DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Timestamp when the conversation was last updated
    /// </summary>
    public required DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// List of messages in the conversation
    /// </summary>
    public required List<ChatMessage> Messages { get; set; }
}

/// <summary>
/// Represents a single message in a conversation
/// </summary>
public class ChatMessage
{
    /// <summary>
    /// Unique identifier for the message
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// ID of the conversation this message belongs to
    /// </summary>
    public required string ConversationId { get; set; }

    /// <summary>
    /// Role of the message sender (user or assistant)
    /// </summary>
    public required string Role { get; set; }

    /// <summary>
    /// Content of the message
    /// </summary>
    public required string Content { get; set; }

    /// <summary>
    /// Timestamp when the message was created
    /// </summary>
    public required DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// Optional citations referenced in the message
    /// </summary>
    public List<MessageCitation>? Citations { get; set; }
}

/// <summary>
/// Represents a citation or source reference in a message
/// </summary>
public class MessageCitation
{
    /// <summary>
    /// Optional index of the content part this citation belongs to
    /// </summary>
    public int? PartIndex { get; set; }

    /// <summary>
    /// Content of the citation
    /// </summary>
    public required string Content { get; set; }

    /// <summary>
    /// Unique identifier for the citation
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Optional title of the cited document
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Optional file path of the cited document
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// Optional URL of the cited document
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// Optional chunk ID within the cited document
    /// </summary>
    public string? ChunkId { get; set; }
}

/// <summary>
/// Request model for creating a new conversation
/// </summary>
public class CreateConversationRequest
{
    /// <summary>
    /// Optional title for the conversation. If not provided, a default will be generated.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Optional user ID who owns this conversation
    /// </summary>
    public string? UserId { get; set; }
}

/// <summary>
/// Request model for updating a conversation's title
/// </summary>
public class UpdateConversationTitleRequest
{
    /// <summary>
    /// New title for the conversation
    /// </summary>
    public required string Title { get; set; }
}
