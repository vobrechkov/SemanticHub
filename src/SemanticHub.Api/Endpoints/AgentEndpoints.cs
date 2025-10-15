using Microsoft.Extensions.AI;
using SemanticHub.Api.Models;
using SemanticHub.Api.Services;
using SemanticHub.Api.Tools;
using System.Runtime.CompilerServices;

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
            .WithTags("Agents")
            .WithOpenApi();

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
    /// Handles streaming chat requests
    /// </summary>
    private static async IAsyncEnumerable<string> HandleStreamChatAsync(
        AgentChatRequest request,
        AgentService agentService,
        KnowledgeBaseTools knowledgeTools,
        IngestionTools ingestionTools,
        ILogger<AgentService> logger,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        logger.LogInformation("Processing streaming chat request: {Message}", request.Message);

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
            if (update != null)
            {
                var updateText = update.ToString();
                if (!string.IsNullOrEmpty(updateText))
                {
                    yield return updateText;
                }
            }
        }
    }
}
