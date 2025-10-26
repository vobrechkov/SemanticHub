using System.Collections.Concurrent;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging.Abstractions;
using SemanticHub.IngestionService.Application.Workflows;
using SemanticHub.IngestionService.Configuration;
using SemanticHub.IngestionService.Domain.Aggregates;
using SemanticHub.IngestionService.Domain.Ports;
using SemanticHub.IngestionService.Domain.Results;
using SemanticHub.IngestionService.Domain.Workflows;
using SemanticHub.IngestionService.Models;
using SemanticHub.IngestionService.Services.OpenApi;

namespace SemanticHub.Tests.Workflows;

public class BulkMarkdownIngestionEndToEndTests
{
    [Fact]
    public async Task ExecuteAsync_MixedBlobSet_ProcessesAllSupportedContent()
    {
        var blobContents = new Dictionary<string, string>
        {
            ["docs/readme.md"] = "# Readme\n\nThis is **markdown** content.",
            ["docs/page.html"] = "<html><body><h1>Sample page</h1><p>HTML body.</p></body></html>",
            ["docs/api.yaml"] =
                """
                openapi: 3.0.1
                info:
                  title: Sample API
                  version: v1
                paths:
                  /items:
                    get:
                      summary: Fetch items
                      responses:
                        '200':
                          description: ok
                """,
            ["docs/ignore.txt"] = "Unrelated file that should be ignored."
        };

        var blobStorage = new FakeBlobStorageService(blobContents);
        var markdownProcessor = new RecordingMarkdownProcessor(chunksPerDocument: 2);
        var htmlProcessor = new RecordingHtmlProcessor(chunksPerDocument: 3);
        var openApiWorkflow = new RecordingOpenApiWorkflow(endpointsProcessed: 1, chunksPerEndpoint: 4);

        var options = new IngestionOptions
        {
            AzureSearch = new AzureSearchOptions { IndexName = "test-index" },
            BlobStorage = new AzureBlobStorageOptions { DefaultContainer = "default-container" },
            OpenApi = new OpenApiIngestionOptions { MaxMarkdownSegmentLength = 2_000 }
        };

        var workflow = new BlobIngestionWorkflow(
            NullLogger<BlobIngestionWorkflow>.Instance,
            blobStorage,
            markdownProcessor,
            htmlProcessor,
            new OpenApiSpecParser(NullLogger<OpenApiSpecParser>.Instance),
            openApiWorkflow,
            options);

        var metadata = IngestionMetadata.Create(
            documentId: "bulk-run",
            title: "Bulk Run",
            sourceType: "blob",
            sourceUri: new Uri("https://contoso.example/ingestion"),
            tags: new[] { "bulk", "ingestion" },
            metadata: new Dictionary<string, object> { ["origin"] = "test-suite" });

        var request = new BulkMarkdownIngestion(metadata, "docs/", "ingestion-container");

        var result = await workflow.ExecuteAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(3, result.TotalFiles);
        Assert.Equal(3, result.FilesProcessed);
        Assert.Equal(
            markdownProcessor.TotalChunks + htmlProcessor.TotalChunks + openApiWorkflow.TotalChunks,
            result.TotalChunksIndexed);
        Assert.Empty(result.Errors);

        var expectedReads = new[] { "docs/api.yaml", "docs/page.html", "docs/readme.md" };
        Assert.Equal(expectedReads, blobStorage.ReadBlobs.OrderBy(x => x).ToArray());

        var markdownRequest = Assert.Single(markdownProcessor.Requests);
        Assert.Equal("readme", markdownRequest.DocumentId);
        Assert.Equal("blob://ingestion-container/docs/readme.md", markdownRequest.SourceUrl);
        Assert.Equal("blob-markdown", markdownRequest.SourceType);
        Assert.Contains("bulk", markdownRequest.Tags!);
        Assert.Equal("test-suite", markdownRequest.Metadata!["origin"]);

        var htmlRequest = Assert.Single(htmlProcessor.Requests);
        Assert.Equal("page", htmlRequest.DocumentId);
        Assert.Equal("blob://ingestion-container/docs/page.html", htmlRequest.SourceUrl);
        Assert.Contains("ingestion", htmlRequest.Tags!);

        var openApiRequest = Assert.Single(openApiWorkflow.Requests);
        Assert.Equal("blob://ingestion-container/docs/api.yaml", openApiRequest.SpecSource);
        Assert.Equal("api", openApiRequest.DocumentIdPrefix);
        Assert.Equal(metadata.Tags, openApiRequest.Metadata.Tags);
        Assert.Equal(metadata.CustomMetadata, openApiRequest.Metadata.CustomMetadata);
    }

