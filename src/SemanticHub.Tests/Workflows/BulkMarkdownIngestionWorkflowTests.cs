using Azure.Storage.Blobs.Models;
using Moq;
using SemanticHub.IngestionService.Application.Workflows;
using SemanticHub.IngestionService.Configuration;
using SemanticHub.IngestionService.Domain.Aggregates;
using SemanticHub.IngestionService.Domain.Mappers;
using SemanticHub.IngestionService.Domain.Ports;
using SemanticHub.IngestionService.Domain.Results;
using SemanticHub.IngestionService.Domain.Workflows;
using SemanticHub.IngestionService.Models;
using Microsoft.Extensions.Logging;

namespace SemanticHub.Tests.Workflows;

public class BulkMarkdownIngestionWorkflowTests
{
    [Fact]
    public async Task ExecuteAsync_NoSupportedFiles_ReturnsFailure()
    {
        var storageMock = new Mock<IBlobStorageService>();
        storageMock
            .Setup(s => s.GetBlobsAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BlobItem>());
        storageMock
            .Setup(s => s.FilterBySupportedExtensions(It.IsAny<List<BlobItem>>(), It.IsAny<string[]>()))
            .Returns(new List<BlobItem>());

        var workflow = CreateWorkflow(
            storageMock.Object,
            markdownProcessor: Mock.Of<IMarkdownProcessor>(),
            htmlProcessor: Mock.Of<IHtmlProcessor>(),
            openApiSpecParser: Mock.Of<IOpenApiSpecParser>(),
            openApiWorkflow: Mock.Of<IIngestionWorkflow<OpenApiSpecificationIngestion, OpenApiIngestionResult>>());

        var request = new BlobIngestionRequest { BlobPath = "docs/", Tags = new List<string>(), Metadata = new Dictionary<string, object>() };
        var domainRequest = request.ToDomain();

        var result = await workflow.ExecuteAsync(domainRequest, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(0, result.TotalFiles);
    }

    [Fact]
    public async Task ExecuteAsync_ProcessesMarkdownFiles()
    {
        var blobItem = BlobsModelFactory.BlobItem(name: "docs/sample.md");

        var storageMock = new Mock<IBlobStorageService>();
        storageMock
            .Setup(s => s.GetBlobsAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BlobItem> { blobItem });
        storageMock
            .Setup(s => s.FilterBySupportedExtensions(It.IsAny<List<BlobItem>>(), It.IsAny<string[]>()))
            .Returns<List<BlobItem>, string[]>((items, exts) =>
                items.Where(b => exts.Contains(Path.GetExtension(b.Name), StringComparer.OrdinalIgnoreCase)).ToList());
        storageMock
            .Setup(s => s.ReadBlobContentAsync("docs/sample.md", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("# Sample");

        var markdownProcessorMock = new Mock<IMarkdownProcessor>();
        markdownProcessorMock
            .Setup(p => p.IngestAsync(It.IsAny<MarkdownIngestionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentIngestionResult
            {
                Success = true,
                DocumentId = "sample",
                IndexName = "index",
                ChunksIndexed = 2
            });

        var workflow = CreateWorkflow(
            storageMock.Object,
            markdownProcessorMock.Object,
            htmlProcessor: Mock.Of<IHtmlProcessor>(),
            openApiSpecParser: Mock.Of<IOpenApiSpecParser>(),
            openApiWorkflow: Mock.Of<IIngestionWorkflow<OpenApiSpecificationIngestion, OpenApiIngestionResult>>());

        var request = new BlobIngestionRequest { BlobPath = "docs/", Tags = new List<string>(), Metadata = new Dictionary<string, object>() };
        var domainRequest = request.ToDomain();

        var result = await workflow.ExecuteAsync(domainRequest, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, result.FilesProcessed);
        Assert.Equal(2, result.TotalChunksIndexed);
    }

    private static BlobIngestionWorkflow CreateWorkflow(
        IBlobStorageService storageService,
        IMarkdownProcessor markdownProcessor,
        IHtmlProcessor htmlProcessor,
        IOpenApiSpecParser openApiSpecParser,
        IIngestionWorkflow<OpenApiSpecificationIngestion, OpenApiIngestionResult> openApiWorkflow)
    {
        var options = new IngestionOptions
        {
            AzureSearch = new AzureSearchOptions { IndexName = "index" },
            BlobStorage = new AzureBlobStorageOptions { DefaultContainer = "container" }
        };

        return new BlobIngestionWorkflow(
            Mock.Of<ILogger<BlobIngestionWorkflow>>(),
            storageService,
            markdownProcessor,
            htmlProcessor,
            openApiSpecParser,
            openApiWorkflow,
            options);
    }
}
