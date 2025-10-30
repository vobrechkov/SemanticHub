using Microsoft.AspNetCore.Mvc;
using SemanticHub.Api.Models;
using SemanticHub.Api.Services;

namespace SemanticHub.Api.Endpoints;

/// <summary>
/// API endpoints for conversation management
/// </summary>
public static class ConversationEndpoints
{
    /// <summary>
    /// Maps conversation-related endpoints
    /// </summary>
    public static IEndpointRouteBuilder MapConversationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/conversations")
            .WithTags("Conversations");

        group.MapGet("", ListConversationsAsync)
            .WithName("ListConversations")
            .WithSummary("List all conversations")
            .WithDescription("Retrieves a paginated list of conversations, ordered by most recently updated.");

        group.MapPost("", CreateConversationAsync)
            .WithName("CreateConversation")
            .WithSummary("Create a new conversation")
            .WithDescription("Creates a new conversation thread with an optional title.");

        group.MapGet("{id}", GetConversationAsync)
            .WithName("GetConversation")
            .WithSummary("Get a specific conversation")
            .WithDescription("Retrieves a conversation by ID including all its messages.");

        group.MapPut("{id}/title", UpdateConversationTitleAsync)
            .WithName("UpdateConversationTitle")
            .WithSummary("Update conversation title")
            .WithDescription("Updates the title of an existing conversation.");

        group.MapDelete("{id}", DeleteConversationAsync)
            .WithName("DeleteConversation")
            .WithSummary("Delete a conversation")
            .WithDescription("Deletes a specific conversation by ID.");

        group.MapDelete("", DeleteAllConversationsAsync)
            .WithName("DeleteAllConversations")
            .WithSummary("Delete all conversations")
            .WithDescription("Deletes all conversations, optionally filtered by user ID.");

        return endpoints;
    }

    /// <summary>
    /// Lists conversations with pagination
    /// </summary>
    private static async Task<IResult> ListConversationsAsync(
        [FromQuery] int offset,
        [FromQuery] int limit,
        [FromQuery] string? userId,
        IConversationStorageService storageService,
        ILogger<IConversationStorageService> logger)
    {
        try
        {
            // Apply defaults and validate pagination parameters
            offset = Math.Max(0, offset);
            limit = Math.Clamp(limit == 0 ? 50 : limit, 1, 100);

            logger.LogInformation("Listing conversations: offset={Offset}, limit={Limit}, userId={UserId}",
                offset, limit, userId ?? "none");

            var conversations = await storageService.ListConversationsAsync(offset, limit, userId);

            return Results.Ok(conversations);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing conversations");
            return Results.Problem(
                title: "Storage Error",
                detail: ex.Message,
                statusCode: 500);
        }
    }

    /// <summary>
    /// Creates a new conversation
    /// </summary>
    private static async Task<IResult> CreateConversationAsync(
        CreateConversationRequest request,
        IConversationStorageService storageService,
        ILogger<IConversationStorageService> logger)
    {
        try
        {
            logger.LogInformation("Creating conversation with title '{Title}' for user {UserId}",
                request.Title ?? "default", request.UserId ?? "anonymous");

            var conversation = await storageService.CreateConversationAsync(request.Title, request.UserId);

            return Results.Created($"/api/conversations/{conversation.Id}", conversation);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating conversation");
            return Results.Problem(
                title: "Storage Error",
                detail: ex.Message,
                statusCode: 500);
        }
    }

    /// <summary>
    /// Gets a specific conversation by ID
    /// </summary>
    private static async Task<IResult> GetConversationAsync(
        string id,
        IConversationStorageService storageService,
        ILogger<IConversationStorageService> logger)
    {
        try
        {
            logger.LogInformation("Retrieving conversation {ConversationId}", id);

            var conversation = await storageService.GetConversationAsync(id);

            if (conversation == null)
            {
                logger.LogWarning("Conversation {ConversationId} not found", id);
                return Results.NotFound(new { message = $"Conversation '{id}' not found" });
            }

            return Results.Ok(conversation);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving conversation {ConversationId}", id);
            return Results.Problem(
                title: "Storage Error",
                detail: ex.Message,
                statusCode: 500);
        }
    }

    /// <summary>
    /// Updates the title of a conversation
    /// </summary>
    private static async Task<IResult> UpdateConversationTitleAsync(
        string id,
        UpdateConversationTitleRequest request,
        IConversationStorageService storageService,
        ILogger<IConversationStorageService> logger)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Title))
            {
                logger.LogWarning("Update conversation title called with empty title");
                return Results.BadRequest(new { message = "Title cannot be empty" });
            }

            logger.LogInformation("Updating conversation {ConversationId} title to '{Title}'", id, request.Title);

            var conversation = await storageService.UpdateConversationTitleAsync(id, request.Title);

            if (conversation == null)
            {
                logger.LogWarning("Conversation {ConversationId} not found for title update", id);
                return Results.NotFound(new { message = $"Conversation '{id}' not found" });
            }

            return Results.Ok(conversation);
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex, "Invalid title provided for conversation {ConversationId}", id);
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating conversation {ConversationId} title", id);
            return Results.Problem(
                title: "Storage Error",
                detail: ex.Message,
                statusCode: 500);
        }
    }

    /// <summary>
    /// Deletes a specific conversation
    /// </summary>
    private static async Task<IResult> DeleteConversationAsync(
        string id,
        IConversationStorageService storageService,
        ILogger<IConversationStorageService> logger)
    {
        try
        {
            logger.LogInformation("Deleting conversation {ConversationId}", id);

            var deleted = await storageService.DeleteConversationAsync(id);

            if (!deleted)
            {
                logger.LogWarning("Conversation {ConversationId} not found for deletion", id);
                return Results.NotFound(new { message = $"Conversation '{id}' not found" });
            }

            return Results.Ok(new { message = $"Conversation '{id}' deleted successfully" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting conversation {ConversationId}", id);
            return Results.Problem(
                title: "Storage Error",
                detail: ex.Message,
                statusCode: 500);
        }
    }

    /// <summary>
    /// Deletes all conversations, optionally filtered by user ID
    /// </summary>
    private static async Task<IResult> DeleteAllConversationsAsync(
        [FromQuery] string? userId,
        IConversationStorageService storageService,
        ILogger<IConversationStorageService> logger)
    {
        try
        {
            logger.LogInformation("Deleting all conversations for user {UserId}", userId ?? "all");

            await storageService.DeleteAllConversationsAsync(userId);

            var message = string.IsNullOrWhiteSpace(userId)
                ? "All conversations deleted successfully"
                : $"All conversations for user '{userId}' deleted successfully";

            return Results.Ok(new { message });
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Invalid operation while deleting conversations");
            return Results.Problem(
                title: "Operation Error",
                detail: ex.Message,
                statusCode: 500);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogError(ex, "Access denied while deleting conversations");
            return Results.Problem(
                title: "Access Denied",
                detail: "Insufficient permissions to delete conversations",
                statusCode: 403);
        }
    }
}
