using System.Diagnostics;
using SemanticHub.IngestionService.Domain.Aggregates;
using SemanticHub.IngestionService.Domain.Ports;
using SemanticHub.IngestionService.Domain.Results;
using SemanticHub.IngestionService.Domain.Workflows;
using SemanticHub.IngestionService.Diagnostics;
using SemanticHub.IngestionService.Models;

namespace SemanticHub.IngestionService.Application.Workflows;

/// <summary>
/// Orchestrates ingestion of Markdown documents.
/// </summary>
public sealed class MarkdownIngestionWorkflow(
    ILogger<MarkdownIngestionWorkflow> logger,
    IMarkdownProcessor markdownProcessor) : IIngestionWorkflow<MarkdownDocumentIngestion>
{
    public async Task<IngestionOutcome> ExecuteAsync(
        MarkdownDocumentIngestion request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var activity = IngestionTelemetry.ActivitySource.StartActivity("Workflow.MarkdownIngestion");
        activity?.SetTag("ingestion.workflow", "markdown");
        activity?.SetTag("ingestion.documentId", request.Metadata.DocumentId ?? string.Empty);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var markdownRequest = BuildMarkdownRequest(request);
            var result = await markdownProcessor.IngestAsync(markdownRequest, cancellationToken);

            stopwatch.Stop();
            activity?.SetTag("ingestion.workflow.durationMs", stopwatch.Elapsed.TotalMilliseconds);
            activity?.SetStatus(result.Success ? ActivityStatusCode.Ok : ActivityStatusCode.Error);

            var diagnostics = BuildDiagnostics(markdownRequest, result, stopwatch.Elapsed);

            if (!result.Success)
            {
                logger.LogWarning(
                    "Markdown ingestion failed for document {DocumentId}: {Message}",
                    markdownRequest.DocumentId ?? markdownRequest.Title,
                    result.Message);

                var error = new IngestionError(
                    IngestionErrorCode.ProcessingFailed,
                    result.Message ?? "Markdown ingestion failed.",
                    null,
                    diagnostics);

                return IngestionOutcome.FromLegacyResult(result) with
                {
                    Diagnostics = diagnostics,
                    Error = error
                };
            }

            return IngestionOutcome.FromLegacyResult(result) with
            {
                Diagnostics = diagnostics,
                Document = CreateProcessedDocument(result, stopwatch.Elapsed, diagnostics)
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("ingestion.workflow.durationMs", stopwatch.Elapsed.TotalMilliseconds);

            logger.LogError(
                ex,
                "Markdown ingestion workflow failed for document {DocumentId}",
                request.Metadata.DocumentId ?? "<generated>");

            throw;
        }
    }

    private static MarkdownIngestionRequest BuildMarkdownRequest(MarkdownDocumentIngestion request)
    {
        var metadata = request.Metadata;
        if (request.Resource.Content is null)
        {
            throw new ArgumentException("Markdown content must be provided for ingestion.");
        }

        return new MarkdownIngestionRequest
        {
            DocumentId = metadata.DocumentId,
            Title = metadata.Title,
            SourceUrl = metadata.SourceUri?.ToString(),
            SourceType = metadata.SourceType,
            Tags = metadata.Tags.ToList(),
            Metadata = metadata.CustomMetadata.ToDictionary(k => k.Key, v => v.Value),
            Content = request.Resource.Content
        };
    }

    private static Dictionary<string, object> BuildDiagnostics(
        MarkdownIngestionRequest request,
        DocumentIngestionResult result,
        TimeSpan duration)
    {
        var diagnostics = new Dictionary<string, object>
        {
            ["documentId"] = result.DocumentId,
            ["indexName"] = result.IndexName ?? string.Empty,
            ["chunksIndexed"] = result.ChunksIndexed,
            ["durationMs"] = duration.TotalMilliseconds,
            ["sourceType"] = request.SourceType ?? "markdown"
        };

        if (!string.IsNullOrWhiteSpace(result.Message))
        {
            diagnostics["message"] = result.Message!;
        }

        return diagnostics;
    }

    private static ProcessedDocument CreateProcessedDocument(
        DocumentIngestionResult result,
        TimeSpan duration,
        IReadOnlyDictionary<string, object> diagnostics)
    {
        var metrics = new DocumentProcessingMetrics
        {
            Duration = duration,
            ChunkCount = result.ChunksIndexed,
            TokenCount = 0,
            AdditionalProperties = diagnostics
        };

        return new ProcessedDocument(
            result.DocumentId,
            result.IndexName ?? string.Empty,
            Array.Empty<DocumentChunk>(),
            metrics);
    }
}
