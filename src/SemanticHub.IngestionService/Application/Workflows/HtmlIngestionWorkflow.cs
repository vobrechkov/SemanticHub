using System.Diagnostics;
using SemanticHub.IngestionService.Domain.Aggregates;
using SemanticHub.IngestionService.Domain.Ports;
using SemanticHub.IngestionService.Domain.Results;
using SemanticHub.IngestionService.Domain.Workflows;
using SemanticHub.IngestionService.Diagnostics;
using SemanticHub.IngestionService.Models;

namespace SemanticHub.IngestionService.Application.Workflows;

/// <summary>
/// Coordinates ingestion of HTML documents by delegating to the HTML processor.
/// </summary>
public sealed class HtmlIngestionWorkflow(
    ILogger<HtmlIngestionWorkflow> logger,
    IHtmlProcessor htmlProcessor) : IIngestionWorkflow<HtmlDocumentIngestion>
{
    public async Task<IngestionOutcome> ExecuteAsync(
        HtmlDocumentIngestion request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var activity = IngestionTelemetry.ActivitySource.StartActivity("Workflow.HtmlIngestion");
        activity?.SetTag("ingestion.workflow", "html");
        activity?.SetTag("ingestion.documentId", request.Metadata.DocumentId ?? string.Empty);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var htmlRequest = BuildHtmlRequest(request);
            var result = await htmlProcessor.IngestHtmlAsync(htmlRequest, cancellationToken);

            stopwatch.Stop();
            activity?.SetTag("ingestion.workflow.durationMs", stopwatch.Elapsed.TotalMilliseconds);
            activity?.SetStatus(result.Success ? ActivityStatusCode.Ok : ActivityStatusCode.Error);

            var diagnostics = BuildDiagnostics(htmlRequest, result, stopwatch.Elapsed);

            if (!result.Success)
            {
                logger.LogWarning(
                    "HTML ingestion failed for document {DocumentId}: {Message}",
                    htmlRequest.DocumentId ?? htmlRequest.Title,
                    result.Message);

                var error = new IngestionError(
                    IngestionErrorCode.ProcessingFailed,
                    result.Message ?? "HTML ingestion failed.",
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
                Diagnostics = diagnostics
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("ingestion.workflow.durationMs", stopwatch.Elapsed.TotalMilliseconds);

            logger.LogError(
                ex,
                "HTML ingestion workflow failed for document {DocumentId}",
                request.Metadata.DocumentId ?? "<generated>");

            throw;
        }
    }

    private static HtmlIngestionRequest BuildHtmlRequest(HtmlDocumentIngestion request)
    {
        if (request.Resource.Content is null)
        {
            throw new ArgumentException("HTML content must be provided for ingestion.");
        }

        var metadata = request.Metadata;
        return new HtmlIngestionRequest
        {
            DocumentId = metadata.DocumentId,
            Title = metadata.Title,
            SourceUrl = metadata.SourceUri?.ToString(),
            Tags = metadata.Tags.ToList(),
            Metadata = metadata.CustomMetadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            Content = request.Resource.Content
        };
    }

    private static Dictionary<string, object> BuildDiagnostics(
        HtmlIngestionRequest request,
        DocumentIngestionResult result,
        TimeSpan duration)
    {
        var diagnostics = new Dictionary<string, object>
        {
            ["documentId"] = result.DocumentId,
            ["sourceUrl"] = request.SourceUrl ?? string.Empty,
            ["chunksIndexed"] = result.ChunksIndexed,
            ["durationMs"] = duration.TotalMilliseconds
        };

        if (!string.IsNullOrWhiteSpace(result.Message))
        {
            diagnostics["message"] = result.Message!;
        }

        return diagnostics;
    }
}
