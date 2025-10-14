using System.Linq;
using Moq;
using SemanticHub.IngestionService.Application.Workflows;
using SemanticHub.IngestionService.Domain.Aggregates;
using SemanticHub.IngestionService.Domain.Ports;
using SemanticHub.IngestionService.Domain.Results;
using SemanticHub.IngestionService.Models;
using Microsoft.Extensions.Logging;

namespace SemanticHub.Tests.Workflows;

public class HtmlIngestionWorkflowTests
{
    [Fact]
    public async Task ExecuteAsync_DelegatesToHtmlProcessor()
    {
        var processorMock = new Mock<IHtmlProcessor>();
        processorMock
            .Setup(p => p.IngestHtmlAsync(It.IsAny<HtmlIngestionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentIngestionResult
            {
                Success = true,
                DocumentId = "doc",
                IndexName = "index",
                ChunksIndexed = 3
            });

        var workflow = new HtmlIngestionWorkflow(Mock.Of<ILogger<HtmlIngestionWorkflow>>(), processorMock.Object);

        var metadata = IngestionMetadata.Create("doc", "Title", "html", new Uri("https://example.com"), new[] { "tag" }, new Dictionary<string, object>());
        var request = new HtmlDocumentIngestion(metadata, "<html><body>Hi</body></html>", metadata.SourceUri);

        var outcome = await workflow.ExecuteAsync(request, CancellationToken.None);

        Assert.True(outcome.Success);
        processorMock.Verify(p => p.IngestHtmlAsync(
            It.Is<HtmlIngestionRequest>(r =>
                r.DocumentId == "doc" &&
                r.Title == "Title" &&
                r.Tags!.Single() == "tag" &&
                r.Content.Contains("Hi")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsFailureOutcome()
    {
        var processorMock = new Mock<IHtmlProcessor>();
        processorMock
            .Setup(p => p.IngestHtmlAsync(It.IsAny<HtmlIngestionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentIngestionResult
            {
                Success = false,
                DocumentId = "doc",
                Message = "conversion failed"
            });

        var workflow = new HtmlIngestionWorkflow(Mock.Of<ILogger<HtmlIngestionWorkflow>>(), processorMock.Object);
        var request = new HtmlDocumentIngestion(IngestionMetadata.Create(null, null, null, null, null, null), "<html></html>");

        var outcome = await workflow.ExecuteAsync(request, CancellationToken.None);

        Assert.False(outcome.Success);
        Assert.NotNull(outcome.Error);
        Assert.Contains("failed", outcome.Error!.Message, StringComparison.OrdinalIgnoreCase);
    }
}
