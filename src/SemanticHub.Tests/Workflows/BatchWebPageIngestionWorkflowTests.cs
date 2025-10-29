using Microsoft.Extensions.Logging;
using Moq;
using SemanticHub.IngestionService.Application.Workflows;
using SemanticHub.IngestionService.Domain.Aggregates;
using SemanticHub.IngestionService.Domain.Ports;
using SemanticHub.IngestionService.Domain.Results;
using SemanticHub.IngestionService.Models;

namespace SemanticHub.Tests.Workflows;

public class BatchWebPageIngestionWorkflowTests
{
    [Fact]
    public async Task ExecuteAsync_IngestsMultipleUrlsSuccessfully()
    {
        // Arrange
        var urls = new[]
        {
            new Uri("https://example.com/page1"),
            new Uri("https://example.com/page2"),
            new Uri("https://example.com/page3")
        };

        var htmlScraper = new Mock<IHtmlScraper>();
        htmlScraper
            .Setup(s => s.ScrapeAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Uri uri, CancellationToken _) => new ScrapedPage
            {
                Url = uri.AbsoluteUri,
                Title = $"Title for {uri.AbsoluteUri}",
                HtmlContent = $"<html><body><h1>{uri.AbsoluteUri}</h1><p>Content</p></body></html>",
                StatusCode = 200,
                Metadata = new Dictionary<string, string>()
            });

        var htmlProcessor = new Mock<IHtmlProcessor>();
        htmlProcessor
            .Setup(p => p.IngestWebPageAsync(It.IsAny<WebPageIngestionRequest>(), It.IsAny<ScrapedPage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WebPageIngestionRequest req, ScrapedPage page, CancellationToken _) =>
                new DocumentIngestionResult
                {
                    Success = true,
                    DocumentId = $"doc-{req.Url}",
                    IndexName = "knowledge-index",
                    ChunksIndexed = 5
                });

        var workflow = new BatchWebPageIngestionWorkflow(
            Mock.Of<ILogger<BatchWebPageIngestionWorkflow>>(),
            htmlScraper.Object,
            htmlProcessor.Object);

        var metadata = IngestionMetadata.Create(
            null,
            "Batch Test",
            "batch-webpage",
            urls[0],
            new[] { "test", "batch" },
            null);

        var request = new BatchWebPageIngestion(metadata, urls)
        {
            MaxConcurrency = 2,
            ThrottleMilliseconds = 0
        };

        // Act
        var result = await workflow.ExecuteAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(3, result.TotalRequested);
        Assert.Equal(3, result.TotalSucceeded);
        Assert.Equal(0, result.TotalFailed);
        Assert.Equal(3, result.Results.Count);
        Assert.All(result.Results, r => Assert.True(r.Success));
        Assert.All(result.Results, r => Assert.Equal(5, r.ChunksIndexed));

