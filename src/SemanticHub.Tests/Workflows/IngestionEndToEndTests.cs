using Moq;
using SemanticHub.IngestionService.Application.Workflows;
using SemanticHub.IngestionService.Domain.Aggregates;
using SemanticHub.IngestionService.Domain.Ports;
using SemanticHub.IngestionService.Domain.Results;
using SemanticHub.IngestionService.Models;
using SemanticHub.IngestionService.Services;
using SemanticHub.IngestionService.Services.Processors;
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
}
