using System.Net.Sockets;
using System.Text;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging.Abstractions;
using SemanticHub.IngestionService.Application.Workflows;
using SemanticHub.IngestionService.Configuration;
using SemanticHub.IngestionService.Domain.Aggregates;
using SemanticHub.IngestionService.Domain.Ports;
using SemanticHub.IngestionService.Domain.Results;
using SemanticHub.IngestionService.Models;
using SemanticHub.IngestionService.Services.OpenApi;

namespace SemanticHub.Tests.Workflows;

public class OpenApiIngestionEndToEndTests
{
    [Fact]
    public async Task ExecuteAsync_HttpSpec_CompletesAndHonorsSplitterOptions()
    {
        var description = string.Join(
            " ",
            Enumerable.Repeat(
                "Detailed analytics dataset description that stresses the markdown splitter logic for OpenAPI ingestion flows.",
                80));

        var specContent = $$"""
openapi: 3.0.1
info:
  title: Sample HTTP API
  version: v1
paths:
  /analytics:
    get:
      summary: Retrieve analytics data
      description: "{{description}}"
      tags:
        - analytics
        - reporting
      responses:
        '200':
          description: Successful response with metrics payload
""";

        var port = GetFreeTcpPort();
        var prefix = $"http://127.0.0.1:{port}/";

        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        var serverTask = Task.Run(async () =>
        {
            try
            {
                var context = await listener.GetContextAsync();
                if (context.Request.Url?.AbsolutePath == "/spec/openapi.yaml")
                {
                    var buffer = Encoding.UTF8.GetBytes(specContent);
                    context.Response.ContentType = "application/yaml";
                    context.Response.ContentLength64 = buffer.Length;
                    await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                }
                else
                {
                    context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                }

                context.Response.OutputStream.Close();
            }
            catch (HttpListenerException)
            {
                // Listener was stopped before a request arrived; acceptable for test cleanup.
            }
        }, TestContext.Current.CancellationToken);

        var specUri = new Uri($"{prefix}spec/openapi.yaml");

        var options = new IngestionOptions();
        options.OpenApi.MaxMarkdownSegmentLength = 400;

        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        var locator = new OpenApiSpecLocator(
            httpClient,
            new NullBlobStorageService(),
            options,
            NullLogger<OpenApiSpecLocator>.Instance);

        var parser = new OpenApiSpecParser(NullLogger<OpenApiSpecParser>.Instance);
        var markdownGenerator = new OpenApiMarkdownGenerator(NullLogger<OpenApiMarkdownGenerator>.Instance);
        var splitter = new OpenApiDocumentSplitter(NullLogger<OpenApiDocumentSplitter>.Instance, options.OpenApi);
        var markdownProcessor = new RecordingMarkdownProcessor();

        var workflow = new OpenApiIngestionWorkflow(
            NullLogger<OpenApiIngestionWorkflow>.Instance,
            locator,
            parser,
            markdownGenerator,
            splitter,
            markdownProcessor);

        var metadata = IngestionMetadata.Create(
            "http-sample",
            "HTTP Sample API",
            "openapi",
            specUri,
            new[] { "api", "http" },
            null);

        var request = new OpenApiSpecificationIngestion(metadata, specUri.ToString(), "http-sample");

        OpenApiIngestionResult result;
        try
        {
            result = await workflow.ExecuteAsync(request, TestContext.Current.CancellationToken);
        }
        finally
        {
            listener.Stop();
            await serverTask;
        }

        Assert.True(result.Success);
        Assert.Equal(1, result.TotalEndpoints);
        Assert.Equal(markdownProcessor.Requests.Count, result.TotalChunksIndexed);
        Assert.True(markdownProcessor.Requests.Count > 1);

        var firstRequest = markdownProcessor.Requests.First();
        Assert.NotNull(firstRequest.Metadata);
        Assert.Equal(markdownProcessor.Requests.Count, Convert.ToInt32(firstRequest.Metadata!["openapi:segmentCount"]));
        Assert.Equal("http-sample_GET_analytics_part1", firstRequest.DocumentId);
        Assert.All(
            markdownProcessor.Requests,
            req => Assert.Equal(specUri.ToString(), req.SourceUrl));
    }

    private sealed class NullBlobStorageService : IBlobStorageService
    {
        public Task<List<BlobItem>> GetBlobsAsync(
            string blobPath,
            string? containerName = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<BlobItem>());

        public Task<string> ReadBlobContentAsync(
            string blobName,
            string? containerName = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Blob storage access was not expected during the HTTP ingestion test.");

        public List<BlobItem> FilterBySupportedExtensions(List<BlobItem> blobs, params string[] extensions) => blobs;
    }

    private sealed class RecordingMarkdownProcessor : IMarkdownProcessor
    {
        public List<MarkdownIngestionRequest> Requests { get; } = [];

        public Task<DocumentIngestionResult> IngestAsync(
            MarkdownIngestionRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);

            return Task.FromResult(new DocumentIngestionResult
            {
                Success = true,
                DocumentId = request.DocumentId ?? $"doc-{Requests.Count}",
                IndexName = "test-index",
                ChunksIndexed = 1,
                Message = "ok"
            });
        }
    }

    private static int GetFreeTcpPort()
    {
        using var tcpListener = new TcpListener(IPAddress.Loopback, 0);
        tcpListener.Start();
        var port = ((IPEndPoint)tcpListener.LocalEndpoint).Port;
        tcpListener.Stop();
        return port;
    }
}
