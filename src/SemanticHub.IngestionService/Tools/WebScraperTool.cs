using System.Diagnostics;
using Microsoft.Playwright;
using SemanticHub.IngestionService.Models;
using System.Xml.Linq;
using SemanticHub.IngestionService.Diagnostics;

namespace SemanticHub.IngestionService.Tools;

/// <summary>
/// Web scraping tool using Playwright for JS-rendered content
/// Supports single-page, recursive crawling, and sitemap-based scraping
/// </summary>
public class WebScraperTool(ILogger<WebScraperTool> logger)
{
    private IBrowser? _browser;
    private readonly HashSet<string> _visitedUrls = [];
    private readonly SemaphoreSlim _concurrencyLimit = new(3); // Max 3 concurrent pages

    /// <summary>
    /// Initialize Playwright and browser
    /// </summary>
    public async Task InitializeAsync()
    {
        logger.LogInformation("Initializing Playwright browser");
        var playwright = await Playwright.CreateAsync();
        _browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
    }

    /// <summary>
    /// Scrape a single web page
    /// </summary>
    public async Task<ScrapedPage> ScrapeSinglePageAsync(string url, CancellationToken cancellationToken = default)
    {
        if (_browser == null)
        {
            await InitializeAsync();
        }

        using var activity = IngestionTelemetry.ActivitySource.StartActivity("ScrapeWebPage");
        activity?.SetTag("ingestion.scraper.url", url);
        var stopwatch = Stopwatch.StartNew();

        logger.LogInformation("Scraping single page: {Url}", url);

        var page = await _browser!.NewPageAsync();
        try
        {
            var response = await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 30000
            });

            if (response == null)
            {
                throw new InvalidOperationException($"Failed to load page: {url}");
            }

            // Extract content
            var title = await page.TitleAsync();
            var htmlContent = await page.ContentAsync();

            // Extract metadata from meta tags
            var metadata = await ExtractMetadataAsync(page);

