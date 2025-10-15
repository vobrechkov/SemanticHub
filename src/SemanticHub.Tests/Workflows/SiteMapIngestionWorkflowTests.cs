using Microsoft.Extensions.Logging;
using Moq;
using SemanticHub.IngestionService.Application.Workflows;
using SemanticHub.IngestionService.Configuration;
using SemanticHub.IngestionService.Domain.Aggregates;
using SemanticHub.IngestionService.Domain.Ports;
using SemanticHub.IngestionService.Domain.Results;
using SemanticHub.IngestionService.Domain.Sitemaps;
using SemanticHub.IngestionService.Models;

namespace SemanticHub.Tests.Workflows;

public class SiteMapIngestionWorkflowTests
{
    [Fact]
    public async Task ExecuteAsync_IngestsFilteredEntries()
    {
        var options = new IngestionOptions();
        options.Sitemap.MaxConcurrency = 1;
        options.Sitemap.ThrottleMilliseconds = 0;

        var sitemapFetcher = new Mock<ISitemapFetcher>();
        sitemapFetcher
            .Setup(f => f.FetchAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Uri uri, CancellationToken _) => SitemapFetchResult.FromSuccess(new SitemapDocument(uri, "<xml />", false, DateTimeOffset.UtcNow)));

        var entryA = new SitemapEntry
        {
            Location = new Uri("https://example.com/a"),
            ChangeFrequency = "daily",
            HeuristicScore = 0.8
        };

        var entryB = new SitemapEntry
        {
            Location = new Uri("https://example.com/skip"),
            ChangeFrequency = "weekly",
            HeuristicScore = 0.4
        };

        var entryC = new SitemapEntry
        {
            Location = new Uri("https://example.com/b"),
            ChangeFrequency = "hourly",
            HeuristicScore = 0.9
        };

        var parser = new Mock<ISitemapParser>();
        parser
            .Setup(p => p.Parse(It.IsAny<Uri>(), It.IsAny<string>()))
            .Returns((Uri uri, string _) => uri.AbsoluteUri.EndsWith("nested.xml", StringComparison.OrdinalIgnoreCase)
                ? new SitemapParseResult { Entries = new[] { entryC } }
                : new SitemapParseResult { Entries = new[] { entryA, entryB }, ChildSitemaps = new[] { new Uri("https://example.com/nested.xml") } });

        var filter = new Mock<IUrlFilterPolicy>();
        filter
            .Setup(f => f.ShouldIncludeAsync(It.IsAny<SitemapEntry>(), It.IsAny<SitemapIngestionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SitemapEntry entry, SitemapIngestionContext _, CancellationToken _) => !entry.Location.AbsoluteUri.Contains("skip", StringComparison.OrdinalIgnoreCase));

        var heuristic = new Mock<IChangeFrequencyHeuristic>();
        heuristic
            .Setup(h => h.CalculateScore(It.IsAny<SitemapEntry>(), It.IsAny<SitemapIngestionContext>()))
            .Returns<SitemapEntry, SitemapIngestionContext>((entry, _) => entry.HeuristicScore);

        var htmlScraper = new Mock<IHtmlScraper>();
        htmlScraper
            .Setup(s => s.ScrapeAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Uri uri, CancellationToken _) => new ScrapedPage
            {
                Url = uri.AbsoluteUri,
                Title = uri.AbsoluteUri,
                HtmlContent = "<html><body>Content</body></html>",
                StatusCode = 200,
                Metadata = new Dictionary<string, string>()
            });

        var htmlProcessor = new Mock<IHtmlProcessor>();
        htmlProcessor
            .Setup(p => p.IngestWebPageAsync(It.IsAny<WebPageIngestionRequest>(), It.IsAny<ScrapedPage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentIngestionResult
            {
                Success = true,
                DocumentId = "doc",
                IndexName = "index",
                ChunksIndexed = 1
            });

        var workflow = new SiteMapIngestionWorkflow(
            Mock.Of<ILogger<SiteMapIngestionWorkflow>>(),
            sitemapFetcher.Object,
            parser.Object,
            filter.Object,
            heuristic.Object,
            htmlScraper.Object,
            htmlProcessor.Object,
            options);

        var metadata = IngestionMetadata.Create(null, "Example", "sitemap", new Uri("https://example.com/sitemap.xml"), new[] { "tag" }, null);
        var request = new SitemapIngestion(metadata, new Uri("https://example.com/sitemap.xml"), new SitemapIngestionSettings
        {
            AllowedHosts = new[] { "example.com" }
        });

        var result = await workflow.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(3, result.TotalDiscovered);
        Assert.Equal(2, result.TotalIngested);
        Assert.Equal(0, result.TotalFailed);
        Assert.True(result.TotalFiltered >= 1);
    }

    [Fact]
    public async Task ExecuteAsync_RecordsFailuresWhenProcessingFails()
    {
        var options = new IngestionOptions();
        options.Sitemap.MaxConcurrency = 1;
        options.Sitemap.ThrottleMilliseconds = 0;

        var sitemapFetcher = new Mock<ISitemapFetcher>();
        sitemapFetcher
            .Setup(f => f.FetchAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SitemapFetchResult.FromSuccess(new SitemapDocument(new Uri("https://example.com/sitemap.xml"), "<xml />", false, DateTimeOffset.UtcNow)));

        var entry = new SitemapEntry
        {
            Location = new Uri("https://example.com/page"),
            ChangeFrequency = "daily"
        };

        var parser = new Mock<ISitemapParser>();
        parser
            .Setup(p => p.Parse(It.IsAny<Uri>(), It.IsAny<string>()))
            .Returns(new SitemapParseResult { Entries = new[] { entry } });

        var filter = new Mock<IUrlFilterPolicy>();
        filter
            .Setup(f => f.ShouldIncludeAsync(It.IsAny<SitemapEntry>(), It.IsAny<SitemapIngestionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var heuristic = new Mock<IChangeFrequencyHeuristic>();
        heuristic
            .Setup(h => h.CalculateScore(It.IsAny<SitemapEntry>(), It.IsAny<SitemapIngestionContext>()))
            .Returns(0.5);

        var htmlScraper = new Mock<IHtmlScraper>();
        htmlScraper
            .Setup(s => s.ScrapeAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapedPage
            {
                Url = entry.Location.AbsoluteUri,
                Title = "Broken",
                HtmlContent = string.Empty,
                StatusCode = 500,
                Metadata = new Dictionary<string, string>()
            });

        var htmlProcessor = new Mock<IHtmlProcessor>();

        var workflow = new SiteMapIngestionWorkflow(
            Mock.Of<ILogger<SiteMapIngestionWorkflow>>(),
            sitemapFetcher.Object,
            parser.Object,
            filter.Object,
            heuristic.Object,
            htmlScraper.Object,
            htmlProcessor.Object,
            options);

        var metadata = IngestionMetadata.Create(null, "Example", "sitemap", new Uri("https://example.com/sitemap.xml"), null, null);
        var request = new SitemapIngestion(metadata, new Uri("https://example.com/sitemap.xml"), new SitemapIngestionSettings());

        var result = await workflow.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(1, result.TotalDiscovered);
        Assert.Equal(0, result.TotalIngested);
        Assert.Equal(1, result.TotalFailed);
        Assert.NotEmpty(result.Errors);
    }
}
