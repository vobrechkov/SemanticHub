using SemanticHub.IngestionService.Domain.Results;
using SemanticHub.IngestionService.Models;

namespace SemanticHub.IngestionService.Domain.Ports;

/// <summary>
/// Processes HTML content and delegates Markdown ingestion.
/// </summary>
public interface IHtmlProcessor
{
    Task<DocumentIngestionResult> IngestHtmlAsync(
        HtmlIngestionRequest request,
        CancellationToken cancellationToken = default);

    Task<DocumentIngestionResult> IngestWebPageAsync(
        WebPageIngestionRequest request,
        ScrapedPage scrapedPage,
        CancellationToken cancellationToken = default);
}
