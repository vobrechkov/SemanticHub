using System.Diagnostics;
using System.Diagnostics.Metrics;
using SemanticHub.IngestionService.Configuration;
using SemanticHub.IngestionService.Diagnostics;
using SemanticHub.IngestionService.Models;
using SemanticHub.IngestionService.Tools;

namespace SemanticHub.IngestionService.Services;

/// <summary>
/// High-level orchestration for ingesting documents into Azure AI Search.
/// </summary>
public class DocumentIngestionService(
    ILogger<DocumentIngestionService> logger,
    SemanticChunker chunker,
    AzureOpenAIEmbeddingService embeddingService,
    AzureSearchIndexer indexer,
    SearchIndexInitializer indexInitializer,
    IngestionOptions options,
    MarkdownConverter markdownConverter,
    WebScraperTool webScraperTool,
    OpenApiIngestionTool openApiIngestionTool,
    BlobStorageService blobStorageService)
{
    public async Task<DocumentIngestionResult> IngestWebPageAsync(
        WebPageIngestionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateWebPageRequest(request);

        using var activity = IngestionTelemetry.ActivitySource.StartActivity("IngestWebPage");
        activity?.SetTag("ingestion.sourceType", "webpage");
        activity?.SetTag("ingestion.request.url", request.Url);

        logger.LogInformation("Scraping and ingesting web page: {Url}", request.Url);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var scrapedPage = await webScraperTool.ScrapeSinglePageAsync(request.Url, cancellationToken);
            return await HandleScrapedWebPageAsync(request, scrapedPage, activity, stopwatch, cancellationToken);
        }
        catch (Exception ex)
        {
            HandleWebPageIngestionException(ex, request, activity, stopwatch);
            throw;
        }
    }

    public async Task<DocumentIngestionResult> IngestHtmlAsync(
        HtmlIngestionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateHtmlRequest(request);

        using var activity = IngestionTelemetry.ActivitySource.StartActivity("IngestHtml");
        activity?.SetTag("ingestion.sourceType", "html");

        logger.LogInformation("Ingesting HTML content");

        try
        {
            var scrapedPage = CreateScrapedPageFromHtml(request);
            var markdownRequest = BuildMarkdownRequestFromHtml(request, scrapedPage);

            activity?.SetTag("ingestion.html.contentLength", markdownRequest.Content.Length);

            return await IngestMarkdownAsync(markdownRequest, cancellationToken);
        }
        catch (Exception ex)
        {
            HandleHtmlIngestionException(ex, activity);
            throw;
        }
    }

    public async Task<BlobIngestionResult> IngestFromBlobAsync(
        BlobIngestionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateBlobIngestionRequest(request);

        using var activity = IngestionTelemetry.ActivitySource.StartActivity("IngestFromBlob");
        activity?.SetTag("ingestion.sourceType", "blob");
        activity?.SetTag("ingestion.blobPath", request.BlobPath);

        logger.LogInformation("Ingesting documents from blob path: {Path}", request.BlobPath);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var supportedBlobs = await GetSupportedBlobsAsync(request, cancellationToken);
            if (supportedBlobs.Count == 0)
            {
                stopwatch.Stop();
                activity?.SetStatus(ActivityStatusCode.Error, "NoSupportedFiles");
                logger.LogWarning("No supported files found in blob path: {Path}", request.BlobPath);
                return BuildEmptyBlobIngestionResult(request);
            }

            activity?.SetTag("ingestion.blob.totalFiles", supportedBlobs.Count);

            var result = CreateBlobResult(request, supportedBlobs.Count);
            await ProcessBlobGroupsAsync(supportedBlobs, request, result, cancellationToken);

            stopwatch.Stop();
            UpdateBlobActivityTelemetry(activity, result, stopwatch);
            FinalizeBlobResultMessage(result);

            activity?.SetStatus(ActivityStatusCode.Ok);
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogError(ex, "Error ingesting from blob path {Path}", request.BlobPath);
            throw;
        }
    }

    private Task ProcessMarkdownFilesAsync(
        IReadOnlyCollection<Azure.Storage.Blobs.Models.BlobItem> files,
        BlobIngestionRequest request,
        BlobIngestionResult result,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Processing {Count} Markdown files", files.Count);
        return ProcessBlobFilesAsync(
            files,
            result,
            file => ProcessMarkdownFileAsync(file, request, cancellationToken));
    }

    private Task ProcessOpenApiFilesAsync(
        IReadOnlyCollection<Azure.Storage.Blobs.Models.BlobItem> files,
        BlobIngestionRequest request,
        BlobIngestionResult result,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Processing {Count} OpenAPI files", files.Count);
        return ProcessBlobFilesAsync(
            files,
            result,
            file => ProcessOpenApiFileAsync(file, request, cancellationToken));
    }

    private Task ProcessHtmlFilesAsync(
        IReadOnlyCollection<Azure.Storage.Blobs.Models.BlobItem> files,
        BlobIngestionRequest request,
        BlobIngestionResult result,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Processing {Count} HTML files", files.Count);
        return ProcessBlobFilesAsync(
            files,
            result,
            file => ProcessHtmlFileAsync(file, request, cancellationToken));
    }

    private void ValidateWebPageRequest(WebPageIngestionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Url))
        {
            throw new ArgumentException("Request URL must not be empty.", nameof(request));
        }
    }

    private async Task<DocumentIngestionResult> HandleScrapedWebPageAsync(
        WebPageIngestionRequest request,
        ScrapedPage scrapedPage,
        Activity? activity,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        activity?.SetTag("ingestion.scrape.statusCode", scrapedPage.StatusCode);

        var scrapeTags = CreateWebPageTags(scrapedPage.Url ?? request.Url, scrapedPage.IsSuccess ? "success" : "failed");

        if (!HasScrapedContent(scrapedPage))
        {
            return HandleFailedWebPageScrape(request, scrapeTags, activity, stopwatch, scrapedPage.StatusCode);
        }

        IngestionTelemetry.WebPagesScraped.Add(1, scrapeTags);

        if (!string.IsNullOrWhiteSpace(request.Title))
        {
            scrapedPage.Title = request.Title!;
        }

        var markdownContent = markdownConverter.ConvertToMarkdown(scrapedPage);
        activity?.SetTag("ingestion.scrape.contentLength", markdownContent.Length);

        var markdownRequest = BuildMarkdownRequestFromWebPage(request, scrapedPage, markdownContent);

        stopwatch.Stop();
        activity?.SetTag("ingestion.scrape.durationMs", stopwatch.Elapsed.TotalMilliseconds);

        var ingestionResult = await IngestMarkdownAsync(markdownRequest, cancellationToken);
        activity?.SetStatus(ActivityStatusCode.Ok);
        return ingestionResult;
    }

    private static bool HasScrapedContent(ScrapedPage scrapedPage) =>
        scrapedPage.IsSuccess && !string.IsNullOrWhiteSpace(scrapedPage.HtmlContent);

    private DocumentIngestionResult HandleFailedWebPageScrape(
        WebPageIngestionRequest request,
        TagList scrapeTags,
        Activity? activity,
        Stopwatch stopwatch,
        int statusCode)
    {
        stopwatch.Stop();
        activity?.SetStatus(ActivityStatusCode.Error, "ScrapeFailed");
        IngestionTelemetry.WebPagesScraped.Add(1, scrapeTags);
        logger.LogWarning("Failed to scrape content from {Url}. Status code: {StatusCode}", request.Url, statusCode);
        return BuildFailedWebPageResult(request);
    }

    private void HandleWebPageIngestionException(
        Exception exception,
        WebPageIngestionRequest request,
        Activity? activity,
        Stopwatch stopwatch)
    {
        stopwatch.Stop();
        activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
        IngestionTelemetry.WebPagesScraped.Add(1, CreateWebPageTags(request.Url, "failed"));
        logger.LogError(exception, "Error ingesting web page {Url}", request.Url);
    }

    private static void ValidateHtmlRequest(HtmlIngestionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
        {
            throw new ArgumentException("Request content must not be empty.", nameof(request));
        }
    }

    private static ScrapedPage CreateScrapedPageFromHtml(HtmlIngestionRequest request)
    {
        return new ScrapedPage
        {
            Url = request.SourceUrl ?? "manual",
            Title = request.Title ?? "Untitled HTML Document",
            HtmlContent = request.Content,
            StatusCode = 200,
            ScrapedAt = DateTime.UtcNow,
            Metadata = new Dictionary<string, string>()
        };
    }

    private MarkdownIngestionRequest BuildMarkdownRequestFromHtml(
        HtmlIngestionRequest request,
        ScrapedPage scrapedPage)
    {
        var markdownContent = markdownConverter.ConvertToMarkdown(scrapedPage);
        return new MarkdownIngestionRequest
        {
            DocumentId = request.DocumentId,
            Title = request.Title ?? scrapedPage.Title,
            SourceUrl = request.SourceUrl,
            SourceType = "html",
            Tags = request.Tags,
            Metadata = request.Metadata,
            Content = markdownContent
        };
    }

    private void HandleHtmlIngestionException(Exception exception, Activity? activity)
    {
        activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
        logger.LogError(exception, "Error ingesting HTML content");
    }

    private static void ValidateBlobIngestionRequest(BlobIngestionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.BlobPath))
        {
            throw new ArgumentException("Request BlobPath must not be empty.", nameof(request));
        }
    }

    private async Task<List<Azure.Storage.Blobs.Models.BlobItem>> GetSupportedBlobsAsync(
        BlobIngestionRequest request,
        CancellationToken cancellationToken)
    {
        var blobs = await blobStorageService.GetBlobsAsync(
            request.BlobPath,
            request.ContainerName,
            cancellationToken);

        return blobStorageService.FilterBySupportedExtensions(
            blobs,
            ".md", ".markdown", ".yml", ".yaml", ".json", ".html", ".htm");
    }

    private static BlobIngestionResult BuildEmptyBlobIngestionResult(BlobIngestionRequest request)
    {
        return new BlobIngestionResult
        {
            Success = false,
            BlobPath = request.BlobPath,
            TotalFiles = 0,
            FilesProcessed = 0,
            TotalChunksIndexed = 0,
            Message = "No supported files found (.md, .yaml, .json, .html)"
        };
    }

    private static BlobIngestionResult CreateBlobResult(BlobIngestionRequest request, int totalFiles)
    {
        return new BlobIngestionResult
        {
            Success = true,
            BlobPath = request.BlobPath,
            TotalFiles = totalFiles,
            FilesProcessed = 0,
            TotalChunksIndexed = 0
        };
    }

    private async Task ProcessBlobGroupsAsync(
        IReadOnlyList<Azure.Storage.Blobs.Models.BlobItem> blobs,
        BlobIngestionRequest request,
        BlobIngestionResult result,
        CancellationToken cancellationToken)
    {
        var tasks = new List<Task>();

        AddBlobProcessingTask(
            tasks,
            FilterByExtensions(blobs, ".md", ".markdown"),
            files => ProcessMarkdownFilesAsync(files, request, result, cancellationToken));

        AddBlobProcessingTask(
            tasks,
            FilterByExtensions(blobs, ".yml", ".yaml", ".json"),
            files => ProcessOpenApiFilesAsync(files, request, result, cancellationToken));

        AddBlobProcessingTask(
            tasks,
            FilterByExtensions(blobs, ".html", ".htm"),
            files => ProcessHtmlFilesAsync(files, request, result, cancellationToken));

        if (tasks.Count == 0)
        {
            return;
        }

        await Task.WhenAll(tasks);
    }

    private static void AddBlobProcessingTask(
        ICollection<Task> tasks,
        IReadOnlyCollection<Azure.Storage.Blobs.Models.BlobItem> files,
        Func<IReadOnlyCollection<Azure.Storage.Blobs.Models.BlobItem>, Task> processor)
    {
        if (files.Count == 0)
        {
            return;
        }

        tasks.Add(processor(files));
    }

    private static IReadOnlyCollection<Azure.Storage.Blobs.Models.BlobItem> FilterByExtensions(
        IEnumerable<Azure.Storage.Blobs.Models.BlobItem> blobs,
        params string[] extensions)
    {
        var allowed = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
        return blobs
            .Where(b => allowed.Contains(Path.GetExtension(b.Name) ?? string.Empty))
            .ToList();
    }

    private static void UpdateBlobActivityTelemetry(Activity? activity, BlobIngestionResult result, Stopwatch stopwatch)
    {
        activity?.SetTag("ingestion.blob.filesProcessed", result.FilesProcessed);
        activity?.SetTag("ingestion.blob.totalChunks", result.TotalChunksIndexed);
        activity?.SetTag("ingestion.blob.durationMs", stopwatch.Elapsed.TotalMilliseconds);
    }

    private static void FinalizeBlobResultMessage(BlobIngestionResult result)
    {
        result.Message = $"Successfully processed {result.FilesProcessed} of {result.TotalFiles} files with {result.TotalChunksIndexed} total chunks.";
        if (result.Errors.Count > 0)
        {
            result.Message += $" {result.Errors.Count} errors occurred.";
        }
    }

    private static async Task ProcessBlobFilesAsync(
        IEnumerable<Azure.Storage.Blobs.Models.BlobItem> files,
        BlobIngestionResult result,
        Func<Azure.Storage.Blobs.Models.BlobItem, Task<FileIngestionOutcome>> processFileAsync)
    {
        foreach (var file in files)
        {
            var outcome = await processFileAsync(file);
            if (outcome.Success)
            {
                lock (result)
                {
                    result.FilesProcessed++;
                    result.TotalChunksIndexed += outcome.ChunksIndexed;
                }
                continue;
            }

            if (string.IsNullOrWhiteSpace(outcome.Error))
            {
                continue;
            }

            lock (result.Errors)
            {
                result.Errors.Add(outcome.Error);
            }
        }
    }

    private async Task<FileIngestionOutcome> ProcessMarkdownFileAsync(
        Azure.Storage.Blobs.Models.BlobItem file,
        BlobIngestionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var content = await blobStorageService.ReadBlobContentAsync(
                file.Name,
                request.ContainerName,
                cancellationToken);

            var markdownRequest = new MarkdownIngestionRequest
            {
                DocumentId = Path.GetFileNameWithoutExtension(file.Name),
                Title = Path.GetFileNameWithoutExtension(file.Name),
                SourceUrl = CreateBlobSourceUrl(request, file.Name),
                SourceType = "blob-markdown",
                Tags = request.Tags,
                Metadata = request.Metadata,
                Content = content
            };

            var ingestionResult = await IngestMarkdownAsync(markdownRequest, cancellationToken);
            return ingestionResult.Success
                ? FileIngestionOutcome.FromSuccess(ingestionResult.ChunksIndexed)
                : FileIngestionOutcome.FromFailure($"Failed to ingest {file.Name}: {ingestionResult.Message}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing Markdown file {FileName}", file.Name);
            return FileIngestionOutcome.FromFailure($"Error processing {file.Name}: {ex.Message}");
        }
    }

    private async Task<FileIngestionOutcome> ProcessOpenApiFileAsync(
        Azure.Storage.Blobs.Models.BlobItem file,
        BlobIngestionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var content = await blobStorageService.ReadBlobContentAsync(
                file.Name,
                request.ContainerName,
                cancellationToken);

            if (!IsOpenApiSpec(content))
            {
                var message = $"Skipped {file.Name}: Not a valid OpenAPI specification";
                logger.LogWarning("File {FileName} does not appear to be an OpenAPI specification, skipping", file.Name);
                return FileIngestionOutcome.FromFailure(message);
            }

            var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}{Path.GetExtension(file.Name)}");
            await File.WriteAllTextAsync(tempFile, content, cancellationToken);

            try
            {
                var openApiRequest = new OpenApiIngestionRequest
                {
                    SpecSource = tempFile,
                    DocumentIdPrefix = Path.GetFileNameWithoutExtension(file.Name),
                    Tags = request.Tags,
                    Metadata = request.Metadata
                };

                var ingestionResult = await IngestOpenApiAsync(openApiRequest, cancellationToken);
                return ingestionResult.Success
                    ? FileIngestionOutcome.FromSuccess(ingestionResult.TotalChunksIndexed)
                    : FileIngestionOutcome.FromFailure($"Failed to ingest {file.Name}: {ingestionResult.Message}");
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing OpenAPI file {FileName}", file.Name);
            return FileIngestionOutcome.FromFailure($"Error processing {file.Name}: {ex.Message}");
        }
    }

    private async Task<FileIngestionOutcome> ProcessHtmlFileAsync(
        Azure.Storage.Blobs.Models.BlobItem file,
        BlobIngestionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var content = await blobStorageService.ReadBlobContentAsync(
                file.Name,
                request.ContainerName,
                cancellationToken);

            var htmlRequest = new HtmlIngestionRequest
            {
                DocumentId = Path.GetFileNameWithoutExtension(file.Name),
                Title = Path.GetFileNameWithoutExtension(file.Name),
                SourceUrl = CreateBlobSourceUrl(request, file.Name),
                Tags = request.Tags,
                Metadata = request.Metadata,
                Content = content
            };

            var ingestionResult = await IngestHtmlAsync(htmlRequest, cancellationToken);
            return ingestionResult.Success
                ? FileIngestionOutcome.FromSuccess(ingestionResult.ChunksIndexed)
                : FileIngestionOutcome.FromFailure($"Failed to ingest {file.Name}: {ingestionResult.Message}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing HTML file {FileName}", file.Name);
            return FileIngestionOutcome.FromFailure($"Error processing {file.Name}: {ex.Message}");
        }
    }

    private string CreateBlobSourceUrl(BlobIngestionRequest request, string blobName)
    {
        var container = request.ContainerName ?? options.BlobStorage.DefaultContainer;
        return $"blob://{container ?? string.Empty}/{blobName}";
    }

    private readonly record struct FileIngestionOutcome(bool Success, int ChunksIndexed, string? Error)
    {
        public static FileIngestionOutcome FromSuccess(int chunks) => new(true, chunks, null);
        public static FileIngestionOutcome FromFailure(string error) => new(false, 0, error);
    }

    private static bool IsOpenApiSpec(string content)
    {
        // Basic verification to check if content looks like OpenAPI spec
        // Check for common OpenAPI 2.0 and 3.x markers
        return content.Contains("openapi:", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("swagger:", StringComparison.OrdinalIgnoreCase) ||
               (content.Contains("\"openapi\"", StringComparison.OrdinalIgnoreCase) &&
                content.Contains("\"info\"", StringComparison.OrdinalIgnoreCase)) ||
               (content.Contains("\"swagger\"", StringComparison.OrdinalIgnoreCase) &&
                content.Contains("\"info\"", StringComparison.OrdinalIgnoreCase));
    }

    private DocumentIngestionResult BuildFailedWebPageResult(WebPageIngestionRequest request)
    {
        return new DocumentIngestionResult
        {
            Success = false,
            DocumentId = request.DocumentId ?? request.Url,
            IndexName = options.AzureSearch.IndexName,
            ChunksIndexed = 0,
            Message = $"Failed to scrape content from '{request.Url}'."
        };
    }

    private static MarkdownIngestionRequest BuildMarkdownRequestFromWebPage(
        WebPageIngestionRequest request,
        ScrapedPage scrapedPage,
        string markdownContent)
    {
        return new MarkdownIngestionRequest
        {
            DocumentId = request.DocumentId,
            Title = request.Title ?? scrapedPage.Title,
            SourceUrl = scrapedPage.Url ?? request.Url,
            SourceType = "webpage",
            Tags = request.Tags,
            Metadata = request.Metadata,
            Content = markdownContent
        };
    }

    private static TagList CreateWebPageTags(string? url, string status)
    {
        var tags = new TagList();
        tags.Add("status", status);
        tags.Add("sourceType", "webpage");

        if (!string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            tags.Add("host", uri.Host);
        }

        return tags;
    }

    private TagList CreateOpenApiTags(OpenApiIngestionRequest request, string status)
    {
        var tags = new TagList();
        tags.Add("status", status);
        tags.Add("sourceType", "openapi");
        if (!string.IsNullOrWhiteSpace(request.SpecSource) &&
            Uri.TryCreate(request.SpecSource, UriKind.Absolute, out var uri))
        {
            tags.Add("host", uri.Host);
        }

        return tags;
    }

    public async Task<OpenApiIngestionResult> IngestOpenApiAsync(
        OpenApiIngestionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateOpenApiRequest(request);

        using var activity = IngestionTelemetry.ActivitySource.StartActivity("IngestOpenApi");
        activity?.SetTag("ingestion.sourceType", "openapi");
        activity?.SetTag("ingestion.request.specSource", request.SpecSource);

        logger.LogInformation("Ingesting OpenAPI spec from: {Source}", request.SpecSource);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var endpoints = await ParseOpenApiEndpointsAsync(request, activity, cancellationToken);
            if (endpoints.Count == 0)
            {
                stopwatch.Stop();
                return BuildEmptyOpenApiResult(request, activity);
            }

            var markdownDocs = openApiIngestionTool.ConvertEndpointsToMarkdown(endpoints);
            var summary = await IngestOpenApiEndpointsAsync(request, endpoints, markdownDocs, activity, cancellationToken);

            stopwatch.Stop();
            UpdateOpenApiTelemetry(activity, summary, stopwatch);

            return BuildOpenApiResult(request, endpoints.Count, summary, activity);
        }
        catch (Exception ex)
        {
            HandleOpenApiIngestionException(ex, request, activity, stopwatch);
            throw;
        }
    }

    private static void ValidateOpenApiRequest(OpenApiIngestionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SpecSource))
        {
            throw new ArgumentException("Request SpecSource must not be empty.", nameof(request));
        }
    }

    private async Task<List<OpenApiEndpoint>> ParseOpenApiEndpointsAsync(
        OpenApiIngestionRequest request,
        Activity? activity,
        CancellationToken cancellationToken)
    {
        var endpoints = await openApiIngestionTool.ParseOpenApiSpecAsync(request.SpecSource!, cancellationToken);
        activity?.SetTag("ingestion.openapi.totalEndpoints", endpoints.Count);

        if (endpoints.Count > 0)
        {
            IngestionTelemetry.OpenApiEndpointsProcessed.Add(endpoints.Count, CreateOpenApiTags(request, "parsed"));
            logger.LogInformation("Found {Count} endpoints in OpenAPI spec", endpoints.Count);
            return endpoints;
        }

        activity?.SetStatus(ActivityStatusCode.Error, "NoEndpoints");
        IngestionTelemetry.IngestionFailures.Add(1, CreateOpenApiTags(request, "no-endpoints"));
        logger.LogWarning("No endpoints found in OpenAPI spec: {Source}", request.SpecSource);
        return endpoints;
    }

    private static OpenApiIngestionResult BuildEmptyOpenApiResult(
        OpenApiIngestionRequest request,
        Activity? activity)
    {
        activity?.SetStatus(ActivityStatusCode.Error, "NoEndpoints");

        return new OpenApiIngestionResult
        {
            Success = false,
            SpecSource = request.SpecSource!,
            EndpointsProcessed = 0,
            TotalEndpoints = 0,
            TotalChunksIndexed = 0,
            Message = "No endpoints found in the OpenAPI specification."
        };
    }

    private async Task<EndpointIngestionSummary> IngestOpenApiEndpointsAsync(
        OpenApiIngestionRequest request,
        IReadOnlyList<OpenApiEndpoint> endpoints,
        IReadOnlyList<string> markdownDocs,
        Activity? activity,
        CancellationToken cancellationToken)
    {
        var successful = 0;
        var totalChunks = 0;
        var errors = new List<string>();

        var count = Math.Min(endpoints.Count, markdownDocs.Count);
        for (var i = 0; i < count; i++)
        {
            var outcome = await IngestSingleEndpointAsync(
                request,
                endpoints[i],
                markdownDocs[i],
                activity,
                cancellationToken);

            if (outcome.Success)
            {
                successful++;
                totalChunks += outcome.Chunks;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(outcome.Error))
            {
                errors.Add(outcome.Error);
            }
        }

        return new EndpointIngestionSummary(successful, totalChunks, errors);
    }

    private async Task<EndpointIngestionOutcome> IngestSingleEndpointAsync(
        OpenApiIngestionRequest request,
        OpenApiEndpoint endpoint,
        string markdown,
        Activity? parentActivity,
        CancellationToken cancellationToken)
    {
        using var endpointActivity = IngestionTelemetry.ActivitySource.StartActivity("IngestOpenApiEndpoint");
        endpointActivity?.SetTag("ingestion.openapi.method", endpoint.Method);
        endpointActivity?.SetTag("ingestion.openapi.path", endpoint.Path);

        try
        {
            var markdownRequest = BuildMarkdownRequestFromEndpoint(request, endpoint, markdown);
            var result = await IngestMarkdownAsync(markdownRequest, cancellationToken);

            if (result.Success)
            {
                endpointActivity?.SetStatus(ActivityStatusCode.Ok);
                return EndpointIngestionOutcome.FromSuccess(result.ChunksIndexed);
            }

            endpointActivity?.SetStatus(ActivityStatusCode.Error, result.Message);
            return EndpointIngestionOutcome.FromFailure($"Failed to ingest {endpoint.Method} {endpoint.Path}: {result.Message}");
        }
        catch (Exception ex)
        {
            endpointActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogError(ex, "Error ingesting endpoint {Method} {Path}", endpoint.Method, endpoint.Path);
            parentActivity?.AddEvent(new ActivityEvent("EndpointIngestionFailed", tags: new ActivityTagsCollection
            {
                { "method", endpoint.Method },
                { "path", endpoint.Path },
                { "error", ex.Message }
            }));

            return EndpointIngestionOutcome.FromFailure($"Error ingesting {endpoint.Method} {endpoint.Path}: {ex.Message}");
        }
    }

    private static MarkdownIngestionRequest BuildMarkdownRequestFromEndpoint(
        OpenApiIngestionRequest request,
        OpenApiEndpoint endpoint,
        string markdown)
    {
        var documentId = string.IsNullOrWhiteSpace(request.DocumentIdPrefix)
            ? endpoint.Id
            : $"{request.DocumentIdPrefix}_{endpoint.Id}";

        return new MarkdownIngestionRequest
        {
            DocumentId = documentId,
            Title = $"{endpoint.Method} {endpoint.Path}",
            SourceUrl = request.SpecSource,
            SourceType = "openapi",
            Tags = request.Tags,
            Metadata = request.Metadata,
            Content = markdown
        };
    }

    private static void UpdateOpenApiTelemetry(
        Activity? activity,
        EndpointIngestionSummary summary,
        Stopwatch stopwatch)
    {
        activity?.SetTag("ingestion.openapi.processed", summary.SuccessfulCount);
        activity?.SetTag("ingestion.openapi.totalChunks", summary.TotalChunks);
        activity?.SetTag("ingestion.openapi.durationMs", stopwatch.Elapsed.TotalMilliseconds);
    }

    private OpenApiIngestionResult BuildOpenApiResult(
        OpenApiIngestionRequest request,
        int totalEndpoints,
        EndpointIngestionSummary summary,
        Activity? activity)
    {
        var success = summary.SuccessfulCount > 0;
        var message = success
            ? $"Successfully ingested {summary.SuccessfulCount} of {totalEndpoints} endpoints with {summary.TotalChunks} total chunks."
            : "Failed to ingest any endpoints.";

        if (summary.Errors.Count > 0)
        {
            message += $" Errors: {string.Join("; ", summary.Errors)}";
        }

        var statusTag = success ? "success" : "failed";
        var resultTags = CreateOpenApiTags(request, statusTag);

        if (success)
        {
            IngestionTelemetry.OpenApiEndpointsProcessed.Add(summary.SuccessfulCount, CreateOpenApiTags(request, "indexed"));
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        else
        {
            IngestionTelemetry.IngestionFailures.Add(1, resultTags);
            activity?.SetStatus(ActivityStatusCode.Error, "NoEndpointsIndexed");
        }

        return new OpenApiIngestionResult
        {
            Success = success,
            SpecSource = request.SpecSource!,
            EndpointsProcessed = summary.SuccessfulCount,
            TotalEndpoints = totalEndpoints,
            TotalChunksIndexed = summary.TotalChunks,
            Message = message,
            Errors = summary.Errors
        };
    }

    private void HandleOpenApiIngestionException(
        Exception exception,
        OpenApiIngestionRequest request,
        Activity? activity,
        Stopwatch stopwatch)
    {
        stopwatch.Stop();
        activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
        IngestionTelemetry.IngestionFailures.Add(1, CreateOpenApiTags(request, "failed"));
        logger.LogError(exception, "Error ingesting OpenAPI spec {Source}", request.SpecSource);
    }

    private readonly record struct EndpointIngestionOutcome(bool Success, int Chunks, string? Error)
    {
        public static EndpointIngestionOutcome FromSuccess(int chunks) => new(true, chunks, null);
        public static EndpointIngestionOutcome FromFailure(string? error) => new(false, 0, error);
    }

    private sealed record EndpointIngestionSummary(int SuccessfulCount, int TotalChunks, List<string> Errors);

    public async Task<DocumentIngestionResult> IngestMarkdownAsync(
        MarkdownIngestionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await indexInitializer.EnsureInitializedAsync(cancellationToken);

        var documentId = string.IsNullOrWhiteSpace(request.DocumentId)
            ? GenerateDocumentId(request.Title)
            : request.DocumentId!;

        var metadata = BuildMetadata(documentId, request);

        using var activity = IngestionTelemetry.ActivitySource.StartActivity("IngestMarkdown");
        activity?.SetTag("ingestion.documentId", metadata.Id);
        activity?.SetTag("ingestion.index", options.AzureSearch.IndexName);
        activity?.SetTag("ingestion.sourceType", metadata.SourceType);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Parse YAML frontmatter if provided to enrich metadata.
            var frontmatter = markdownConverter.ParseFrontmatter(request.Content);
            if (frontmatter != null)
            {
                MergeFrontmatter(metadata, frontmatter);
                activity?.AddEvent(new ActivityEvent("FrontmatterMerged"));
            }

            var content = StripFrontmatter(request.Content);

            logger.LogInformation("Chunking document {DocumentId}. Length: {Length} characters", metadata.Id, content.Length);
            activity?.SetTag("ingestion.contentLength", content.Length);

            var chunks = chunker.ChunkMarkdown(content, metadata.Id, metadata);
            activity?.SetTag("ingestion.chunk.count", chunks.Count);

            if (chunks.Count == 0)
            {
                logger.LogWarning("No chunks produced for document {DocumentId}", metadata.Id);

                var failureResult = new DocumentIngestionResult
                {
                    Success = false,
                    DocumentId = metadata.Id,
                    IndexName = options.AzureSearch.IndexName,
                    ChunksIndexed = 0,
                    Message = "No content chunks produced."
                };

                RecordFailure(metadata, stopwatch, "no_chunks", activity);
                return failureResult;
            }

            var embeddings = await embeddingService.GenerateEmbeddingsAsync(
                chunks.Select(c => c.Content).ToList(),
                cancellationToken);

            activity?.SetTag("ingestion.embedding.count", embeddings.Count);

            for (var i = 0; i < chunks.Count && i < embeddings.Count; i++)
            {
                chunks[i].ContentVector = embeddings[i];
            }

            logger.LogInformation("Uploading {Count} chunks for document {DocumentId}", chunks.Count, metadata.Id);

            await indexer.UploadChunksAsync(chunks, cancellationToken);

            var result = new DocumentIngestionResult
            {
                Success = true,
                DocumentId = metadata.Id,
                IndexName = options.AzureSearch.IndexName,
                ChunksIndexed = chunks.Count,
                Message = $"Document '{metadata.Id}' ingested into index '{options.AzureSearch.IndexName}'."
            };

            RecordSuccess(metadata, stopwatch, chunks.Count, activity);
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error ingesting markdown document {DocumentId}", metadata.Id);
            RecordFailure(metadata, stopwatch, "exception", activity);
            throw;
        }
    }

    private TagList CreateIngestionTags(DocumentMetadata metadata, string status)
    {
        var tags = new TagList();
        tags.Add("index", options.AzureSearch.IndexName);
        tags.Add("sourceType", metadata.SourceType ?? "manual");
        tags.Add("status", status);
        return tags;
    }

    private void RecordSuccess(DocumentMetadata metadata, Stopwatch stopwatch, int chunkCount, Activity? activity)
    {
        stopwatch.Stop();
        activity?.SetStatus(ActivityStatusCode.Ok);
        activity?.AddEvent(new ActivityEvent("IngestionCompleted", tags: new ActivityTagsCollection
        {
            { "chunks", chunkCount }
        }));

        var tags = CreateIngestionTags(metadata, "success");
        IngestionTelemetry.IngestionDurationSeconds.Record(stopwatch.Elapsed.TotalSeconds, tags);
        IngestionTelemetry.DocumentsIngested.Add(1, tags);
        IngestionTelemetry.DocumentChunksIndexed.Add(chunkCount, tags);
    }

    private void RecordFailure(DocumentMetadata metadata, Stopwatch stopwatch, string reason, Activity? activity)
    {
        stopwatch.Stop();
        activity?.SetStatus(ActivityStatusCode.Error, reason);
        activity?.AddEvent(new ActivityEvent("IngestionFailed", tags: new ActivityTagsCollection
        {
            { "reason", reason }
        }));

        var tags = CreateIngestionTags(metadata, "failed");
        tags.Add("failureReason", reason);

        IngestionTelemetry.IngestionDurationSeconds.Record(stopwatch.Elapsed.TotalSeconds, tags);
        IngestionTelemetry.IngestionFailures.Add(1, tags);
    }

    private static string GenerateDocumentId(string? title)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            var safe = new string(title
                .Trim()
                .ToLowerInvariant()
                .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
                .ToArray());

            safe = string.Join('-', safe.Split('-', StringSplitOptions.RemoveEmptyEntries));

            if (!string.IsNullOrWhiteSpace(safe))
            {
                return safe.Length > 64 ? safe[..64] : safe;
            }
        }

        return $"doc-{Guid.NewGuid():N}";
    }

    private static DocumentMetadata BuildMetadata(string documentId, MarkdownIngestionRequest request)
    {
        return new DocumentMetadata
        {
            Id = documentId,
            Title = string.IsNullOrWhiteSpace(request.Title) ? documentId : request.Title!,
            SourceUrl = request.SourceUrl ?? "manual",
            SourceType = string.IsNullOrWhiteSpace(request.SourceType) ? "manual" : request.SourceType!,
            Description = null,
            Tags = request.Tags ?? [],
            CustomMetadata = request.Metadata ?? []
        };
    }

    private static void MergeFrontmatter(DocumentMetadata metadata, Dictionary<string, object> frontmatter)
    {
        ApplyString(frontmatter, "title", value => metadata.Title = value, skipEmpty: true);
        ApplyString(frontmatter, "description", value => metadata.Description = value);
        ApplyString(frontmatter, "url", value => metadata.SourceUrl = value);
        ApplyString(frontmatter, "sourceType", value => metadata.SourceType = value);

        if (frontmatter.TryGetValue("tags", out var tagsValue))
        {
            var tags = ExtractStringList(tagsValue);
            if (tags.Count > 0)
            {
                metadata.Tags = tags;
            }
        }

        foreach (var kvp in frontmatter)
        {
            metadata.CustomMetadata[kvp.Key] = kvp.Value;
        }
    }

    private static void ApplyString(
        IReadOnlyDictionary<string, object> source,
        string key,
        Action<string> apply,
        bool skipEmpty = false)
    {
        if (!source.TryGetValue(key, out var value) || value is not string stringValue)
        {
            return;
        }

        if (skipEmpty && string.IsNullOrWhiteSpace(stringValue))
        {
            return;
        }

        apply(stringValue);
    }

    private static List<string> ExtractStringList(object value)
    {
        return value switch
        {
            IEnumerable<string> stringEnumerable => stringEnumerable
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList(),
            IEnumerable<object> objectEnumerable => objectEnumerable
                .Select(item => item?.ToString())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!)
                .ToList(),
            string single when !string.IsNullOrWhiteSpace(single) => new List<string> { single },
            _ => []
        };
    }

    private static string StripFrontmatter(string markdown)
    {
        if (!markdown.StartsWith("---", StringComparison.Ordinal))
        {
            return markdown;
        }

        var endIndex = markdown.IndexOf("---", 3, StringComparison.Ordinal);
        if (endIndex < 0)
        {
            return markdown;
        }

        return markdown[(endIndex + 3)..].TrimStart();
    }
}

/// <summary>
/// Result returned after a document was ingested.
/// </summary>
public class DocumentIngestionResult
{
    public bool Success { get; set; }

    public string DocumentId { get; set; } = string.Empty;

    public string? IndexName { get; set; }

    public int ChunksIndexed { get; set; }

    public string? Message { get; set; }
}

/// <summary>
/// Result returned after an OpenAPI specification was ingested.
/// </summary>
public class OpenApiIngestionResult
{
    public bool Success { get; set; }

    public string SpecSource { get; set; } = string.Empty;

    public int EndpointsProcessed { get; set; }

    public int TotalEndpoints { get; set; }

    public int TotalChunksIndexed { get; set; }

    public string? Message { get; set; }

    public List<string> Errors { get; set; } = [];
}

/// <summary>
/// Result returned after blob storage ingestion.
/// </summary>
public class BlobIngestionResult
{
    public bool Success { get; set; }

    public string BlobPath { get; set; } = string.Empty;

    public int TotalFiles { get; set; }

    public int FilesProcessed { get; set; }

    public int TotalChunksIndexed { get; set; }

    public string? Message { get; set; }

    public List<string> Errors { get; set; } = [];
}
