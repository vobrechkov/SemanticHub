using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using SemanticHub.Api.Configuration;

namespace SemanticHub.Api.Memory;

/// <summary>
/// Injects knowledge store grounding context prior to agent invocation.
/// </summary>
public sealed class KnowledgeStoreContextProvider(
    ILogger<KnowledgeStoreContextProvider> logger,
    IKnowledgeStore knowledgeStore,
    AgentFrameworkOptions options)
    : AIContextProvider
{
    private readonly ILogger<KnowledgeStoreContextProvider> _logger = logger;
    private readonly IKnowledgeStore _knowledgeStore = knowledgeStore;
    private readonly AgentFrameworkOptions _options = options;

    public override async ValueTask<AIContext> InvokingAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        var latestMessage = context.RequestMessages.LastOrDefault(m => m.Role == ChatRole.User)?.Text;
        if (string.IsNullOrWhiteSpace(latestMessage))
        {
            return new AIContext();
        }

        var maxResults = _options.Memory.MaxResults;
        var minRelevance = _options.Memory.MinRelevance;

        try
        {
            _logger.LogDebug("Retrieving knowledge store grounding context for query: {Query}", latestMessage);

            var results = await _knowledgeStore.SearchAsync(
                latestMessage,
                maxResults,
                minRelevance,
                cancellationToken);

            if (results.Count == 0)
            {
                return new AIContext();
            }

            var contextBuilder = new System.Text.StringBuilder();
            contextBuilder.AppendLine("Relevant knowledge base documents:");
            contextBuilder.AppendLine();

            for (var i = 0; i < results.Count; i++)
            {
                var result = results[i];
                contextBuilder.AppendLine($"Document {i}: {result.Document.DocumentId}");
                if (!string.IsNullOrEmpty(result.Document.Title))
                {
                    contextBuilder.AppendLine($"Title: {result.Document.Title}");
                }
                contextBuilder.AppendLine($"Relevance: {result.NormalizedScore:P0}");
                contextBuilder.AppendLine(result.Content);
                contextBuilder.AppendLine();
            }

            return new AIContext
            {
                Instructions = contextBuilder.ToString()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve context from knowledge store");
            return new AIContext();
        }
    }
}
