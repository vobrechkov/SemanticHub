using System.Collections.Concurrent;
using System.Diagnostics;
using SemanticHub.IngestionService.Configuration;
using SemanticHub.IngestionService.Domain.Aggregates;
using SemanticHub.IngestionService.Domain.Ports;
using SemanticHub.IngestionService.Domain.Results;
using SemanticHub.IngestionService.Domain.Workflows;
using SemanticHub.IngestionService.Diagnostics;
using SemanticHub.IngestionService.Models;

namespace SemanticHub.IngestionService.Application.Workflows;

/// <summary>
/// Coordinates batch ingestion of multiple web pages with concurrency control.
/// Titles are automatically inferred from page content.
/// </summary>
public sealed class BatchWebPageIngestionWorkflow(
    ILogger<BatchWebPageIngestionWorkflow> logger,
    IHtmlScraper htmlScraper,
    IHtmlProcessor htmlProcessor)
    : IIngestionWorkflow<BatchWebPageIngestion, BatchWebPageIngestionResult>
{
    public async Task<BatchWebPageIngestionResult> ExecuteAsync(
        BatchWebPageIngestion request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var activity = IngestionTelemetry.ActivitySource.StartActivity("Workflow.BatchWebPageIngestion");
        activity?.SetTag("ingestion.workflow", "batch-webpage");
        activity?.SetTag("ingestion.batch.urlCount", request.Urls.Count);
        activity?.SetTag("ingestion.batch.maxConcurrency", request.MaxConcurrency);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            logger.LogInformation(
                "Starting batch web page ingestion for {Count} URLs with max concurrency {MaxConcurrency}",
                request.Urls.Count,
                request.MaxConcurrency);

            var results = new ConcurrentBag<PageIngestionOutcome>();
            var semaphore = new SemaphoreSlim(request.MaxConcurrency);

            var tasks = request.Urls.Select(async url =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    var outcome = await IngestSinglePageAsync(url, request, cancellationToken);
                    results.Add(outcome);

                    // Throttle between requests
                    if (request.ThrottleMilliseconds > 0)
                    {
                        await Task.Delay(request.ThrottleMilliseconds, cancellationToken);
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);

            stopwatch.Stop();

            var resultsList = results.ToList();
            var succeeded = resultsList.Count(r => r.Success);
            var failed = resultsList.Count(r => !r.Success);

            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("ingestion.batch.succeeded", succeeded);
            activity?.SetTag("ingestion.batch.failed", failed);
            activity?.SetTag("ingestion.workflow.durationMs", stopwatch.Elapsed.TotalMilliseconds);

            logger.LogInformation(
                "Batch web page ingestion completed. Succeeded: {Succeeded}/{Total}, Failed: {Failed}",
                succeeded,
                request.Urls.Count,
                failed);

            return new BatchWebPageIngestionResult
            {
                Success = failed == 0,
                TotalRequested = request.Urls.Count,
                TotalSucceeded = succeeded,
                TotalFailed = failed,
                Results = resultsList,
                Duration = stopwatch.Elapsed,
                Message = $"Ingested {succeeded} of {request.Urls.Count} web pages."
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogError(ex, "Batch web page ingestion workflow failed");
            throw;
        }
    }

    private async Task<PageIngestionOutcome> IngestSinglePageAsync(
        Uri url,
        BatchWebPageIngestion batchRequest,
        CancellationToken cancellationToken)
    {
        try
        {
            var scrapedPage = await htmlScraper.ScrapeAsync(url, cancellationToken);

            if (!scrapedPage.IsSuccess || string.IsNullOrWhiteSpace(scrapedPage.HtmlContent))
            {
                logger.LogWarning("Failed to scrape {Url}. Status: {Status}", url, scrapedPage.StatusCode);
                return new PageIngestionOutcome
                {
                    Url = url,
                    Success = false,
                    ErrorMessage = $"Scrape failed with status {scrapedPage.StatusCode}"
                };
            }

            // Create ingestion request with no title (will be inferred)
            var webPageRequest = new WebPageIngestionRequest
            {
                Url = url.ToString(),
                DocumentId = null, // Auto-generate
                Title = null, // Infer from content
                Tags = batchRequest.Metadata.Tags.ToList(),
                Metadata = batchRequest.Metadata.CustomMetadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
            };

            var result = await htmlProcessor.IngestWebPageAsync(webPageRequest, scrapedPage, cancellationToken);

            IngestionTelemetry.WebPagesScraped.Add(1, new TagList
            {
                { "status", result.Success ? "success" : "failed" },
                { "sourceType", "batch-webpage" },
                { "host", url.Host }
            });

            return new PageIngestionOutcome
            {
                Url = url,
                Success = result.Success,
                Title = webPageRequest.Title,
                ChunksIndexed = result.ChunksIndexed,
                ErrorMessage = result.Success ? null : result.Message
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error ingesting page {Url}", url);
            return new PageIngestionOutcome
            {
                Url = url,
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }
}
