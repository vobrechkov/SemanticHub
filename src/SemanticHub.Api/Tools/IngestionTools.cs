using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using SemanticHub.Api.Models;
using SemanticHub.Api.Services;

namespace SemanticHub.Api.Tools;

/// <summary>
/// Tools that allow agents to send content to the ingestion pipeline backed by Azure AI Search.
/// </summary>
public class IngestionTools(
    IngestionClient ingestionClient,
    ILogger<IngestionTools> logger)
{

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

            var response = await ingestionClient.IngestMarkdownAsync(request, cancellationToken);
            if (response.Success)
            {
                return $"Document '{response.DocumentId}' ingested successfully with {response.ChunksIndexed} chunk(s).";
            }

            return $"Failed to ingest document: {response.ErrorMessage ?? "unknown error"}";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to ingest markdown content");
            return $"Error ingesting document: {ex.Message}";
        }
    }

    /// <summary>
    /// Scrape a web page and ingest its content into the knowledge base.
    /// </summary>
    [Description("Scrape a web page and ingest its content into the knowledge base.")]
    public async Task<string> IngestWebPageAsync(
        [Description("URL of the page to scrape and ingest.")] string url,
        [Description("Optional document identifier. If omitted a new identifier is generated.")] string? documentId = null,
        [Description("Optional override title for the document.")] string? title = null,
        [Description("Optional tags to apply to the document.")] List<string>? tags = null,
        [Description("Optional metadata to attach to the document.")] Dictionary<string, object>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "URL must not be empty.";
        }

        try
        {
            var request = new WebPageIngestionRequest
            {
                Url = url,
                DocumentId = string.IsNullOrWhiteSpace(documentId) ? null : documentId,
                Title = title,
                Tags = tags?.Where(t => !string.IsNullOrWhiteSpace(t))
                    .Select(t => t.Trim())
                    .Where(t => t.Length > 0)
                    .ToList(),
                Metadata = metadata
            };

            var response = await ingestionClient.IngestWebPageAsync(request, cancellationToken);
            if (response.Success)
            {
                return $"Web page '{response.DocumentId}' ingested successfully with {response.ChunksIndexed} chunk(s).";
            }

            return $"Failed to ingest web page: {response.ErrorMessage ?? "unknown error"}";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to ingest web page {Url}", url);
            return $"Error ingesting web page: {ex.Message}";
        }
    }
}
