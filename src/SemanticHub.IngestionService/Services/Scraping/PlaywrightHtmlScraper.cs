using System.Diagnostics;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using Polly;
using Polly.Retry;
using SemanticHub.IngestionService.Domain.Ports;
using SemanticHub.IngestionService.Diagnostics;
using SemanticHub.IngestionService.Models;
using SemanticHub.ServiceDefaults.Configuration;

namespace SemanticHub.IngestionService.Services.Scraping;

/// <summary>
/// Playwright-backed implementation of the HTML scraper port.
/// </summary>
public sealed class PlaywrightHtmlScraper(
    ILogger<PlaywrightHtmlScraper> logger,
    IOptions<ResilienceOptions> resilienceOptions) : IHtmlScraper, IAsyncDisposable
{
    private readonly HashSet<string> _visitedUrls = [];
    private readonly SemaphoreSlim _concurrencyLimit = new(3);
    private readonly IAsyncPolicy<ScrapedPage> _scrapePolicy = CreateRetryPolicy(logger, resilienceOptions.Value);

    private IPlaywright? _playwright;
    private IBrowser? _browser;

    /// <summary>
    /// Creates a retry policy for web scraping based on resilience configuration.
    /// </summary>
    private static IAsyncPolicy<ScrapedPage> CreateRetryPolicy(
        ILogger<PlaywrightHtmlScraper> logger,
        ResilienceOptions options)
    {
        if (!options.Enabled)
        {
            // Resilience disabled - return no-op policy (no retries)
            return Policy.NoOpAsync<ScrapedPage>();
        }

        var webScrapingOptions = options.WebScraping;

        return Policy<ScrapedPage>
            .Handle<Exception>()
            .WaitAndRetryAsync(
                retryCount: webScrapingOptions.MaxAttempts,
                sleepDurationProvider: attempt =>
                {
                    if (webScrapingOptions.UseExponentialBackoff)
                    {
                        var delay = TimeSpan.FromMilliseconds(
                            webScrapingOptions.BaseDelayMs * Math.Pow(2, attempt));

                        var maxDelay = TimeSpan.FromMilliseconds(webScrapingOptions.MaxDelayMs);
                        return delay > maxDelay ? maxDelay : delay;
                    }

                    return TimeSpan.FromMilliseconds(webScrapingOptions.BaseDelayMs);
                },
                onRetry: (outcome, _, attempt, _) =>
                {
                    if (outcome.Exception is not null)
                    {
                        logger.LogWarning(outcome.Exception, "Retrying web page scrape. Attempt {Attempt}", attempt);
                    }
                    else
                    {
                        logger.LogWarning("Retrying web page scrape. Attempt {Attempt}", attempt);
                    }
                });
    }

    public async Task<ScrapedPage> ScrapeAsync(Uri url, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        await EnsureBrowserAsync(cancellationToken);

        return await _scrapePolicy.ExecuteAsync(
            (_, token) => ScrapeInternalAsync(url, token),
            new Context(),
            cancellationToken);
    }

    public async Task<IReadOnlyList<ScrapedPage>> ScrapeManyAsync(
        IEnumerable<Uri> urls,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(urls);

        var results = new List<ScrapedPage>();
        foreach (var url in urls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await ScrapeAsync(url, cancellationToken));
        }

        return results;
    }

    public async Task<IReadOnlyList<ScrapedPage>> ScrapeRecursivelyAsync(
        Uri startUrl,
        int maxDepth = 2,
        int maxPages = 50,
        IEnumerable<string>? allowedDomains = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(startUrl);
        await EnsureBrowserAsync(cancellationToken);

        logger.LogInformation("Starting recursive crawl from {Url} with depth {Depth}", startUrl, maxDepth);

        _visitedUrls.Clear();
        var results = new List<ScrapedPage>();
        var queue = new Queue<(Uri Url, int Depth)>();
        queue.Enqueue((startUrl, 0));

        var allowed = allowedDomains?.ToHashSet(StringComparer.OrdinalIgnoreCase)
                      ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase) { startUrl.Host };

        while (queue.Count > 0 && results.Count < maxPages && !cancellationToken.IsCancellationRequested)
        {
            var (currentUrl, depth) = queue.Dequeue();
            if (_visitedUrls.Contains(currentUrl.AbsoluteUri) || depth > maxDepth)
            {
                continue;
            }

            if (!allowed.Contains(currentUrl.Host))
            {
                logger.LogDebug("Skipping {Url} - outside allowed domains", currentUrl);
                continue;
            }

            _visitedUrls.Add(currentUrl.AbsoluteUri);

            await _concurrencyLimit.WaitAsync(cancellationToken);
            try
            {
                var page = await ScrapeAsync(currentUrl, cancellationToken);
                if (page.IsSuccess)
                {
                    results.Add(page);
                    if (depth < maxDepth)
                    {
                        foreach (var link in page.Links)
                        {
                            if (Uri.TryCreate(link, UriKind.Absolute, out var linkUri) &&
                                !_visitedUrls.Contains(linkUri.AbsoluteUri))
                            {
                                queue.Enqueue((linkUri, depth + 1));
                            }
                        }
                    }
                }

                await Task.Delay(200, cancellationToken);
            }
            finally
            {
                _concurrencyLimit.Release();
            }
        }

        logger.LogInformation("Recursive crawl completed. Scraped {Count} pages", results.Count);
        return results;
    }

    public async Task<IReadOnlyList<ScrapedPage>> ScrapeSitemapAsync(
        Uri sitemapUrl,
        int maxPages = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sitemapUrl);
        await EnsureBrowserAsync(cancellationToken);

        logger.LogInformation("Scraping sitemap: {Sitemap}", sitemapUrl);

        using var httpClient = new HttpClient();
        var sitemapXml = await httpClient.GetStringAsync(sitemapUrl, cancellationToken);
        var urls = ParseSitemap(sitemapXml);
        var urlsToScrape = urls.Take(maxPages).ToList();

        var results = new List<ScrapedPage>();
        foreach (var url in urlsToScrape)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                continue;
            }

            await _concurrencyLimit.WaitAsync(cancellationToken);
            try
            {
                var page = await ScrapeAsync(uri, cancellationToken);
                if (page.IsSuccess)
                {
                    results.Add(page);
                }

                await Task.Delay(200, cancellationToken);
            }
            finally
            {
                _concurrencyLimit.Release();
            }
        }

        logger.LogInformation("Sitemap scraping completed. Scraped {Count} pages", results.Count);
        return results;
    }

    private async Task EnsureBrowserAsync(CancellationToken cancellationToken)
    {
        if (_browser != null)
        {
            return;
        }

        _playwright ??= await Microsoft.Playwright.Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
    }

    private async Task<ScrapedPage> ScrapeInternalAsync(Uri url, CancellationToken cancellationToken)
    {
        using var activity = IngestionTelemetry.ActivitySource.StartActivity("ScrapeWebPage");
        activity?.SetTag("ingestion.scraper.url", url.AbsoluteUri);

        var stopwatch = Stopwatch.StartNew();
        logger.LogInformation("Scraping page: {Url}", url);

        var page = await _browser!.NewPageAsync();
        try
        {
            var response = await page.GotoAsync(
                url.ToString(),
                new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.NetworkIdle,
                    Timeout = 30000
                });

            if (response == null)
            {
                throw new InvalidOperationException($"Failed to load page: {url}");
            }

            var title = await page.TitleAsync();
            var htmlContent = await page.ContentAsync();
            var metadata = await ExtractMetadataAsync(page);
            var links = await page.EvaluateAsync<string[]>(@"
                Array.from(document.querySelectorAll('a[href]'))
                    .map(a => a.href)
                    .filter(href => href.startsWith('http'))
            ");

            var scrapedPage = new ScrapedPage
            {
                Url = url.AbsoluteUri,
                Title = string.IsNullOrWhiteSpace(title) ? url.AbsoluteUri : title,
                HtmlContent = htmlContent,
                Metadata = metadata,
                Links = links?.ToList() ?? [],
                StatusCode = response.Status,
                ScrapedAt = DateTime.UtcNow
            };

            stopwatch.Stop();
            activity?.SetTag("ingestion.scraper.durationMs", stopwatch.Elapsed.TotalMilliseconds);
            activity?.SetTag("ingestion.scraper.statusCode", response.Status);
            activity?.SetStatus(ActivityStatusCode.Ok);

            logger.LogInformation("Successfully scraped page: {Title} ({Url})", scrapedPage.Title, url);
            return scrapedPage;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogError(ex, "Error scraping page: {Url}", url);
            return new ScrapedPage
            {
                Url = url.AbsoluteUri,
                Title = "Error",
                HtmlContent = string.Empty,
                StatusCode = 500,
                Metadata = new Dictionary<string, string>
                {
                    ["error"] = ex.Message
                }
            };
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    private async Task<Dictionary<string, string>> ExtractMetadataAsync(IPage page)
    {
        try
        {
            var metaTags = await page.EvaluateAsync<Dictionary<string, string>>(@"
                (() => {
                    const meta = {};
                    const selectContent = (selector) => {
                        const element = document.querySelector(selector);
                        return element ? element.content : null;
                    };

                    const mappings = {
                        description: 'meta[name=""description""]',
                        author: 'meta[name=""author""]',
                        keywords: 'meta[name=""keywords""]',
                        'og:title': 'meta[property=""og:title""]',
                        'og:description': 'meta[property=""og:description""]',
                        published: 'meta[property=""article:published_time""]'
                    };

                    Object.entries(mappings).forEach(([key, selector]) => {
                        const value = selectContent(selector);
                        if (value) meta[key] = value;
                    });

                    return meta;
                })()
            ");

            return metaTags ?? [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to extract metadata for page");
            return [];
        }
    }

    private List<string> ParseSitemap(string sitemapXml)
    {
        var results = new List<string>();
        try
        {
            var document = XDocument.Parse(sitemapXml);
            var ns = document.Root?.Name.Namespace;
            if (ns is null)
            {
                return results;
            }

            foreach (var urlElement in document.Descendants(ns + "url"))
            {
                var loc = urlElement.Element(ns + "loc")?.Value;
                if (!string.IsNullOrWhiteSpace(loc))
                {
                    results.Add(loc);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to parse sitemap XML");
        }

        return results;
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser != null)
        {
            await _browser.CloseAsync();
            await _browser.DisposeAsync();
        }

        if (_playwright != null)
        {
            _playwright.Dispose();
        }

        _concurrencyLimit.Dispose();
    }
}
