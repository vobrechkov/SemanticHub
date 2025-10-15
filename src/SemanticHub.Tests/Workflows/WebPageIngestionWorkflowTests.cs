using Moq;
using SemanticHub.IngestionService.Application.Workflows;
using SemanticHub.IngestionService.Domain.Aggregates;
using SemanticHub.IngestionService.Domain.Ports;
using SemanticHub.IngestionService.Domain.Results;
using SemanticHub.IngestionService.Models;
using Microsoft.Extensions.Logging;

namespace SemanticHub.Tests.Workflows;

public class WebPageIngestionWorkflowTests
{
    [Fact]
    public async Task ExecuteAsync_SuccessfulScrapeAndIngestion()
    {
        var scraperMock = new Mock<IHtmlScraper>();
        scraperMock
            .Setup(s => s.ScrapeAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapedPage
            {
                Url = "https://example.com",
                Title = "Example",
                HtmlContent = "<html></html>",
                StatusCode = 200,
                Metadata = new Dictionary<string, string>()
            });

        var processorMock = new Mock<IHtmlProcessor>();
        processorMock
            .Setup(p => p.IngestWebPageAsync(It.IsAny<WebPageIngestionRequest>(), It.IsAny<ScrapedPage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentIngestionResult
            {
                Success = true,
                DocumentId = "doc",
                IndexName = "index",
                ChunksIndexed = 5
            });

        var workflow = new WebPageIngestionWorkflow(
            Mock.Of<ILogger<WebPageIngestionWorkflow>>(),
            scraperMock.Object,
            processorMock.Object);

        var metadata = IngestionMetadata.Create("doc", "Example", "webpage", new Uri("https://example.com"), null, null);
        var request = new WebPageIngestion(metadata, new Uri("https://example.com"))
        {
            TitleOverride = "Example"
        };

        var outcome = await workflow.ExecuteAsync(request, CancellationToken.None);

        Assert.True(outcome.Success);
        processorMock.Verify(p => p.IngestWebPageAsync(
            It.IsAny<WebPageIngestionRequest>(),
            It.IsAny<ScrapedPage>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsFailureWhenScrapeFails()
    {
        var scraperMock = new Mock<IHtmlScraper>();
        scraperMock
            .Setup(s => s.ScrapeAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScrapedPage
            {
                Url = "https://example.com",
                Title = "Example",
                HtmlContent = string.Empty,
                StatusCode = 500,
                Metadata = new Dictionary<string, string>()
            });

        var processorMock = new Mock<IHtmlProcessor>();

        var workflow = new WebPageIngestionWorkflow(
            Mock.Of<ILogger<WebPageIngestionWorkflow>>(),
            scraperMock.Object,
            processorMock.Object);

        var request = new WebPageIngestion(IngestionMetadata.Create(null, null, null, new Uri("https://example.com"), null, null), new Uri("https://example.com"));

        var outcome = await workflow.ExecuteAsync(request, CancellationToken.None);

        Assert.False(outcome.Success);
        Assert.NotNull(outcome.Error);
        Assert.Equal(IngestionErrorCode.ScrapeFailed, outcome.Error!.Code);
        processorMock.Verify(p => p.IngestWebPageAsync(It.IsAny<WebPageIngestionRequest>(), It.IsAny<ScrapedPage>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
