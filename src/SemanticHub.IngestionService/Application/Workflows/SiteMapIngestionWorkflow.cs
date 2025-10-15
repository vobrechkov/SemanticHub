using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using SemanticHub.IngestionService.Configuration;
using SemanticHub.IngestionService.Domain.Aggregates;
using SemanticHub.IngestionService.Domain.Ports;
using SemanticHub.IngestionService.Domain.Results;
using SemanticHub.IngestionService.Domain.Sitemaps;
using SemanticHub.IngestionService.Domain.Workflows;
using SemanticHub.IngestionService.Diagnostics;
using SemanticHub.IngestionService.Models;

namespace SemanticHub.IngestionService.Application.Workflows;

/// <summary>
/// Coordinates sitemap traversal and ingestion of discovered pages.
/// </summary>
public sealed class SiteMapIngestionWorkflow(
    ILogger<SiteMapIngestionWorkflow> logger,
    ISitemapFetcher sitemapFetcher,
    ISitemapParser sitemapParser,
    IUrlFilterPolicy urlFilterPolicy,
    IChangeFrequencyHeuristic changeFrequencyHeuristic,
    IHtmlScraper htmlScraper,
    IHtmlProcessor htmlProcessor,
    IngestionOptions options)
    : IIngestionWorkflow<SitemapIngestion, SitemapIngestionResult>
{
    private readonly SitemapIngestionOptions _sitemapOptions = options.Sitemap;

    public async Task<SitemapIngestionResult> ExecuteAsync(
        SitemapIngestion request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var activity = IngestionTelemetry.ActivitySource.StartActivity("Workflow.SitemapIngestion");
        activity?.SetTag("ingestion.workflow", "sitemap");
        activity?.SetTag("ingestion.request.sitemap", request.SitemapUri.AbsoluteUri);

        var stopwatch = Stopwatch.StartNew();
        var context = new SitemapIngestionContext(
            request.SitemapUri,
            request.Settings,
            _sitemapOptions,
            request.Metadata);

        try
        {
            var discoveredEntries = await DiscoverEntriesAsync(request, context, cancellationToken);
            var dedupedAndFiltered = await FilterAndScoreAsync(discoveredEntries, context, cancellationToken);
            var maxPages = request.Settings.MaxPages ?? _sitemapOptions.MaxPages;
            var selectedEntries = dedupedAndFiltered
                .OrderByDescending(entry => entry.HeuristicScore)
                .ThenByDescending(entry => entry.LastModified ?? DateTimeOffset.MinValue)
                .Take(maxPages)
                .ToList();

            var ingestionResult = await ProcessEntriesAsync(request, context, selectedEntries, cancellationToken);

            stopwatch.Stop();
            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("ingestion.workflow.durationMs", stopwatch.Elapsed.TotalMilliseconds);

            ingestionResult.TotalDiscovered = discoveredEntries.Count;
            ingestionResult.TotalFiltered = discoveredEntries.Count - selectedEntries.Count;
            ingestionResult.Duration = stopwatch.Elapsed;
            ingestionResult.Message = $"Ingested {ingestionResult.TotalIngested} of {selectedEntries.Count} sitemap URLs.";

            logger.LogInformation(
                "Sitemap ingestion completed for {Sitemap}. Ingested {Ingested}/{Total} URLs.",
                request.SitemapUri,
                ingestionResult.TotalIngested,
                selectedEntries.Count);

            return ingestionResult;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogError(ex, "Sitemap ingestion workflow failed for {Sitemap}", request.SitemapUri);
            throw;
        }
    }

    private async Task<IReadOnlyList<SitemapEntry>> DiscoverEntriesAsync(
        SitemapIngestion request,
        SitemapIngestionContext context,
        CancellationToken cancellationToken)
    {
        var queue = new Queue<(Uri Uri, int Depth)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<SitemapEntry>();
        var maxDepth = Math.Max(0, request.Settings.MaxDepth ?? _sitemapOptions.MaxDepth);

        queue.Enqueue((request.SitemapUri, 0));

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (currentUri, depth) = queue.Dequeue();
            if (!visited.Add(currentUri.AbsoluteUri))
            {
                continue;
            }

            using var activity = IngestionTelemetry.ActivitySource.StartActivity("Sitemap.FetchDocument");
            activity?.SetTag("ingestion.sitemap.depth", depth);
            activity?.SetTag("ingestion.sitemap.url", currentUri.AbsoluteUri);

            var fetchResult = await sitemapFetcher.FetchAsync(currentUri, cancellationToken);
            if (!fetchResult.Success || fetchResult.Document is null)
            {
                activity?.SetStatus(ActivityStatusCode.Error, fetchResult.Error);
                logger.LogWarning(
                    "Skipping sitemap {Sitemap} because it could not be fetched. Reason: {Reason}",
                    currentUri,
                    fetchResult.Error);
                continue;
            }

            var parseResult = sitemapParser.Parse(currentUri, fetchResult.Document.Content);
            if (parseResult.Entries.Count > 0)
            {
                entries.AddRange(parseResult.Entries);
                IngestionTelemetry.SitemapUrlsDiscovered.Add(parseResult.Entries.Count, new TagList
                {
                    { "host", currentUri.Host },
                    { "depth", depth }
                });
            }

            if (parseResult.ChildSitemaps.Count > 0 && depth < maxDepth)
            {
                foreach (var child in parseResult.ChildSitemaps)
                {
                    if (child is null)
                    {
                        continue;
                    }

                    queue.Enqueue((child, depth + 1));
                }
            }
        }

        return entries;
    }

    private async Task<IReadOnlyList<SitemapEntry>> FilterAndScoreAsync(
        IReadOnlyList<SitemapEntry> entries,
        SitemapIngestionContext context,
        CancellationToken cancellationToken)
    {
        var deduped = new List<SitemapEntry>(entries.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var absoluteUri = entry.Location.AbsoluteUri;
            if (!seen.Add(absoluteUri))
            {
                continue;
            }

            if (!await urlFilterPolicy.ShouldIncludeAsync(entry, context, cancellationToken))
            {
                continue;
            }

            var score = changeFrequencyHeuristic.CalculateScore(entry, context);
            deduped.Add(entry with { HeuristicScore = score });
        }

        return deduped;
    }

    private async Task<SitemapIngestionResult> ProcessEntriesAsync(
        SitemapIngestion request,
        SitemapIngestionContext context,
        IReadOnlyList<SitemapEntry> entries,
        CancellationToken cancellationToken)
    {
        var concurrency = Math.Max(1, _sitemapOptions.MaxConcurrency);
        var throttleDelayMs = request.Settings.ThrottleMilliseconds ?? _sitemapOptions.ThrottleMilliseconds;
        var throttleDelay = throttleDelayMs > 0 ? TimeSpan.FromMilliseconds(throttleDelayMs) : TimeSpan.Zero;

        var semaphore = new SemaphoreSlim(concurrency);
        var errors = new ConcurrentBag<string>();
        var totalIngested = 0;
        var totalFailed = 0;
        var tasks = new List<Task>(entries.Count);

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            tasks.Add(ProcessEntryAsync(entry));
        }

        await Task.WhenAll(tasks);

        return new SitemapIngestionResult
        {
            SitemapUrl = request.SitemapUri.AbsoluteUri,
            TotalIngested = totalIngested,
            TotalFailed = totalFailed,
            Errors = errors.ToList()
        };

        async Task ProcessEntryAsync(SitemapEntry entry)
        {
            await semaphore.WaitAsync(cancellationToken);
            using var activity = IngestionTelemetry.ActivitySource.StartActivity("Sitemap.ProcessUrl");
            activity?.SetTag("ingestion.sitemap.page", entry.Location.AbsoluteUri);

            var tagList = new TagList
            {
                { "host", entry.Location.Host }
            };

            try
            {
                var scrapedPage = await htmlScraper.ScrapeAsync(entry.Location, cancellationToken);
                if (!scrapedPage.IsSuccess || string.IsNullOrWhiteSpace(scrapedPage.HtmlContent))
                {
                    Interlocked.Increment(ref totalFailed);
                    tagList.Add("status", "scrape_failed");
                    IngestionTelemetry.SitemapUrlFailures.Add(1, tagList);
                    errors.Add($"Scrape failed for {entry.Location} (status {scrapedPage.StatusCode})");

                    logger.LogWarning(
                        "Scraping failed for sitemap URL {Url} with status {Status}",
                        entry.Location,
                        scrapedPage.StatusCode);

                    activity?.SetStatus(ActivityStatusCode.Error, "ScrapeFailed");
                    return;
                }

                var webPageRequest = BuildWebPageRequest(request, entry, scrapedPage);
                var result = await htmlProcessor.IngestWebPageAsync(webPageRequest, scrapedPage, cancellationToken);

                if (result.Success)
                {
                    Interlocked.Increment(ref totalIngested);
                    tagList.Add("status", "success");
                    IngestionTelemetry.SitemapUrlsIngested.Add(1, tagList);
                    activity?.SetStatus(ActivityStatusCode.Ok);
                }
                else
                {
                    Interlocked.Increment(ref totalFailed);
                    tagList.Add("status", "processing_failed");
                    IngestionTelemetry.SitemapUrlFailures.Add(1, tagList);
                    errors.Add($"Ingestion failed for {entry.Location}: {result.Message ?? "Unknown error"}");

                    logger.LogWarning(
                        "Ingestion failed for sitemap URL {Url}: {Message}",
                        entry.Location,
                        result.Message);

                    activity?.SetStatus(ActivityStatusCode.Error, result.Message);
                }
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref totalFailed);
                tagList.Add("status", "exception");
                IngestionTelemetry.SitemapUrlFailures.Add(1, tagList);
                errors.Add($"Exception processing {entry.Location}: {ex.Message}");

                logger.LogError(ex, "Error ingesting sitemap URL {Url}", entry.Location);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            }
            finally
            {
                semaphore.Release();

                if (throttleDelay > TimeSpan.Zero)
                {
                    try
                    {
                        await Task.Delay(throttleDelay, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                    }
                }
            }
        }
    }

    private static WebPageIngestionRequest BuildWebPageRequest(
        SitemapIngestion request,
        SitemapEntry entry,
        ScrapedPage scrapedPage)
    {
        var metadata = request.Metadata;
        var documentId = ComputeDocumentId(entry.Location, metadata.DocumentId);

        var tags = metadata.Tags.ToList();
        if (!string.IsNullOrWhiteSpace(entry.ChangeFrequency))
        {
            var tagValue = $"changefreq:{entry.ChangeFrequency}";
            if (!tags.Contains(tagValue, StringComparer.OrdinalIgnoreCase))
            {
                tags.Add(tagValue);
            }
        }

        var metadataDictionary = metadata.CustomMetadata.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value);

        metadataDictionary["sitemap.url"] = entry.Location.AbsoluteUri;
        metadataDictionary["sitemap.changeFrequency"] = entry.ChangeFrequency ?? "unspecified";
        metadataDictionary["sitemap.priority"] = entry.Priority ?? 0d;
        metadataDictionary["sitemap.lastModified"] = entry.LastModified?.UtcDateTime;
        metadataDictionary["sitemap.score"] = entry.HeuristicScore;

        return new WebPageIngestionRequest
        {
            Url = entry.Location.AbsoluteUri,
            DocumentId = documentId,
            Title = scrapedPage.Title,
            Tags = tags,
            Metadata = metadataDictionary
        };
    }

    private static string ComputeDocumentId(Uri uri, string? prefix)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(uri.AbsoluteUri));
        var hash = Convert.ToHexString(bytes);
        return string.IsNullOrWhiteSpace(prefix) ? hash : string.Concat(prefix, "-", hash);
    }
}
