using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using SemanticHub.IngestionService.Domain.Aggregates;
using SemanticHub.IngestionService.Domain.Ports;
using SemanticHub.IngestionService.Domain.Results;
using SemanticHub.IngestionService.Domain.Workflows;
using SemanticHub.IngestionService.Diagnostics;
using SemanticHub.IngestionService.Models;

namespace SemanticHub.IngestionService.Application.Workflows;

/// <summary>
/// Orchestrates web page scraping and ingestion.
/// </summary>
public sealed class WebPageIngestionWorkflow(
    ILogger<WebPageIngestionWorkflow> logger,
    IHtmlScraper htmlScraper,
    IHtmlProcessor htmlProcessor) : IIngestionWorkflow<WebPageIngestion>
{
    public async Task<IngestionOutcome> ExecuteAsync(
        WebPageIngestion request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var activity = IngestionTelemetry.ActivitySource.StartActivity("Workflow.WebPageIngestion");
        activity?.SetTag("ingestion.workflow", "webpage");
        activity?.SetTag("ingestion.request.url", request.Url.ToString());

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var webRequest = BuildWebPageRequest(request);
            var scrapedPage = await htmlScraper.ScrapeAsync(request.Url, cancellationToken);

            var scrapeTags = CreateScrapeTags(request.Url, scrapedPage);
            IngestionTelemetry.WebPagesScraped.Add(1, scrapeTags);

            if (!scrapedPage.IsSuccess || string.IsNullOrWhiteSpace(scrapedPage.HtmlContent))
            {
                stopwatch.Stop();
                activity?.SetStatus(ActivityStatusCode.Error, "ScrapeFailed");

                var failedResult = BuildFailedResult(request, scrapedPage);
                var diagnostics = BuildScrapeDiagnostics(scrapedPage, stopwatch.Elapsed);

                logger.LogWarning(
                    "Failed to scrape web page {Url}. Status {StatusCode}",
                    request.Url,
                    scrapedPage.StatusCode);

                return IngestionOutcome.FromLegacyResult(failedResult) with
                {
                    Diagnostics = diagnostics,
                    Error = new IngestionError(
                        IngestionErrorCode.ScrapeFailed,
                        failedResult.Message ?? "Web page scrape failed.",
                        null,
                        diagnostics)
                };
            }

            var ingestionStopwatch = Stopwatch.StartNew();

            var result = await htmlProcessor.IngestWebPageAsync(webRequest, scrapedPage, cancellationToken);

            ingestionStopwatch.Stop();
            stopwatch.Stop();

            var ingestionDiagnostics = BuildIngestionDiagnostics(scrapedPage, result, ingestionStopwatch.Elapsed, stopwatch.Elapsed);
            activity?.SetStatus(result.Success ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
            activity?.SetTag("ingestion.workflow.durationMs", stopwatch.Elapsed.TotalMilliseconds);

            if (!result.Success)
            {
                logger.LogWarning(
                    "Web page ingestion failed for {Url}: {Message}",
                    request.Url,
                    result.Message);

                return IngestionOutcome.FromLegacyResult(result) with
                {
                    Diagnostics = ingestionDiagnostics,
                    Error = new IngestionError(
                        IngestionErrorCode.ProcessingFailed,
                        result.Message ?? "Web page ingestion failed.",
                        null,
                        ingestionDiagnostics)
                };
            }

            return IngestionOutcome.FromLegacyResult(result) with
            {
                Diagnostics = ingestionDiagnostics
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("ingestion.workflow.durationMs", stopwatch.Elapsed.TotalMilliseconds);

            logger.LogError(ex, "Web page ingestion workflow failed for {Url}", request.Url);
            throw;
        }
    }

    private static WebPageIngestionRequest BuildWebPageRequest(WebPageIngestion request)
    {
        var metadata = request.Metadata;
        return new WebPageIngestionRequest
        {
            Url = request.Url.ToString(),
            DocumentId = metadata.DocumentId,
            Title = request.TitleOverride ?? metadata.Title,
            Tags = metadata.Tags.ToList(),
            Metadata = metadata.CustomMetadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
        };
    }

    private static TagList CreateScrapeTags(Uri url, ScrapedPage scrapedPage)
    {
        var tagList = new TagList
        {
            { "status", scrapedPage.IsSuccess ? "success" : "failed" },
            { "sourceType", "webpage" },
            { "host", url.Host }
        };

        return tagList;
    }

    private static Dictionary<string, object> BuildScrapeDiagnostics(ScrapedPage scrapedPage, TimeSpan duration)
    {
        return new Dictionary<string, object>
        {
            ["durationMs"] = duration.TotalMilliseconds,
            ["statusCode"] = scrapedPage.StatusCode,
            ["url"] = scrapedPage.Url,
            ["hasContent"] = !string.IsNullOrWhiteSpace(scrapedPage.HtmlContent)
        };
    }

    private static Dictionary<string, object> BuildIngestionDiagnostics(
        ScrapedPage scrapedPage,
        DocumentIngestionResult result,
        TimeSpan ingestionDuration,
        TimeSpan totalDuration)
    {
        var diagnostics = new Dictionary<string, object>
        {
            ["url"] = scrapedPage.Url,
            ["chunksIndexed"] = result.ChunksIndexed,
            ["ingestionDurationMs"] = ingestionDuration.TotalMilliseconds,
            ["totalDurationMs"] = totalDuration.TotalMilliseconds,
            ["statusCode"] = scrapedPage.StatusCode
        };

        if (!string.IsNullOrWhiteSpace(result.Message))
        {
            diagnostics["message"] = result.Message!;
        }

        return diagnostics;
    }

    private static DocumentIngestionResult BuildFailedResult(WebPageIngestion request, ScrapedPage scrapedPage)
    {
        return new DocumentIngestionResult
        {
            Success = false,
            DocumentId = request.Metadata.DocumentId ?? request.Url.ToString(),
            IndexName = null,
            ChunksIndexed = 0,
            Message = $"Failed to scrape content from '{request.Url}'. Status {scrapedPage.StatusCode}"
        };
    }
}
