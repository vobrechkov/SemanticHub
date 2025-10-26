using Moq;
using SemanticHub.IngestionService.Application.Workflows;
using SemanticHub.IngestionService.Domain.Aggregates;
using SemanticHub.IngestionService.Domain.Ports;
using SemanticHub.IngestionService.Domain.Results;
using SemanticHub.IngestionService.Models;
using Microsoft.Extensions.Logging;

namespace SemanticHub.Tests.Workflows;

public class MarkdownIngestionWorkflowTests
{
    [Fact]
    public async Task ExecuteAsync_MapsRequestAndReturnsSuccess()
    {
        // Arrange
        var processorMock = new Mock<IMarkdownProcessor>();
        processorMock
            .Setup(p => p.IngestAsync(It.IsAny<MarkdownIngestionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentIngestionResult
            {
                Success = true,
                DocumentId = "doc-123",
                IndexName = "index",
                ChunksIndexed = 4,
                Message = "ok"
            });

        var workflow = new MarkdownIngestionWorkflow(Mock.Of<ILogger<MarkdownIngestionWorkflow>>(), processorMock.Object);

        var metadata = IngestionMetadata.Create(
            documentId: "doc-123",
            title: "Title",
            sourceType: "markdown",
            sourceUri: new Uri("https://example.com/doc.md"),
            tags: new[] { "tag1" },
            metadata: new Dictionary<string, object> { ["foo"] = "bar" });

        var request = new MarkdownDocumentIngestion(metadata, "# heading", metadata.SourceUri);

        // Act
        var outcome = await workflow.ExecuteAsync(request, CancellationToken.None);

        // Assert
        Assert.True(outcome.Success);
        Assert.NotNull(outcome.LegacyResult);
        Assert.Equal("doc-123", outcome.LegacyResult!.DocumentId);

        processorMock.Verify(p => p.IngestAsync(
            It.Is<MarkdownIngestionRequest>(r =>
                r.DocumentId == "doc-123" &&
                r.Title == "Title" &&
                r.SourceUrl == "https://example.com/doc.md" &&
                r.Tags!.Single() == "tag1" &&
                r.Content == "# heading" &&
                r.Metadata!["foo"].Equals("bar")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesFailureDiagnostics()
    {
        // Arrange
        var processorMock = new Mock<IMarkdownProcessor>();
        processorMock
            .Setup(p => p.IngestAsync(It.IsAny<MarkdownIngestionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentIngestionResult
            {
                Success = false,
                DocumentId = "doc-123",
                IndexName = "index",
                ChunksIndexed = 0,
                Message = "ingestion failed"
            });

        var workflow = new MarkdownIngestionWorkflow(Mock.Of<ILogger<MarkdownIngestionWorkflow>>(), processorMock.Object);
        var metadata = IngestionMetadata.Create(null, "Title", "markdown", null, null, null);
        var request = new MarkdownDocumentIngestion(metadata, "content");

        // Act
        var outcome = await workflow.ExecuteAsync(request, CancellationToken.None);

        // Assert
        Assert.False(outcome.Success);
        Assert.NotNull(outcome.Error);
        Assert.Contains("failed", outcome.Error!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(outcome.Diagnostics.ContainsKey("chunksIndexed"));
    }
}
