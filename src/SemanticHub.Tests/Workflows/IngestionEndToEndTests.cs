using System.Linq;
using System.Xml.Linq;
using Moq;
using SemanticHub.IngestionService.Application.Workflows;
using System.Net;
using SemanticHub.IngestionService.Configuration;
using SemanticHub.IngestionService.Domain.Aggregates;
using SemanticHub.IngestionService.Domain.Ports;
using SemanticHub.IngestionService.Domain.Results;
using SemanticHub.IngestionService.Domain.Sitemaps;
using SemanticHub.IngestionService.Models;
using SemanticHub.IngestionService.Services;
using SemanticHub.IngestionService.Services.Processors;
using SemanticHub.IngestionService.Services.Sitemaps;
using Microsoft.Extensions.Logging;

namespace SemanticHub.Tests.Workflows;

public class IngestionEndToEndTests
{
    [Fact]
    public async Task HtmlDocument_FlowsThroughMarkdownConversion()
    {
        var capturedRequest = default(MarkdownIngestionRequest);

        var markdownProcessorMock = new Mock<IMarkdownProcessor>();
        markdownProcessorMock
            .Setup(m => m.IngestAsync(It.IsAny<MarkdownIngestionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<MarkdownIngestionRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new DocumentIngestionResult
            {
                Success = true,
                DocumentId = "doc",
                IndexName = "index",
                ChunksIndexed = 3
            });

        var markdownConverter = new MarkdownConverter(Mock.Of<ILogger<MarkdownConverter>>());
        var htmlProcessor = new HtmlProcessor(
            Mock.Of<ILogger<HtmlProcessor>>(),
            markdownConverter,
            markdownProcessorMock.Object);

        var workflow = new HtmlIngestionWorkflow(
            Mock.Of<ILogger<HtmlIngestionWorkflow>>(),
            htmlProcessor);

        var metadata = IngestionMetadata.Create("doc", "Sample", "html", new Uri("https://example.com"), new[] { "tag" }, null);
        var request = new HtmlDocumentIngestion(metadata, "<html><body><h1>Sample</h1><p>Body</p></body></html>", metadata.SourceUri);

        var outcome = await workflow.ExecuteAsync(request, CancellationToken.None);

        Assert.True(outcome.Success);
        Assert.NotNull(capturedRequest);
        Assert.Contains("Sample", capturedRequest!.Content);
        Assert.Contains("Body", capturedRequest.Content);
        Assert.Equal("doc", capturedRequest.DocumentId);
    }

    [Fact]
    public async Task SitemapWorkflow_EndToEndProcessesSyntheticDataset()
    {
        var options = new IngestionOptions();
        options.Sitemap.MaxConcurrency = 1;
        options.Sitemap.ThrottleMilliseconds = 0;

        var documents = new Dictionary<string, (string Content, bool IsIndex)>
        {
            ["https://example.com/sitemap.xml"] = (
                """
                <sitemapindex xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
                  <sitemap><loc>https://example.com/articles.xml</loc></sitemap>
                  <sitemap><loc>https://example.com/docs.xml</loc></sitemap>
                </sitemapindex>
                """,
                true),
            ["https://example.com/articles.xml"] = (
                """
                <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
                  <url><loc>https://example.com/articles/1</loc><changefreq>daily</changefreq></url>
                  <url><loc>https://example.com/articles/2</loc><changefreq>weekly</changefreq></url>
                </urlset>
                """,
                false),
            ["https://example.com/docs.xml"] = (
                """
                <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
                  <url><loc>https://example.com/docs/getting-started</loc><changefreq>monthly</changefreq></url>
                </urlset>
                """,
                false)
        };

        var fetcher = new StubSitemapFetcher(documents);
        var parser = new XmlSitemapParser(Mock.Of<ILogger<XmlSitemapParser>>());
        var articlesDoc = XDocument.Parse(documents["https://example.com/articles.xml"].Content);
        Assert.Equal(2, articlesDoc.Root!.Elements().Count());
        var articlesParsed = parser.Parse(new Uri("https://example.com/articles.xml"), documents["https://example.com/articles.xml"].Content);
        Assert.Equal(2, articlesParsed.Entries.Count);
        var docsParsed = parser.Parse(new Uri("https://example.com/docs.xml"), documents["https://example.com/docs.xml"].Content);
        Assert.Single(docsParsed.Entries);
        var manualEntries = new List<SitemapEntry>();
        var manualQueue = new Queue<(Uri Uri, int Depth)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        manualQueue.Enqueue((new Uri("https://example.com/sitemap.xml"), 0));

        while (manualQueue.Count > 0)
        {
            var (uri, depth) = manualQueue.Dequeue();
            if (!visited.Add(uri.AbsoluteUri))
            {
                continue;
            }

            var fetched = await fetcher.FetchAsync(uri, CancellationToken.None);
            Assert.True(fetched.Success);
            var parsed = parser.Parse(uri, fetched.Document!.Content);
            manualEntries.AddRange(parsed.Entries);

            if (depth < options.Sitemap.MaxDepth)
            {
                foreach (var child in parsed.ChildSitemaps)
                {
                    manualQueue.Enqueue((child, depth + 1));
                }
            }
        }

        Assert.Equal(3, manualEntries.Count);

        fetcher.Requested.Clear();

        var filter = new AllowAllFilter();
        var heuristic = new DefaultChangeFrequencyHeuristic(options);
        var scraper = new StubHtmlScraper();
        var processor = new RecordingHtmlProcessor();

        var workflow = new SiteMapIngestionWorkflow(
            Mock.Of<ILogger<SiteMapIngestionWorkflow>>(),
            fetcher,
            parser,
            filter,
            heuristic,
            scraper,
            processor,
            options);

        var metadata = IngestionMetadata.Create("synthetic", "Synthetic", "sitemap", new Uri("https://example.com/sitemap.xml"), null, null);
        var request = new SitemapIngestion(metadata, new Uri("https://example.com/sitemap.xml"), new SitemapIngestionSettings());

        var result = await workflow.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(new[]
        {
            "https://example.com/sitemap.xml",
            "https://example.com/articles.xml",
            "https://example.com/docs.xml"
        }, fetcher.Requested);

        Assert.Equal(3, result.TotalDiscovered);
        Assert.Equal(3, result.TotalIngested);
        Assert.Equal(0, result.TotalFailed);
        Assert.Empty(result.Errors);
        Assert.Equal(3, processor.Processed.Count);
        Assert.All(processor.Processed, req => Assert.True(req.Metadata?.ContainsKey("sitemap.url") == true));
    }

    private sealed class StubSitemapFetcher(Dictionary<string, (string Content, bool IsIndex)> documents) : ISitemapFetcher
    {
        public List<string> Requested { get; } = [];

        public Task<SitemapFetchResult> FetchAsync(Uri sitemapUri, CancellationToken cancellationToken = default)
        {
            Requested.Add(sitemapUri.AbsoluteUri);

            if (documents.TryGetValue(sitemapUri.AbsoluteUri, out var tuple))
            {
                var doc = new SitemapDocument(sitemapUri, tuple.Content, tuple.IsIndex, DateTimeOffset.UtcNow);
                return Task.FromResult(SitemapFetchResult.FromSuccess(doc));
            }

            return Task.FromResult(SitemapFetchResult.FromFailure(HttpStatusCode.NotFound, "Not found"));
        }
    }

    private sealed class AllowAllFilter : IUrlFilterPolicy
    {
        public Task<bool> ShouldIncludeAsync(SitemapEntry entry, SitemapIngestionContext context, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class StubHtmlScraper : IHtmlScraper
    {
        public Task<ScrapedPage> ScrapeAsync(Uri url, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ScrapedPage
            {
                Url = url.AbsoluteUri,
                Title = url.AbsoluteUri,
                HtmlContent = "<html><body>Stub</body></html>",
                StatusCode = 200,
                Metadata = new Dictionary<string, string>()
            });
        }

        public async Task<IReadOnlyList<ScrapedPage>> ScrapeManyAsync(IEnumerable<Uri> urls, CancellationToken cancellationToken = default)
        {
            var results = new List<ScrapedPage>();
            foreach (var url in urls)
            {
                results.Add(await ScrapeAsync(url, cancellationToken));
            }

            return results;
        }

        public Task<IReadOnlyList<ScrapedPage>> ScrapeRecursivelyAsync(Uri startUrl, int maxDepth = 2, int maxPages = 50, IEnumerable<string>? allowedDomains = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ScrapedPage>>(Array.Empty<ScrapedPage>());

        public Task<IReadOnlyList<ScrapedPage>> ScrapeSitemapAsync(Uri sitemapUrl, int maxPages = 100, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ScrapedPage>>(Array.Empty<ScrapedPage>());
    }

    private sealed class RecordingHtmlProcessor : IHtmlProcessor
    {
        public List<WebPageIngestionRequest> Processed { get; } = [];

        public Task<DocumentIngestionResult> IngestHtmlAsync(HtmlIngestionRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DocumentIngestionResult { Success = true, DocumentId = request.DocumentId ?? "html", ChunksIndexed = 1 });

        public Task<DocumentIngestionResult> IngestWebPageAsync(WebPageIngestionRequest request, ScrapedPage scrapedPage, CancellationToken cancellationToken = default)
        {
            Processed.Add(request);
            return Task.FromResult(new DocumentIngestionResult
            {
                Success = true,
                DocumentId = request.DocumentId ?? Guid.NewGuid().ToString("N"),
                ChunksIndexed = 1
            });
        }
    }
}
