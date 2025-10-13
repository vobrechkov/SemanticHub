using System.ComponentModel;
using SemanticHub.Api.Models;
using SemanticHub.Api.Services;

namespace SemanticHub.Api.Tools;

/// <summary>
/// Tools that allow agents to send content to the ingestion pipeline backed by Azure AI Search.
/// </summary>
public class IngestionTools
{
    private readonly IngestionClient _ingestionClient;
    private readonly ILogger<IngestionTools> _logger;

    public IngestionTools(
        IngestionClient ingestionClient,
        ILogger<IngestionTools> logger)
    {
        _ingestionClient = ingestionClient;
        _logger = logger;
    }

    /// <summary>
    /// Ingest Markdown content into the knowledge base.
    /// </summary>
    [Description("Send Markdown content to the knowledge ingestion pipeline so it becomes searchable by agents.")]
    public async Task<string> IngestMarkdownDocumentAsync(
        [Description("Markdown content to ingest. Can include YAML frontmatter for metadata.")] string content,
        [Description("Optional document identifier. If omitted a new identifier is generated.")] string? documentId = null,
        [Description("Optional title for the document.")] string? title = null,
        [Description("Optional source URL associated with the document.")] string? sourceUrl = null,
        [Description("Optional source type descriptor (e.g. 'manual', 'webpage', 'openapi').")] string? sourceType = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new MarkdownIngestionRequest
            {
                DocumentId = string.IsNullOrWhiteSpace(documentId) ? null : documentId,
                Title = title,
                SourceUrl = sourceUrl,
                SourceType = sourceType,
                Content = content
            };

            var response = await _ingestionClient.IngestMarkdownAsync(request, cancellationToken);
            if (response.Success)
            {
                return $"Document '{response.DocumentId}' ingested successfully with {response.ChunksIndexed} chunk(s).";
            }

            return $"Failed to ingest document: {response.ErrorMessage ?? "unknown error"}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ingest markdown content");
            return $"Error ingesting document: {ex.Message}";
        }
    }
}