        // Verify scraper was called for each URL
        htmlScraper.Verify(s => s.ScrapeAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()), Times.Exactly(3));

        // Verify processor was called for each successful scrape
        htmlProcessor.Verify(p => p.IngestWebPageAsync(It.IsAny<WebPageIngestionRequest>(), It.IsAny<ScrapedPage>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task ExecuteAsync_HandlesPartialFailures()
    {
        // Arrange
        var urls = new[]
        {
            new Uri("https://example.com/success"),
            new Uri("https://example.com/fail"),
            new Uri("https://example.com/success2")
        };

        var htmlScraper = new Mock<IHtmlScraper>();
        htmlScraper
            .Setup(s => s.ScrapeAsync(It.Is<Uri>(u => u.AbsoluteUri.Contains("fail")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapedPage
            {
                Url = "https://example.com/fail",
                Title = "Failed Page",
                HtmlContent = string.Empty,
                StatusCode = 404,
                Metadata = new Dictionary<string, string>()
            });

        htmlScraper
            .Setup(s => s.ScrapeAsync(It.Is<Uri>(u => u.AbsoluteUri.Contains("success")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Uri uri, CancellationToken _) => new ScrapedPage
            {
                Url = uri.AbsoluteUri,
                Title = $"Success {uri.AbsoluteUri}",
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
                ChunksIndexed = 3
            });

        var workflow = new BatchWebPageIngestionWorkflow(
            Mock.Of<ILogger<BatchWebPageIngestionWorkflow>>(),
            htmlScraper.Object,
            htmlProcessor.Object);

        var metadata = IngestionMetadata.Create(null, "Partial Failure Test", "batch-webpage", urls[0], null, null);
        var request = new BatchWebPageIngestion(metadata, urls)
        {
            MaxConcurrency = 3,
            ThrottleMilliseconds = 0
        };

        // Act
        var result = await workflow.ExecuteAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.Success); // Should be false because there was a failure
        Assert.Equal(3, result.TotalRequested);
        Assert.Equal(2, result.TotalSucceeded);
        Assert.Equal(1, result.TotalFailed);
        Assert.Equal(3, result.Results.Count);

        var failedResult = result.Results.FirstOrDefault(r => !r.Success);
        Assert.NotNull(failedResult);
        Assert.Contains("404", failedResult.ErrorMessage!);

        // Verify processor was only called for successful scrapes
        htmlProcessor.Verify(p => p.IngestWebPageAsync(It.IsAny<WebPageIngestionRequest>(), It.IsAny<ScrapedPage>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ExecuteAsync_HandlesProcessingException()
    {
        // Arrange
        var urls = new[]
        {
            new Uri("https://example.com/page1"),
            new Uri("https://example.com/exception"),
            new Uri("https://example.com/page2")
        };

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
            .Setup(p => p.IngestWebPageAsync(
                It.Is<WebPageIngestionRequest>(r => r.Url.Contains("exception")),
                It.IsAny<ScrapedPage>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Processing failed"));

        htmlProcessor
            .Setup(p => p.IngestWebPageAsync(
                It.Is<WebPageIngestionRequest>(r => !r.Url.Contains("exception")),
                It.IsAny<ScrapedPage>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentIngestionResult
            {
                Success = true,
                DocumentId = "doc",
                IndexName = "index",
                ChunksIndexed = 2
            });

        var workflow = new BatchWebPageIngestionWorkflow(
            Mock.Of<ILogger<BatchWebPageIngestionWorkflow>>(),
            htmlScraper.Object,
            htmlProcessor.Object);

        var metadata = IngestionMetadata.Create(null, "Exception Test", "batch-webpage", urls[0], null, null);
        var request = new BatchWebPageIngestion(metadata, urls)
        {
            MaxConcurrency = 3,
            ThrottleMilliseconds = 0
        };

        // Act
        var result = await workflow.ExecuteAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal(3, result.TotalRequested);
        Assert.Equal(2, result.TotalSucceeded);
        Assert.Equal(1, result.TotalFailed);

        var failedResult = result.Results.FirstOrDefault(r => !r.Success && r.Url.AbsoluteUri.Contains("exception"));
        Assert.NotNull(failedResult);
        Assert.Contains("Processing failed", failedResult.ErrorMessage!);
    }

    [Fact]
    public async Task ExecuteAsync_RespectsConcurrencyLimit()
    {
        // Arrange
        var urls = Enumerable.Range(1, 10)
            .Select(i => new Uri($"https://example.com/page{i}"))
            .ToArray();

        var activeRequests = 0;
        var maxConcurrentRequests = 0;
        var lockObject = new object();

        var htmlScraper = new Mock<IHtmlScraper>();
        htmlScraper
            .Setup(s => s.ScrapeAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .Returns<Uri, CancellationToken>(async (uri, ct) =>
            {
                lock (lockObject)
                {
                    activeRequests++;
                    maxConcurrentRequests = Math.Max(maxConcurrentRequests, activeRequests);
                }

                await Task.Delay(50, ct); // Simulate work

                lock (lockObject)
                {
                    activeRequests--;
                }

                return new ScrapedPage
                {
                    Url = uri.AbsoluteUri,
                    Title = uri.AbsoluteUri,
                    HtmlContent = "<html><body>Content</body></html>",
                    StatusCode = 200,
                    Metadata = new Dictionary<string, string>()
                };
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

        var workflow = new BatchWebPageIngestionWorkflow(
            Mock.Of<ILogger<BatchWebPageIngestionWorkflow>>(),
            htmlScraper.Object,
            htmlProcessor.Object);

        var metadata = IngestionMetadata.Create(null, "Concurrency Test", "batch-webpage", urls[0], null, null);
        var request = new BatchWebPageIngestion(metadata, urls)
        {
            MaxConcurrency = 3,
            ThrottleMilliseconds = 0
        };

        // Act
        var result = await workflow.ExecuteAsync(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(10, result.TotalSucceeded);
        Assert.True(maxConcurrentRequests <= 3, $"Max concurrent requests was {maxConcurrentRequests}, expected <= 3");
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsForNullRequest()
    {
        // Arrange
        var workflow = new BatchWebPageIngestionWorkflow(
            Mock.Of<ILogger<BatchWebPageIngestionWorkflow>>(),
            Mock.Of<IHtmlScraper>(),
            Mock.Of<IHtmlProcessor>());

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            workflow.ExecuteAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_IncludesTagsAndMetadataInRequests()
    {
        // Arrange
        var urls = new[] { new Uri("https://example.com/page") };
        var expectedTags = new[] { "tag1", "tag2" };
        var expectedMetadata = new Dictionary<string, object> { ["key"] = "value" };

        WebPageIngestionRequest? capturedRequest = null;

        var htmlScraper = new Mock<IHtmlScraper>();
        htmlScraper
            .Setup(s => s.ScrapeAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapedPage
            {
                Url = urls[0].AbsoluteUri,
                Title = "Test",
                HtmlContent = "<html><body>Content</body></html>",
                StatusCode = 200,
                Metadata = new Dictionary<string, string>()
            });

        var htmlProcessor = new Mock<IHtmlProcessor>();
        htmlProcessor
            .Setup(p => p.IngestWebPageAsync(It.IsAny<WebPageIngestionRequest>(), It.IsAny<ScrapedPage>(), It.IsAny<CancellationToken>()))
            .Callback<WebPageIngestionRequest, ScrapedPage, CancellationToken>((req, _, _) => capturedRequest = req)
            .ReturnsAsync(new DocumentIngestionResult
            {
                Success = true,
                DocumentId = "doc",
                IndexName = "index",
                ChunksIndexed = 1
            });

        var workflow = new BatchWebPageIngestionWorkflow(
            Mock.Of<ILogger<BatchWebPageIngestionWorkflow>>(),
            htmlScraper.Object,
            htmlProcessor.Object);

        var metadata = IngestionMetadata.Create(null, "Test", "batch-webpage", urls[0], expectedTags, expectedMetadata);
        var request = new BatchWebPageIngestion(metadata, urls)
        {
            MaxConcurrency = 1,
            ThrottleMilliseconds = 0
        };

        // Act
        await workflow.ExecuteAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(capturedRequest);
        Assert.Equal(expectedTags.Length, capturedRequest.Tags!.Count);
        Assert.All(expectedTags, tag => Assert.Contains(tag, capturedRequest.Tags));
        Assert.NotNull(capturedRequest.Metadata);
        Assert.Single(capturedRequest.Metadata);
        Assert.Equal("value", capturedRequest.Metadata["key"]);
    }
}
