using SemanticHub.IngestionService.Domain.Results;
using SemanticHub.IngestionService.Models;

namespace SemanticHub.IngestionService.Domain.Ports;

/// <summary>
/// Processes Markdown documents through chunking, embedding, and indexing.
/// </summary>
public interface IMarkdownProcessor
{
    Task<DocumentIngestionResult> IngestAsync(
        MarkdownIngestionRequest request,
        CancellationToken cancellationToken = default);
}
