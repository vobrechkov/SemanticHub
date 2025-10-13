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

        if (string.IsNullOrWhiteSpace(request.Url))
        {
            throw new ArgumentException("Request URL must not be empty.", nameof(request));
        }

        using var activity = IngestionTelemetry.ActivitySource.StartActivity("IngestWebPage");
        activity?.SetTag("ingestion.sourceType", "webpage");
        activity?.SetTag("ingestion.request.url", request.Url);

        logger.LogInformation("Scraping and ingesting web page: {Url}", request.Url);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var scrapedPage = await webScraperTool.ScrapeSinglePageAsync(request.Url, cancellationToken);
            activity?.SetTag("ingestion.scrape.statusCode", scrapedPage.StatusCode);

            var scrapeTags = CreateWebPageTags(scrapedPage.Url ?? request.Url, scrapedPage.IsSuccess ? "success" : "failed");

            if (!scrapedPage.IsSuccess || string.IsNullOrWhiteSpace(scrapedPage.HtmlContent))
            {
                activity?.SetStatus(ActivityStatusCode.Error, "ScrapeFailed");
                IngestionTelemetry.WebPagesScraped.Add(1, scrapeTags);

                logger.LogWarning("Failed to scrape content from {Url}. Status code: {StatusCode}", request.Url, scrapedPage.StatusCode);
                return BuildFailedWebPageResult(request);
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

            return await IngestMarkdownAsync(markdownRequest, cancellationToken);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            IngestionTelemetry.WebPagesScraped.Add(1, CreateWebPageTags(request.Url, "failed"));

            logger.LogError(ex, "Error ingesting web page {Url}", request.Url);
            throw;
        }
    }

    public async Task<DocumentIngestionResult> IngestHtmlAsync(
        HtmlIngestionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            throw new ArgumentException("Request content must not be empty.", nameof(request));
        }

        using var activity = IngestionTelemetry.ActivitySource.StartActivity("IngestHtml");
        activity?.SetTag("ingestion.sourceType", "html");

        logger.LogInformation("Ingesting HTML content");

        try
        {
            // Create a scraped page model from the HTML content
            var scrapedPage = new ScrapedPage
            {
                Url = request.SourceUrl ?? "manual",
                Title = request.Title ?? "Untitled HTML Document",
                HtmlContent = request.Content,
                StatusCode = 200,
                ScrapedAt = DateTime.UtcNow,
                Metadata = new Dictionary<string, string>()
            };

            // Convert HTML to Markdown
            var markdownContent = markdownConverter.ConvertToMarkdown(scrapedPage);
            activity?.SetTag("ingestion.html.contentLength", markdownContent.Length);

            // Build Markdown request
            var markdownRequest = new MarkdownIngestionRequest
            {
                DocumentId = request.DocumentId,
                Title = request.Title ?? scrapedPage.Title,
                SourceUrl = request.SourceUrl,
                SourceType = "html",
                Tags = request.Tags,
                Metadata = request.Metadata,
                Content = markdownContent
            };

            return await IngestMarkdownAsync(markdownRequest, cancellationToken);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogError(ex, "Error ingesting HTML content");
            throw;
        }
    }

    public async Task<BlobIngestionResult> IngestFromBlobAsync(
        BlobIngestionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.BlobPath))
        {
            throw new ArgumentException("Request BlobPath must not be empty.", nameof(request));
        }

        using var activity = IngestionTelemetry.ActivitySource.StartActivity("IngestFromBlob");
        activity?.SetTag("ingestion.sourceType", "blob");
        activity?.SetTag("ingestion.blobPath", request.BlobPath);

        logger.LogInformation("Ingesting documents from blob path: {Path}", request.BlobPath);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Get all blobs matching the path
            var allBlobs = await blobStorageService.GetBlobsAsync(
                request.BlobPath,
                request.ContainerName,
                cancellationToken);

            // Filter by supported extensions
            var supportedBlobs = blobStorageService.FilterBySupportedExtensions(
                allBlobs,
                ".md", ".markdown", ".yml", ".yaml", ".json", ".html", ".htm");

            if (supportedBlobs.Count == 0)
            {
                logger.LogWarning("No supported files found in blob path: {Path}", request.BlobPath);
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

            activity?.SetTag("ingestion.blob.totalFiles", supportedBlobs.Count);

            var result = new BlobIngestionResult
            {
                Success = true,
                BlobPath = request.BlobPath,
                TotalFiles = supportedBlobs.Count,
                FilesProcessed = 0,
                TotalChunksIndexed = 0
            };

            // Process files in parallel by type
            var tasks = new List<Task>();

            var markdownFiles = supportedBlobs.Where(b =>
                b.Name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
                b.Name.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase)).ToList();

            var yamlFiles = supportedBlobs.Where(b =>
                b.Name.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ||
                b.Name.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)).ToList();

            var jsonFiles = supportedBlobs.Where(b =>
                b.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)).ToList();

            var htmlFiles = supportedBlobs.Where(b =>
                b.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
                b.Name.EndsWith(".htm", StringComparison.OrdinalIgnoreCase)).ToList();

            // Process each file type in parallel
            if (markdownFiles.Count > 0)
            {
                tasks.Add(ProcessMarkdownFilesAsync(markdownFiles, request, result, cancellationToken));
            }

            if (yamlFiles.Count > 0 || jsonFiles.Count > 0)
            {
                var openApiFiles = yamlFiles.Concat(jsonFiles).ToList();
                tasks.Add(ProcessOpenApiFilesAsync(openApiFiles, request, result, cancellationToken));
            }

            if (htmlFiles.Count > 0)
            {
                tasks.Add(ProcessHtmlFilesAsync(htmlFiles, request, result, cancellationToken));
            }

            await Task.WhenAll(tasks);

            stopwatch.Stop();
            activity?.SetTag("ingestion.blob.filesProcessed", result.FilesProcessed);
            activity?.SetTag("ingestion.blob.totalChunks", result.TotalChunksIndexed);
            activity?.SetTag("ingestion.blob.durationMs", stopwatch.Elapsed.TotalMilliseconds);

            result.Message = $"Successfully processed {result.FilesProcessed} of {result.TotalFiles} files with {result.TotalChunksIndexed} total chunks.";

            if (result.Errors.Count > 0)
            {
                result.Message += $" {result.Errors.Count} errors occurred.";
            }

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

    private async Task ProcessMarkdownFilesAsync(
        List<Azure.Storage.Blobs.Models.BlobItem> files,
        BlobIngestionRequest request,
        BlobIngestionResult result,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Processing {Count} Markdown files", files.Count);

        foreach (var file in files)
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
                    SourceUrl = $"blob://{request.ContainerName ?? options.BlobStorage.DefaultContainer}/{file.Name}",
                    SourceType = "blob-markdown",
                    Tags = request.Tags,
                    Metadata = request.Metadata,
                    Content = content
                };

                var ingestionResult = await IngestMarkdownAsync(markdownRequest, cancellationToken);

                if (ingestionResult.Success)
                {
                    lock (result)
                    {
                        result.FilesProcessed++;
                        result.TotalChunksIndexed += ingestionResult.ChunksIndexed;
                    }
                }
                else
                {
                    lock (result.Errors)
                    {
                        result.Errors.Add($"Failed to ingest {file.Name}: {ingestionResult.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing Markdown file {FileName}", file.Name);
                lock (result.Errors)
                {
                    result.Errors.Add($"Error processing {file.Name}: {ex.Message}");
                }
            }
        }
    }

    private async Task ProcessOpenApiFilesAsync(
        List<Azure.Storage.Blobs.Models.BlobItem> files,
        BlobIngestionRequest request,
        BlobIngestionResult result,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Processing {Count} OpenAPI files", files.Count);

        foreach (var file in files)
        {
            try
            {
                var content = await blobStorageService.ReadBlobContentAsync(
                    file.Name,
                    request.ContainerName,
                    cancellationToken);

                // Verify it's an OpenAPI spec
                if (!IsOpenApiSpec(content))
                {
                    logger.LogWarning("File {FileName} does not appear to be an OpenAPI specification, skipping", file.Name);
                    lock (result.Errors)
                    {
                        result.Errors.Add($"Skipped {file.Name}: Not a valid OpenAPI specification");
                    }
                    continue;
                }

                // Save content to temp file for OpenAPI parser
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

                    if (ingestionResult.Success)
                    {
                        lock (result)
                        {
                            result.FilesProcessed++;
                            result.TotalChunksIndexed += ingestionResult.TotalChunksIndexed;
                        }
                    }
                    else
                    {
                        lock (result.Errors)
                        {
                            result.Errors.Add($"Failed to ingest {file.Name}: {ingestionResult.Message}");
                        }
                    }
                }
                finally
                {
                    // Clean up temp file
                    if (File.Exists(tempFile))
                    {
                        File.Delete(tempFile);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing OpenAPI file {FileName}", file.Name);
                lock (result.Errors)
                {
                    result.Errors.Add($"Error processing {file.Name}: {ex.Message}");
                }
            }
        }
    }

    private async Task ProcessHtmlFilesAsync(
        List<Azure.Storage.Blobs.Models.BlobItem> files,
        BlobIngestionRequest request,
        BlobIngestionResult result,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Processing {Count} HTML files", files.Count);

        foreach (var file in files)
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
                    SourceUrl = $"blob://{request.ContainerName ?? options.BlobStorage.DefaultContainer}/{file.Name}",
                    Tags = request.Tags,
                    Metadata = request.Metadata,
                    Content = content
                };

                var ingestionResult = await IngestHtmlAsync(htmlRequest, cancellationToken);

                if (ingestionResult.Success)
                {
                    lock (result)
                    {
                        result.FilesProcessed++;
                        result.TotalChunksIndexed += ingestionResult.ChunksIndexed;
                    }
                }
                else
                {
                    lock (result.Errors)
                    {
                        result.Errors.Add($"Failed to ingest {file.Name}: {ingestionResult.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing HTML file {FileName}", file.Name);
                lock (result.Errors)
                {
                    result.Errors.Add($"Error processing {file.Name}: {ex.Message}");
                }
            }
        }
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

        if (string.IsNullOrWhiteSpace(request.SpecSource))
        {
            throw new ArgumentException("Request SpecSource must not be empty.", nameof(request));
        }

        using var activity = IngestionTelemetry.ActivitySource.StartActivity("IngestOpenApi");
        activity?.SetTag("ingestion.sourceType", "openapi");
        activity?.SetTag("ingestion.request.specSource", request.SpecSource);

        logger.LogInformation("Ingesting OpenAPI spec from: {Source}", request.SpecSource);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Parse OpenAPI specification
            var endpoints = await openApiIngestionTool.ParseOpenApiSpecAsync(request.SpecSource, cancellationToken);
            activity?.SetTag("ingestion.openapi.totalEndpoints", endpoints.Count);

            if (endpoints.Count == 0)
            {
                activity?.SetStatus(ActivityStatusCode.Error, "NoEndpoints");
                IngestionTelemetry.IngestionFailures.Add(1, CreateOpenApiTags(request, "no-endpoints"));

                logger.LogWarning("No endpoints found in OpenAPI spec: {Source}", request.SpecSource);
                return new OpenApiIngestionResult
                {
                    Success = false,
                    SpecSource = request.SpecSource,
                    EndpointsProcessed = 0,
                    TotalChunksIndexed = 0,
                    Message = "No endpoints found in the OpenAPI specification."
                };
            }

            IngestionTelemetry.OpenApiEndpointsProcessed.Add(endpoints.Count, CreateOpenApiTags(request, "parsed"));

            logger.LogInformation("Found {Count} endpoints in OpenAPI spec", endpoints.Count);

            // Convert endpoints to Markdown
            var markdownDocs = openApiIngestionTool.ConvertEndpointsToMarkdown(endpoints);

            var successfulIngestions = 0;
            var totalChunks = 0;
            var errors = new List<string>();

            // Ingest each endpoint as a separate document
        for (var i = 0; i < endpoints.Count && i < markdownDocs.Count; i++)
        {
            var endpoint = endpoints[i];
            var markdown = markdownDocs[i];
            Activity? endpointActivity = null;

            try
            {
                endpointActivity = IngestionTelemetry.ActivitySource.StartActivity("IngestOpenApiEndpoint");
                endpointActivity?.SetTag("ingestion.openapi.method", endpoint.Method);
                endpointActivity?.SetTag("ingestion.openapi.path", endpoint.Path);

                var documentId = string.IsNullOrWhiteSpace(request.DocumentIdPrefix)
                    ? endpoint.Id
                    : $"{request.DocumentIdPrefix}_{endpoint.Id}";

                var markdownRequest = new MarkdownIngestionRequest
                {
                    DocumentId = documentId,
                    Title = $"{endpoint.Method} {endpoint.Path}",
                    SourceUrl = request.SpecSource,
                    SourceType = "openapi",
                    Tags = request.Tags,
                    Metadata = request.Metadata,
                    Content = markdown
                };

                var result = await IngestMarkdownAsync(markdownRequest, cancellationToken);

                if (result.Success)
                {
                    successfulIngestions++;
                    totalChunks += result.ChunksIndexed;
                    endpointActivity?.SetStatus(ActivityStatusCode.Ok);
                }
                else
                {
                    endpointActivity?.SetStatus(ActivityStatusCode.Error, result.Message);
                    errors.Add($"Failed to ingest {endpoint.Method} {endpoint.Path}: {result.Message}");
                }
            }
            catch (Exception ex)
            {
                endpointActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                logger.LogError(ex, "Error ingesting endpoint {Method} {Path}", endpoint.Method, endpoint.Path);
                errors.Add($"Error ingesting {endpoint.Method} {endpoint.Path}: {ex.Message}");
                activity?.AddEvent(new ActivityEvent("EndpointIngestionFailed", tags: new ActivityTagsCollection
                {
                    { "method", endpoint.Method },
                    { "path", endpoint.Path },
                    { "error", ex.Message }
                }));
            }
            finally
            {
                endpointActivity?.Dispose();
            }
        }

            stopwatch.Stop();
            activity?.SetTag("ingestion.openapi.processed", successfulIngestions);
            activity?.SetTag("ingestion.openapi.totalChunks", totalChunks);
            activity?.SetTag("ingestion.openapi.durationMs", stopwatch.Elapsed.TotalMilliseconds);

            var success = successfulIngestions > 0;
            var message = success
                ? $"Successfully ingested {successfulIngestions} of {endpoints.Count} endpoints with {totalChunks} total chunks."
                : "Failed to ingest any endpoints.";

            if (errors.Any())
            {
                message += $" Errors: {string.Join("; ", errors)}";
            }

            var resultTags = CreateOpenApiTags(request, success ? "success" : "failed");
            if (success)
            {
                IngestionTelemetry.OpenApiEndpointsProcessed.Add(successfulIngestions, CreateOpenApiTags(request, "indexed"));
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
                SpecSource = request.SpecSource,
                EndpointsProcessed = successfulIngestions,
                TotalEndpoints = endpoints.Count,
                TotalChunksIndexed = totalChunks,
                Message = message,
                Errors = errors
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            IngestionTelemetry.IngestionFailures.Add(1, CreateOpenApiTags(request, "failed"));

            logger.LogError(ex, "Error ingesting OpenAPI spec {Source}", request.SpecSource);
            throw;
        }
    }

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
        if (frontmatter.TryGetValue("title", out var title) && title is string titleValue && !string.IsNullOrWhiteSpace(titleValue))
        {
            metadata.Title = titleValue;
        }

        if (frontmatter.TryGetValue("description", out var description) && description is string descValue)
        {
            metadata.Description = descValue;
        }

        if (frontmatter.TryGetValue("url", out var url) && url is string urlValue)
        {
            metadata.SourceUrl = urlValue;
        }

        if (frontmatter.TryGetValue("sourceType", out var sourceType) && sourceType is string sourceTypeValue)
        {
            metadata.SourceType = sourceTypeValue;
        }

        if (frontmatter.TryGetValue("tags", out var tagsValue) && tagsValue is IEnumerable<object> tags)
        {
            metadata.Tags = tags.Select(t => t?.ToString() ?? string.Empty).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        }

        foreach (var kvp in frontmatter)
        {
            metadata.CustomMetadata[kvp.Key] = kvp.Value;
        }
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