    private sealed class FakeBlobStorageService : IBlobStorageService
    {
        private readonly Dictionary<string, string> _contents;
        private readonly ConcurrentBag<string> _reads = [];

        public FakeBlobStorageService(Dictionary<string, string> contents) =>
            _contents = contents;

        public IReadOnlyCollection<string> ReadBlobs => _reads.ToArray();

        public Task<List<BlobItem>> GetBlobsAsync(
            string blobPath,
            string? containerName = null,
            CancellationToken cancellationToken = default)
        {
            var items = _contents.Keys
                .Where(name => name.StartsWith(blobPath, StringComparison.OrdinalIgnoreCase))
                .Select(name => BlobsModelFactory.BlobItem(name: name))
                .ToList();

            return Task.FromResult(items);
        }

        public Task<string> ReadBlobContentAsync(
            string blobName,
            string? containerName = null,
            CancellationToken cancellationToken = default)
        {
            if (!_contents.TryGetValue(blobName, out var content))
            {
                throw new FileNotFoundException("Blob not found", blobName);
            }

            _reads.Add(blobName);
            return Task.FromResult(content);
        }

        public List<BlobItem> FilterBySupportedExtensions(List<BlobItem> blobs, params string[] extensions)
        {
            var allowed = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
            return blobs
                .Where(item => allowed.Contains(Path.GetExtension(item.Name)))
                .ToList();
        }
    }

    private sealed class RecordingMarkdownProcessor(int chunksPerDocument) : IMarkdownProcessor
    {
        private readonly int _chunksPerDocument = chunksPerDocument;
        private readonly ConcurrentBag<MarkdownIngestionRequest> _requests = [];

        public IReadOnlyCollection<MarkdownIngestionRequest> Requests => _requests.ToArray();

        public int TotalChunks => _requests.Count * _chunksPerDocument;

        public Task<DocumentIngestionResult> IngestAsync(
            MarkdownIngestionRequest request,
            CancellationToken cancellationToken = default)
        {
            _requests.Add(request);

            return Task.FromResult(new DocumentIngestionResult
            {
                Success = true,
                DocumentId = request.DocumentId ?? "markdown",
                IndexName = "test-index",
                ChunksIndexed = _chunksPerDocument,
                Message = "ok"
            });
        }
    }

    private sealed class RecordingHtmlProcessor(int chunksPerDocument) : IHtmlProcessor
    {
        private readonly int _chunksPerDocument = chunksPerDocument;
        private readonly ConcurrentBag<HtmlIngestionRequest> _requests = [];

        public IReadOnlyCollection<HtmlIngestionRequest> Requests => _requests.ToArray();

        public int TotalChunks => _requests.Count * _chunksPerDocument;

        public Task<DocumentIngestionResult> IngestHtmlAsync(
            HtmlIngestionRequest request,
            CancellationToken cancellationToken = default)
        {
            _requests.Add(request);

            return Task.FromResult(new DocumentIngestionResult
            {
                Success = true,
                DocumentId = request.DocumentId ?? "html",
                IndexName = "test-index",
                ChunksIndexed = _chunksPerDocument,
                Message = "ok"
            });
        }

        public Task<DocumentIngestionResult> IngestWebPageAsync(
            WebPageIngestionRequest request,
            ScrapedPage scrapedPage,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new DocumentIngestionResult
            {
                Success = true,
                DocumentId = request.DocumentId ?? "web",
                IndexName = "test-index",
                ChunksIndexed = _chunksPerDocument,
                Message = "ok"
            });
    }

    private sealed class RecordingOpenApiWorkflow(int endpointsProcessed, int chunksPerEndpoint)
        : IIngestionWorkflow<OpenApiSpecificationIngestion, OpenApiIngestionResult>
    {
        private readonly ConcurrentBag<OpenApiSpecificationIngestion> _requests = [];
        private readonly int _chunksPerEndpoint = chunksPerEndpoint;
        private readonly int _endpointsProcessed = endpointsProcessed;

        public IReadOnlyCollection<OpenApiSpecificationIngestion> Requests => _requests.ToArray();

        public int TotalChunks => _requests.Count * _endpointsProcessed * _chunksPerEndpoint;

        public Task<OpenApiIngestionResult> ExecuteAsync(
            OpenApiSpecificationIngestion request,
            CancellationToken cancellationToken = default)
        {
            _requests.Add(request);

            return Task.FromResult(new OpenApiIngestionResult
            {
                Success = true,
                SpecSource = request.SpecSource,
                EndpointsProcessed = _endpointsProcessed,
                TotalEndpoints = _endpointsProcessed,
                TotalChunksIndexed = _endpointsProcessed * _chunksPerEndpoint,
                Message = "ok"
            });
        }
    }
}