            // Extract links for potential recursive crawling
            var links = await page.EvaluateAsync<string[]>(@"
                Array.from(document.querySelectorAll('a[href]'))
                    .map(a => a.href)
                    .filter(href => href.startsWith('http'))
            ");

            var scrapedPage = new ScrapedPage
            {
                Url = url,
                Title = title,
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

            logger.LogInformation("Successfully scraped page: {Title} ({Url})", title, url);
            return scrapedPage;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogError(ex, "Error scraping page: {Url}", url);
            return new ScrapedPage
            {
                Url = url,
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

    /// <summary>
    /// Scrape pages recursively starting from a URL
    /// </summary>
    public async Task<List<ScrapedPage>> ScrapeRecursivelyAsync(
        string startUrl,
        int maxDepth = 2,
        int maxPages = 50,
        string[]? allowedDomains = null,
        CancellationToken cancellationToken = default)
    {
        if (_browser == null)
        {
            await InitializeAsync();
        }

        logger.LogInformation("Starting recursive crawl from {Url} with max depth {MaxDepth}", startUrl, maxDepth);

        _visitedUrls.Clear();
        var results = new List<ScrapedPage>();
        var urlsToVisit = new Queue<(string Url, int Depth)>();
        urlsToVisit.Enqueue((startUrl, 0));

        var startUri = new Uri(startUrl);
        var allowedDomainsSet = allowedDomains?.ToHashSet() ?? [startUri.Host];

        while (urlsToVisit.Count > 0 && results.Count < maxPages && !cancellationToken.IsCancellationRequested)
        {
            var (currentUrl, depth) = urlsToVisit.Dequeue();

            // Skip if already visited
            if (_visitedUrls.Contains(currentUrl))
            {
                continue;
            }

            // Skip if exceeds max depth
            if (depth > maxDepth)
            {
                continue;
            }

            // Check if domain is allowed
            if (!Uri.TryCreate(currentUrl, UriKind.Absolute, out var uri) ||
                !allowedDomainsSet.Contains(uri.Host))
            {
                continue;
            }

            _visitedUrls.Add(currentUrl);

            await _concurrencyLimit.WaitAsync(cancellationToken);
            try
            {
                var scrapedPage = await ScrapeSinglePageAsync(currentUrl, cancellationToken);

                if (scrapedPage.IsSuccess)
                {
                    results.Add(scrapedPage);

                    // Add links to queue for next depth level
                    if (depth < maxDepth)
                    {
                        foreach (var link in scrapedPage.Links)
                        {
                            if (!_visitedUrls.Contains(link))
                            {
                                urlsToVisit.Enqueue((link, depth + 1));
                            }
                        }
                    }
                }

                // Be polite: wait between requests
                await Task.Delay(1000, cancellationToken);
            }
            finally
            {
                _concurrencyLimit.Release();
            }
        }

        logger.LogInformation("Recursive crawl completed. Scraped {Count} pages", results.Count);
        return results;
    }

    /// <summary>
    /// Scrape pages from a sitemap XML
    /// </summary>
    public async Task<List<ScrapedPage>> ScrapeSitemapAsync(
        string sitemapUrl,
        int maxPages = 100,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Scraping sitemap: {SitemapUrl}", sitemapUrl);

        // Download and parse sitemap
        using var httpClient = new HttpClient();
        var sitemapXml = await httpClient.GetStringAsync(sitemapUrl, cancellationToken);
        var urls = ParseSitemap(sitemapXml);

        logger.LogInformation("Found {Count} URLs in sitemap", urls.Count);

        var results = new List<ScrapedPage>();
        var urlsToScrape = urls.Take(maxPages).ToList();

        foreach (var url in urlsToScrape)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            await _concurrencyLimit.WaitAsync(cancellationToken);
            try
            {
                var scrapedPage = await ScrapeSinglePageAsync(url, cancellationToken);
                if (scrapedPage.IsSuccess)
                {
                    results.Add(scrapedPage);
                }

                // Be polite: wait between requests
                await Task.Delay(1000, cancellationToken);
            }
            finally
            {
                _concurrencyLimit.Release();
            }
        }

        logger.LogInformation("Sitemap scraping completed. Scraped {Count} pages", results.Count);
        return results;
    }

    /// <summary>
    /// Extract metadata from page meta tags
    /// </summary>
    private async Task<Dictionary<string, string>> ExtractMetadataAsync(IPage page)
    {
        var metadata = new Dictionary<string, string>();

        try
        {
            // Extract common meta tags
            var metaTags = await page.EvaluateAsync<Dictionary<string, string>>(@"
                (() => {
                    const meta = {};
                    // Description
                    const desc = document.querySelector('meta[name=""description""]');
                    if (desc) meta['description'] = desc.content;

                    // Author
                    const author = document.querySelector('meta[name=""author""]');
                    if (author) meta['author'] = author.content;

                    // Keywords
                    const keywords = document.querySelector('meta[name=""keywords""]');
                    if (keywords) meta['keywords'] = keywords.content;

                    // Open Graph tags
                    const ogTitle = document.querySelector('meta[property=""og:title""]');
                    if (ogTitle) meta['og:title'] = ogTitle.content;

                    const ogDesc = document.querySelector('meta[property=""og:description""]');
                    if (ogDesc) meta['og:description'] = ogDesc.content;

                    // Published date
                    const published = document.querySelector('meta[property=""article:published_time""]');
                    if (published) meta['published'] = published.content;

                    return meta;
                })()
            ");

            foreach (var kvp in metaTags)
            {
                metadata[kvp.Key] = kvp.Value;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error extracting metadata from page");
        }

        return metadata;
    }

    /// <summary>
    /// Parse sitemap XML to extract URLs
    /// </summary>
    private List<string> ParseSitemap(string sitemapXml)
    {
        var urls = new List<string>();

        try
        {
            var doc = XDocument.Parse(sitemapXml);
            var ns = doc.Root?.Name.Namespace;

            if (ns != null)
            {
                var urlElements = doc.Descendants(ns + "url");
                foreach (var urlElement in urlElements)
                {
                    var loc = urlElement.Element(ns + "loc")?.Value;
                    if (!string.IsNullOrWhiteSpace(loc))
                    {
                        urls.Add(loc);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error parsing sitemap XML");
        }

        return urls;
    }

    /// <summary>
    /// Cleanup resources
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_browser != null)
        {
            await _browser.CloseAsync();
            await _browser.DisposeAsync();
        }

        _concurrencyLimit.Dispose();
    }
}
