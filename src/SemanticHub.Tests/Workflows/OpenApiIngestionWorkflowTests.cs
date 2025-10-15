using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using SemanticHub.IngestionService.Application.Workflows;
using SemanticHub.IngestionService.Domain.Aggregates;
using SemanticHub.IngestionService.Domain.OpenApi;
using SemanticHub.IngestionService.Domain.Ports;
using SemanticHub.IngestionService.Domain.Results;
using SemanticHub.IngestionService.Models;

namespace SemanticHub.Tests.Workflows;

public class OpenApiIngestionWorkflowTests
{
    private readonly Mock<IOpenApiSpecLocator> _locator = new();
    private readonly Mock<IOpenApiSpecParser> _parser = new();
    private readonly Mock<IOpenApiMarkdownGenerator> _generator = new();
    private readonly Mock<IOpenApiDocumentSplitter> _splitter = new();
    private readonly Mock<IMarkdownProcessor> _markdownProcessor = new();
    private readonly OpenApiIngestionWorkflow _workflow;

    public OpenApiIngestionWorkflowTests()
    {
        _workflow = new OpenApiIngestionWorkflow(
            Mock.Of<Microsoft.Extensions.Logging.ILogger<OpenApiIngestionWorkflow>>(),
            _locator.Object,
            _parser.Object,
            _generator.Object,
            _splitter.Object,
            _markdownProcessor.Object);
    }

    [Fact]
    public async Task ExecuteAsync_SuccessfulIngestion_ReturnsResult()
    {
        var endpoint = new OpenApiEndpoint
        {
            Id = "GET_health",
            Method = "GET",
            Path = "/health",
            OperationId = "Health"
        };

        ArrangeSpecification(endpoint, markdownSuccess: true);

        var request = CreateRequest("spec.yaml", "health");

        var result = await _workflow.ExecuteAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, result.EndpointsProcessed);
        Assert.Equal(1, result.TotalEndpoints);
        Assert.Equal(2, result.TotalChunksIndexed);
        Assert.Contains("Successfully ingested", result.Message);
    }

    [Fact]
    public async Task ExecuteAsync_MarkdownFailure_ReturnsFailure()
    {
        var endpoint = new OpenApiEndpoint
        {
            Id = "POST_items",
            Method = "POST",
            Path = "/items"
        };

        ArrangeSpecification(endpoint, markdownSuccess: false);

        var request = CreateRequest("spec.yaml", "items");

        var result = await _workflow.ExecuteAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(0, result.EndpointsProcessed);
        Assert.Single(result.Errors);
    }

    private void ArrangeSpecification(OpenApiEndpoint endpoint, bool markdownSuccess)
    {
        _locator
            .Setup(l => l.LocateAsync(It.IsAny<OpenApiSpecificationIngestion>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OpenApiSpecDocument("spec.yaml", "openapi: 3.0.1", new Uri("file:///spec.yaml")));

        _parser
            .Setup(p => p.ParseAsync(It.IsAny<OpenApiSpecDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OpenApiSpecificationDocument("Spec", "1.0", "spec.yaml", new List<OpenApiEndpoint> { endpoint }));

        _generator
            .Setup(g => g.Generate(It.IsAny<OpenApiSpecificationDocument>(), endpoint))
            .Returns("markdown");

        _splitter
            .Setup(s => s.Split(It.IsAny<OpenApiSpecificationDocument>(), endpoint, It.IsAny<string>()))
            .Returns(new List<OpenApiEndpointDocument>
            {
                new(endpoint, "markdown", 1, 1)
            });

        _markdownProcessor
            .Setup(p => p.IngestAsync(It.IsAny<MarkdownIngestionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(markdownSuccess
                ? new DocumentIngestionResult { Success = true, DocumentId = "health", ChunksIndexed = 2 }
                : new DocumentIngestionResult { Success = false, Message = "failure" });
    }

    private static OpenApiSpecificationIngestion CreateRequest(string specSource, string? prefix)
    {
        var metadata = IngestionMetadata.Create(
            prefix,
            "Spec",
            "openapi",
            null,
            Array.Empty<string>(),
            null);

        return new OpenApiSpecificationIngestion(metadata, specSource, prefix);
    }
}
