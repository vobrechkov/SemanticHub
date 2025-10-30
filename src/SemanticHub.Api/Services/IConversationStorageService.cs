using SemanticHub.Api.Models;

namespace SemanticHub.Api.Services;

/// <summary>
/// Service for managing conversation storage and retrieval
/// </summary>
public interface IConversationStorageService
{
    /// <summary>
    /// Lists conversations with pagination support
    /// </summary>
    /// <param name="offset">Number of conversations to skip (default: 0)</param>
    /// <param name="limit">Maximum number of conversations to return (default: 50)</param>
    /// <param name="userId">Optional user ID to filter conversations</param>
    /// <returns>Collection of conversations ordered by most recently updated</returns>
    Task<IEnumerable<Conversation>> ListConversationsAsync(int offset = 0, int limit = 50, string? userId = null);

    /// <summary>
    /// Retrieves a specific conversation by ID
    /// </summary>
    /// <param name="conversationId">The conversation ID to retrieve</param>
    /// <returns>The conversation if found, otherwise null</returns>
    Task<Conversation?> GetConversationAsync(string conversationId);

    /// <summary>
    /// Creates a new conversation
    /// </summary>
    /// <param name="title">Optional title for the conversation. If not provided, a default is generated.</param>
    /// <param name="userId">Optional user ID who owns this conversation</param>
    /// <returns>The newly created conversation</returns>
    Task<Conversation> CreateConversationAsync(string? title = null, string? userId = null);

    /// <summary>
    /// Updates the title of an existing conversation
    /// </summary>
    /// <param name="conversationId">The conversation ID to update</param>
    /// <param name="title">The new title</param>
    /// <returns>The updated conversation, or null if not found</returns>
    Task<Conversation?> UpdateConversationTitleAsync(string conversationId, string title);

    /// <summary>
    /// Deletes a specific conversation
    /// </summary>
    /// <param name="conversationId">The conversation ID to delete</param>
    /// <returns>True if the conversation was deleted, false if not found</returns>
    Task<bool> DeleteConversationAsync(string conversationId);

    /// <summary>
    /// Deletes all conversations, optionally filtered by user ID
    /// </summary>
    /// <param name="userId">Optional user ID to filter which conversations to delete</param>
    Task DeleteAllConversationsAsync(string? userId = null);

    /// <summary>
    /// Adds a message to an existing conversation
    /// </summary>
    /// <param name="conversationId">The conversation ID to add the message to</param>
    /// <param name="message">The message to add</param>
    /// <returns>True if the message was added, false if the conversation was not found</returns>
    Task<bool> AddMessageAsync(string conversationId, ChatMessage message);
}
