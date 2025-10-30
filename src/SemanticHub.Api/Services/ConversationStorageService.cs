using SemanticHub.Api.Models;
using System.Collections.Concurrent;

namespace SemanticHub.Api.Services;

/// <summary>
/// In-memory implementation of conversation storage service
/// </summary>
public class ConversationStorageService : IConversationStorageService
{
    private readonly ConcurrentDictionary<string, Conversation> _conversations = new();
    private readonly ILogger<ConversationStorageService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConversationStorageService"/> class
    /// </summary>
    /// <param name="logger">Logger instance for structured logging</param>
    public ConversationStorageService(ILogger<ConversationStorageService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public Task<IEnumerable<Conversation>> ListConversationsAsync(int offset = 0, int limit = 50, string? userId = null)
    {
        _logger.LogDebug("Listing conversations with offset={Offset}, limit={Limit}, userId={UserId}",
            offset, limit, userId ?? "none");

        var query = _conversations.Values.AsEnumerable();

        // Filter by user ID if provided
        if (!string.IsNullOrWhiteSpace(userId))
        {
            query = query.Where(c => c.UserId == userId);
        }

        // Order by most recently updated, apply pagination
        var result = query
            .OrderByDescending(c => c.UpdatedAt)
            .Skip(offset)
            .Take(limit)
            .ToList();

        _logger.LogInformation("Returned {Count} conversations", result.Count);

        return Task.FromResult<IEnumerable<Conversation>>(result);
    }

    /// <inheritdoc/>
    public Task<Conversation?> GetConversationAsync(string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            _logger.LogWarning("GetConversationAsync called with null or empty conversationId");
            return Task.FromResult<Conversation?>(null);
        }

        _logger.LogDebug("Retrieving conversation {ConversationId}", conversationId);

        var found = _conversations.TryGetValue(conversationId, out var conversation);

        if (!found)
        {
            _logger.LogWarning("Conversation {ConversationId} not found", conversationId);
            return Task.FromResult<Conversation?>(null);
        }

        _logger.LogDebug("Found conversation {ConversationId} with {MessageCount} messages",
            conversationId, conversation?.Messages.Count ?? 0);

        return Task.FromResult<Conversation?>(conversation);
    }

    /// <inheritdoc/>
    public Task<Conversation> CreateConversationAsync(string? title = null, string? userId = null)
    {
        var conversationId = Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow;

        var conversation = new Conversation
        {
            Id = conversationId,
            UserId = userId,
            Title = title ?? "New Conversation",
            CreatedAt = now,
            UpdatedAt = now,
            Messages = new List<ChatMessage>()
        };

        if (!_conversations.TryAdd(conversationId, conversation))
        {
            _logger.LogError("Failed to add conversation {ConversationId} to storage", conversationId);
            throw new InvalidOperationException($"Failed to create conversation with ID {conversationId}");
        }

        _logger.LogInformation("Created conversation {ConversationId} with title '{Title}' for user {UserId}",
            conversationId, conversation.Title, userId ?? "anonymous");

        return Task.FromResult(conversation);
    }

    /// <inheritdoc/>
    public Task<Conversation?> UpdateConversationTitleAsync(string conversationId, string title)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            _logger.LogWarning("UpdateConversationTitleAsync called with null or empty conversationId");
            return Task.FromResult<Conversation?>(null);
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            _logger.LogWarning("UpdateConversationTitleAsync called with null or empty title");
            throw new ArgumentException("Title cannot be null or empty", nameof(title));
        }

        _logger.LogDebug("Updating title for conversation {ConversationId} to '{Title}'", conversationId, title);

        if (!_conversations.TryGetValue(conversationId, out var conversation))
        {
            _logger.LogWarning("Cannot update title: conversation {ConversationId} not found", conversationId);
            return Task.FromResult<Conversation?>(null);
        }

        // Update the conversation
        conversation.Title = title;
        conversation.UpdatedAt = DateTimeOffset.UtcNow;

        _logger.LogInformation("Updated conversation {ConversationId} title to '{Title}'", conversationId, title);

        return Task.FromResult<Conversation?>(conversation);
    }

    /// <inheritdoc/>
    public Task<bool> DeleteConversationAsync(string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            _logger.LogWarning("DeleteConversationAsync called with null or empty conversationId");
            return Task.FromResult(false);
        }

        _logger.LogDebug("Deleting conversation {ConversationId}", conversationId);

        var removed = _conversations.TryRemove(conversationId, out _);

        if (removed)
        {
            _logger.LogInformation("Deleted conversation {ConversationId}", conversationId);
        }
        else
        {
            _logger.LogWarning("Cannot delete: conversation {ConversationId} not found", conversationId);
        }

        return Task.FromResult(removed);
    }

    /// <inheritdoc/>
    public Task DeleteAllConversationsAsync(string? userId = null)
    {
        _logger.LogDebug("Deleting all conversations for user {UserId}", userId ?? "all");

        if (string.IsNullOrWhiteSpace(userId))
        {
            // Delete all conversations
            var count = _conversations.Count;
            _conversations.Clear();
            _logger.LogInformation("Deleted all {Count} conversations", count);
        }
        else
        {
            // Delete only conversations for the specified user
            var keysToDelete = _conversations
                .Where(kvp => kvp.Value.UserId == userId)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in keysToDelete)
            {
                _conversations.TryRemove(key, out _);
            }

            _logger.LogInformation("Deleted {Count} conversations for user {UserId}", keysToDelete.Count, userId);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<bool> AddMessageAsync(string conversationId, ChatMessage message)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            _logger.LogWarning("AddMessageAsync called with null or empty conversationId");
            return Task.FromResult(false);
        }

        if (message == null)
        {
            _logger.LogWarning("AddMessageAsync called with null message");
            throw new ArgumentNullException(nameof(message));
        }

        _logger.LogDebug("Adding message to conversation {ConversationId}", conversationId);

        if (!_conversations.TryGetValue(conversationId, out var conversation))
        {
            _logger.LogWarning("Cannot add message: conversation {ConversationId} not found", conversationId);
            return Task.FromResult(false);
        }

        // Add the message and update the conversation timestamp
        conversation.Messages.Add(message);
        conversation.UpdatedAt = DateTimeOffset.UtcNow;

        _logger.LogInformation("Added message {MessageId} from {Role} to conversation {ConversationId}",
            message.Id, message.Role, conversationId);

        return Task.FromResult(true);
    }
}
