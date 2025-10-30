using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using SemanticHub.Api.Models;
using SemanticHub.Api.Services;
using SemanticHub.Api.Tools;
using System.Text.Json;

namespace SemanticHub.Api.Endpoints;

/// <summary>
/// API endpoints for Agent Framework interactions
/// </summary>
public static class AgentEndpoints
{
    /// <summary>
    /// Maps agent-related endpoints
    /// </summary>
    public static IEndpointRouteBuilder MapAgentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/agents")
            .WithTags("Agents");

        group.MapPost("/chat", HandleChatAsync)
            .WithName("AgentChat")
            .WithSummary("Send a message to an AI agent")
            .WithDescription("Sends a message to an AI agent and receives a response. Agents can autonomously call tools to search the knowledge base.");

        group.MapPost("/chat/stream", HandleStreamChatAsync)
            .WithName("AgentChatStream")
            .WithSummary("Stream a chat response from an AI agent")
            .WithDescription("Sends a message to an AI agent and streams the response in real-time.");

        group.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "agent-framework" }))
            .WithName("AgentHealth")
            .WithSummary("Check agent service health");

        return endpoints;
    }

    /// <summary>
    /// Handles non-streaming chat requests
    /// </summary>
    private static async Task<IResult> HandleChatAsync(
        AgentChatRequest request,
        AgentService agentService,
        KnowledgeBaseTools knowledgeTools,
        IngestionTools ingestionTools,
        ILogger<AgentService> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Processing chat request: {Message}", request.Message);

            // Create tool functions from KnowledgeBaseTools methods
            var tools = new[]
            {
                AIFunctionFactory.Create(knowledgeTools.SearchKnowledgeBase),
                AIFunctionFactory.Create(knowledgeTools.GetDocumentStatus),
                AIFunctionFactory.Create(knowledgeTools.ListDocuments),
                AIFunctionFactory.Create(ingestionTools.IngestMarkdownDocumentAsync)
            };

            // Create agent with tools
            var agent = string.IsNullOrEmpty(request.CustomInstructions)
                ? agentService.CreateDefaultAgent(tools)
                : agentService.CreateAgent(request.CustomInstructions, tools: tools);

            logger.LogInformation("Agent created with {ToolCount} tools", tools.Length);

            // Run the agent - framework handles conversation context internally
            var response = await agent.RunAsync(request.Message);

            // Extract response text
            var responseMessage = response.ToString() ?? "No response generated";

            return Results.Ok(new AgentChatResponse
            {
                Message = responseMessage,
                ThreadId = Guid.NewGuid().ToString(), // Generate unique ID for this interaction
                Metadata = new Dictionary<string, object>
                {
                    ["userId"] = request.UserId ?? "anonymous",
                    ["toolsAvailable"] = tools.Length,
                    ["timestamp"] = DateTime.UtcNow
                }
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing chat request");
            return Results.Problem(
                title: "Agent Error",
                detail: ex.Message,
                statusCode: 500);
        }
    }

    /// <summary>
    /// Handles streaming chat requests with citation support
    /// </summary>
    private static async Task HandleStreamChatAsync(
        HttpContext httpContext,
        AgentChatRequest request,
        AgentService agentService,
        KnowledgeBaseTools knowledgeTools,
        IngestionTools ingestionTools,
        ILogger<AgentService> logger,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Processing streaming chat request: {Message}", request.Message);

        var messageId = Guid.NewGuid().ToString();
        var conversationId = request.ConversationId ?? request.ThreadId;
        var citationsSent = false;

        var response = httpContext.Response;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Append("X-Accel-Buffering", "no"); // Disable response buffering for proxies

        await response.StartAsync(cancellationToken);

        // Create tool functions from KnowledgeBaseTools methods
        var tools = new[]
        {
            AIFunctionFactory.Create(knowledgeTools.SearchKnowledgeBase),
            AIFunctionFactory.Create(knowledgeTools.GetDocumentStatus),
            AIFunctionFactory.Create(knowledgeTools.ListDocuments),
            AIFunctionFactory.Create(ingestionTools.IngestMarkdownDocumentAsync)
        };

        // Create agent with tools
        var agent = string.IsNullOrEmpty(request.CustomInstructions)
            ? agentService.CreateDefaultAgent(tools)
            : agentService.CreateAgent(request.CustomInstructions, tools: tools);

        logger.LogInformation("Agent created for streaming with {ToolCount} tools", tools.Length);

        // Stream the response - framework handles conversation context internally
        await foreach (var update in agent.RunStreamingAsync(request.Message))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (update != null)
            {
                var updateText = update.ToString();
                if (!string.IsNullOrEmpty(updateText))
                {
                    // Check if we have search results and haven't sent citations yet
                    if (!citationsSent && knowledgeTools.LatestSearchResults != null && knowledgeTools.LatestSearchResults.Count > 0)
                    {
                        var citations = ExtractCitations(knowledgeTools.LatestSearchResults);

                        var chunkWithCitations = new StreamedChatChunk
                        {
                            MessageId = messageId,
                            ConversationId = conversationId,
                            Content = updateText,
                            Role = "assistant",
                            Citations = citations,
                            IsComplete = false
                        };

                        await WriteSseAsync(response, chunkWithCitations, cancellationToken);
                        citationsSent = true;
                    }
                    else
                    {
                        var chunk = new StreamedChatChunk
                        {
                            MessageId = messageId,
                            ConversationId = conversationId,
                            Content = updateText,
                            Role = "assistant",
                            IsComplete = false
                        };

                        await WriteSseAsync(response, chunk, cancellationToken);
                    }
                }
            }
        }

        // Send final chunk to indicate completion
        var finalChunk = new StreamedChatChunk
        {
            MessageId = messageId,
            ConversationId = conversationId,
            Content = "",
            Role = "assistant",
            IsComplete = true
        };

        await WriteSseAsync(response, finalChunk, cancellationToken);

        // Ensure the client receives the final chunk promptly
        await response.Body.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Writes a chat chunk to the response stream as SSE
    /// </summary>
    private static async Task WriteSseAsync(HttpResponse response, StreamedChatChunk chunk, CancellationToken cancellationToken)
    {
        var message = FormatSseMessage(chunk);
        await response.WriteAsync(message, cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Formats a chat chunk as an SSE message payload
    /// </summary>
    private static string FormatSseMessage(StreamedChatChunk chunk)
    {
        var json = JsonSerializer.Serialize(chunk, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
        return $"data: {json}\n\n";
    }

    /// <summary>
    /// Extracts citations from knowledge base search results
    /// </summary>
    private static List<CitationInfo> ExtractCitations(IReadOnlyList<Memory.KnowledgeRecord> searchResults)
    {
        var citations = new List<CitationInfo>();

        for (var i = 0; i < searchResults.Count; i++)
        {
            var result = searchResults[i];

            // Extract URL and file path from metadata if available
            result.Metadata.TryGetValue("url", out var urlObj);
            result.Metadata.TryGetValue("file_path", out var filePathObj);
            result.Metadata.TryGetValue("chunk_id", out var chunkIdObj);

            var citation = new CitationInfo
            {
                PartIndex = i + 1,
                Content = result.Content,
                Id = result.Document.DocumentId,
                Title = result.Document.Title,
                FilePath = filePathObj?.ToString(),
                Url = urlObj?.ToString(),
                ChunkId = chunkIdObj?.ToString(),
                Score = result.NormalizedScore
            };

            citations.Add(citation);
        }

        return citations;
    }
}
