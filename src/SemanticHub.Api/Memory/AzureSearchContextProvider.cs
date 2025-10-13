using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using SemanticHub.Api.Configuration;

namespace SemanticHub.Api.Memory;

/// <summary>
/// Injects Azure AI Search grounding context prior to agent invocation.
/// </summary>
public sealed class AzureSearchContextProvider(
    ILogger<AzureSearchContextProvider> logger,
    IAzureSearchKnowledgeStore knowledgeStore,
    AgentFrameworkOptions options)
    : AIContextProvider
{
    private readonly ILogger<AzureSearchContextProvider> _logger = logger;
    private readonly IAzureSearchKnowledgeStore _knowledgeStore = knowledgeStore;
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

        var maxResults = _options.Memory.AzureSearch.MaxResults;
        var minRelevance = _options.Memory.AzureSearch.MinRelevance;

        try
        {
            _logger.LogDebug("Retrieving Azure AI Search grounding context for query: {Query}", latestMessage);

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
                contextBuilder.AppendLine($"Document {i + 1}: {result.Document.DocumentId}");
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
            _logger.LogError(ex, "Failed to retrieve context from Azure AI Search");
            return new AIContext();
        }
    }
}
