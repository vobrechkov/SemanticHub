using System.ComponentModel;
using SemanticHub.Api.Configuration;
using SemanticHub.Api.Memory;

namespace SemanticHub.Api.Tools;

/// <summary>
/// AI agent tools for interacting with the Azure AI Search backed knowledge base.
/// </summary>
public class KnowledgeBaseTools
{
    private readonly ILogger<KnowledgeBaseTools> _logger;
    private readonly IAzureSearchKnowledgeStore _knowledgeStore;
    private readonly AgentFrameworkOptions _options;

    public KnowledgeBaseTools(
        ILogger<KnowledgeBaseTools> logger,
        IAzureSearchKnowledgeStore knowledgeStore,
        AgentFrameworkOptions options)
    {
        _logger = logger;
        _knowledgeStore = knowledgeStore;
        _options = options;
    }

    /// <summary>
    /// Searches the knowledge base for relevant information.
    /// </summary>
    [Description("Search the knowledge base for information related to a query. Returns relevant documents and snippets.")]
    public async Task<string> SearchKnowledgeBase(
        [Description("The search query to find relevant information")] string query,
        [Description("Minimum relevance between 0.0 and 1.0, default comes from configuration")] double minRelevance = 0.0,
        [Description("Maximum number of results to return, default comes from configuration")] int limit = 0,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var effectiveLimit = limit > 0 ? limit : _options.Memory.AzureSearch.MaxResults;
            var effectiveMinRelevance = minRelevance > 0 ? minRelevance : _options.Memory.AzureSearch.MinRelevance;

            _logger.LogInformation("Searching Azure AI Search index with query: {Query}", query);

            var results = await _knowledgeStore.SearchAsync(
                query,
                effectiveLimit,
                effectiveMinRelevance,
                cancellationToken);

            if (results.Count == 0)
            {
                return "No relevant information found in the knowledge base.";
            }

            var resultText = new System.Text.StringBuilder();
            resultText.AppendLine($"Found {results.Count} relevant result(s):");
            resultText.AppendLine();

            for (var i = 0; i < results.Count; i++)
            {
                var result = results[i];
                resultText.AppendLine($"Result {i + 1} (Relevance: {result.NormalizedScore:P0})");
                if (!string.IsNullOrEmpty(result.Document.Title))
                {
                    resultText.AppendLine($"Title: {result.Document.Title}");
                }
                resultText.AppendLine($"Document ID: {result.Document.DocumentId}");
                resultText.AppendLine(result.Content);
                resultText.AppendLine();
            }

            return resultText.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching Azure AI Search knowledge base");
            return $"Error searching knowledge base: {ex.Message}";
        }
    }

    /// <summary>
    /// Retrieves the status (existence) of a document within the index.
    /// </summary>
    [Description("Check whether a document exists in the knowledge base and inspect its metadata.")]
    public async Task<string> GetDocumentStatus(
        [Description("The document ID to check")] string documentId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Checking Azure AI Search for document: {DocumentId}", documentId);

            var document = await _knowledgeStore.TryGetDocumentAsync(documentId, cancellationToken);

            if (document is null)
            {
                return $"Document {documentId} status: not found in index.";
            }

            var builder = new System.Text.StringBuilder();
            builder.AppendLine($"Document {documentId} status: indexed.");
            if (!string.IsNullOrEmpty(document.Title))
            {
                builder.AppendLine($"Title: {document.Title}");
            }

            if (!string.IsNullOrEmpty(document.Summary))
            {
                builder.AppendLine($"Summary: {document.Summary}");
            }

            return builder.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving document status from Azure AI Search");
            return $"Error retrieving document status: {ex.Message}";
        }
    }

    /// <summary>
    /// Lists documents currently stored in the index.
    /// </summary>
    [Description("List documents currently indexed in the knowledge base.")]
    public async Task<string> ListDocuments(
        [Description("Maximum number of documents to list")] int limit = 10,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var effectiveLimit = limit > 0 ? limit : _options.Memory.AzureSearch.MaxResults;
            _logger.LogInformation("Listing up to {Limit} documents from Azure AI Search", effectiveLimit);

            var documents = await _knowledgeStore.ListDocumentsAsync(effectiveLimit, cancellationToken);

            if (documents.Count == 0)
            {
                return "No documents currently indexed.";
            }

            var builder = new System.Text.StringBuilder();
            builder.AppendLine($"Found {documents.Count} document(s) in the knowledge base:");
            builder.AppendLine();

            foreach (var document in documents)
            {
                builder.AppendLine($"- {document.DocumentId}: {document.Title ?? "Untitled"}");
                if (!string.IsNullOrEmpty(document.Summary))
                {
                    builder.AppendLine($"  Summary: {document.Summary}");
                }
            }

            return builder.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing documents from Azure AI Search");
            return $"Error listing documents: {ex.Message}";
        }
    }
}
