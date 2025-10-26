using System.Diagnostics;
using Azure.Storage.Blobs.Models;
using SemanticHub.IngestionService.Configuration;
using SemanticHub.IngestionService.Domain.Aggregates;
using SemanticHub.IngestionService.Domain.Mappers;
using SemanticHub.IngestionService.Domain.Ports;
using SemanticHub.IngestionService.Domain.Results;
using SemanticHub.IngestionService.Domain.Workflows;
using SemanticHub.IngestionService.Diagnostics;
using SemanticHub.IngestionService.Models;

namespace SemanticHub.IngestionService.Application.Workflows;

/// <summary>
/// Handles bulk ingestion of Markdown, HTML, and OpenAPI content sourced from blob storage.
/// </summary>
public sealed class BulkMarkdownIngestionWorkflow(
    ILogger<BulkMarkdownIngestionWorkflow> logger,
    IBlobStorageService blobStorageService,
    IMarkdownProcessor markdownProcessor,
    IHtmlProcessor htmlProcessor,
    IOpenApiSpecParser openApiSpecParser,
    IIngestionWorkflow<OpenApiSpecificationIngestion, OpenApiIngestionResult> openApiWorkflow,
    IngestionOptions options)
    : IIngestionWorkflow<BulkMarkdownIngestion, BlobIngestionResult>
{
    public async Task<BlobIngestionResult> ExecuteAsync(
        BulkMarkdownIngestion request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var activity = IngestionTelemetry.ActivitySource.StartActivity("Workflow.BulkMarkdownIngestion");
        activity?.SetTag("ingestion.workflow", "blob");
        activity?.SetTag("ingestion.blob.path", request.BlobPath);

        logger.LogInformation("Starting bulk ingestion for blob path {Path}", request.BlobPath);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var blobs = await blobStorageService.GetBlobsAsync(
                request.BlobPath,
                request.ContainerName,
                cancellationToken);

            var supportedBlobs = blobStorageService.FilterBySupportedExtensions(
                blobs,
                ".md", ".markdown", ".yml", ".yaml", ".json", ".html", ".htm");

            if (supportedBlobs.Count == 0)
            {
                stopwatch.Stop();
                activity?.SetStatus(ActivityStatusCode.Error, "NoSupportedFiles");
                logger.LogWarning("No supported files found in blob path {Path}", request.BlobPath);
                return BuildEmptyResult(request);
            }

            activity?.SetTag("ingestion.blob.totalFiles", supportedBlobs.Count);

            var result = CreateResultSkeleton(request, supportedBlobs.Count);
            await ProcessGroupsAsync(supportedBlobs, request, result, cancellationToken);

            stopwatch.Stop();
            activity?.SetTag("ingestion.blob.durationMs", stopwatch.Elapsed.TotalMilliseconds);
            activity?.SetTag("ingestion.blob.filesProcessed", result.FilesProcessed);
            activity?.SetTag("ingestion.blob.totalChunks", result.TotalChunksIndexed);
            activity?.SetStatus(ActivityStatusCode.Ok);

            FinaliseResultMessage(result);
            logger.LogInformation(
                "Completed bulk ingestion for {Path}. Files processed: {Processed}/{Total}",
                request.BlobPath,
                result.FilesProcessed,
                result.TotalFiles);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogError(ex, "Bulk ingestion failed for {Path}", request.BlobPath);
            throw;
        }
    }

    private async Task ProcessGroupsAsync(
        IReadOnlyList<BlobItem> blobs,
        BulkMarkdownIngestion request,
        BlobIngestionResult result,
        CancellationToken cancellationToken)
    {
        var tasks = new List<Task>();

        AddGroupTask(
            tasks,
            FilterByExtensions(blobs, ".md", ".markdown"),
            files => ProcessMarkdownFilesAsync(files, request, result, cancellationToken));

        AddGroupTask(
            tasks,
            FilterByExtensions(blobs, ".html", ".htm"),
            files => ProcessHtmlFilesAsync(files, request, result, cancellationToken));

        AddGroupTask(
            tasks,
            FilterByExtensions(blobs, ".yml", ".yaml", ".json"),
            files => ProcessOpenApiFilesAsync(files, request, result, cancellationToken));

        if (tasks.Count == 0)
        {
            return;
        }

        await Task.WhenAll(tasks);
    }

    private static void AddGroupTask(
        ICollection<Task> tasks,
        IReadOnlyCollection<BlobItem> files,
        Func<IReadOnlyCollection<BlobItem>, Task> processor)
    {
        if (files.Count == 0)
        {
            return;
        }

        tasks.Add(processor(files));
    }

    private async Task ProcessMarkdownFilesAsync(
        IReadOnlyCollection<BlobItem> files,
        BulkMarkdownIngestion request,
        BlobIngestionResult result,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Processing {Count} Markdown files", files.Count);
        await ProcessFilesAsync(
            files,
            result,
            file => ProcessMarkdownFileAsync(file, request, cancellationToken));
    }

    private async Task ProcessHtmlFilesAsync(
        IReadOnlyCollection<BlobItem> files,
        BulkMarkdownIngestion request,
        BlobIngestionResult result,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Processing {Count} HTML files", files.Count);
        await ProcessFilesAsync(
            files,
            result,
            file => ProcessHtmlFileAsync(file, request, cancellationToken));
    }

    private async Task ProcessOpenApiFilesAsync(
        IReadOnlyCollection<BlobItem> files,
        BulkMarkdownIngestion request,
        BlobIngestionResult result,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Processing {Count} OpenAPI files", files.Count);
        await ProcessFilesAsync(
            files,
            result,
            file => ProcessOpenApiFileAsync(file, request, cancellationToken));
    }

    private static async Task ProcessFilesAsync(
        IEnumerable<BlobItem> files,
        BlobIngestionResult result,
        Func<BlobItem, Task<FileIngestionOutcome>> processor)
    {
        foreach (var file in files)
        {
            var outcome = await processor(file);
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
        BlobItem file,
        BulkMarkdownIngestion request,
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
                Tags = request.Metadata.Tags.ToList(),
                Metadata = request.Metadata.CustomMetadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                Content = content
            };

            var ingestionResult = await markdownProcessor.IngestAsync(markdownRequest, cancellationToken);
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

    private async Task<FileIngestionOutcome> ProcessHtmlFileAsync(
        BlobItem file,
        BulkMarkdownIngestion request,
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
                Tags = request.Metadata.Tags.ToList(),
                Metadata = request.Metadata.CustomMetadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                Content = content
            };

            var ingestionResult = await htmlProcessor.IngestHtmlAsync(htmlRequest, cancellationToken);
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

    private async Task<FileIngestionOutcome> ProcessOpenApiFileAsync(
        BlobItem file,
        BulkMarkdownIngestion request,
        CancellationToken cancellationToken)
    {
        try
        {
            var content = await blobStorageService.ReadBlobContentAsync(
                file.Name,
                request.ContainerName,
                cancellationToken);

            if (!openApiSpecParser.LooksLikeSpecification(content))
            {
                logger.LogWarning("File {FileName} is not a valid OpenAPI spec, skipping", file.Name);
                return FileIngestionOutcome.FromFailure($"Skipped {file.Name}: Not a valid OpenAPI specification");
            }

            var openApiRequest = new OpenApiIngestionRequest
            {
                SpecSource = CreateBlobSourceUrl(request, file.Name),
                DocumentIdPrefix = Path.GetFileNameWithoutExtension(file.Name),
                Tags = request.Metadata.Tags.ToList(),
                Metadata = request.Metadata.CustomMetadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
            };

            var domainRequest = openApiRequest.ToDomain();
            var ingestionResult = await openApiWorkflow.ExecuteAsync(domainRequest, cancellationToken);

            return ingestionResult.Success
                ? FileIngestionOutcome.FromSuccess(ingestionResult.TotalChunksIndexed)
                : FileIngestionOutcome.FromFailure($"Failed to ingest {file.Name}: {ingestionResult.Message}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing OpenAPI file {FileName}", file.Name);
            return FileIngestionOutcome.FromFailure($"Error processing {file.Name}: {ex.Message}");
        }
    }

    private static IReadOnlyCollection<BlobItem> FilterByExtensions(
        IEnumerable<BlobItem> blobs,
        params string[] extensions)
    {
        var allowed = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
        return blobs
            .Where(b => allowed.Contains(Path.GetExtension(b.Name) ?? string.Empty))
            .ToList();
    }

    private BlobIngestionResult BuildEmptyResult(BulkMarkdownIngestion request) => new()
    {
        Success = false,
        BlobPath = request.BlobPath,
        TotalFiles = 0,
        FilesProcessed = 0,
        TotalChunksIndexed = 0,
        Message = "No supported files found (.md, .yaml, .json, .html)"
    };

    private static BlobIngestionResult CreateResultSkeleton(BulkMarkdownIngestion request, int totalFiles) => new()
    {
        Success = true,
        BlobPath = request.BlobPath,
        TotalFiles = totalFiles,
        FilesProcessed = 0,
        TotalChunksIndexed = 0
    };

    private static void FinaliseResultMessage(BlobIngestionResult result)
    {
        result.Message = $"Successfully processed {result.FilesProcessed} of {result.TotalFiles} files with {result.TotalChunksIndexed} total chunks.";
        if (result.Errors.Count > 0)
        {
            result.Message += $" {result.Errors.Count} errors occurred.";
        }
    }

    private string CreateBlobSourceUrl(BulkMarkdownIngestion request, string blobName)
    {
        var container = request.ContainerName ?? options.BlobStorage.DefaultContainer;
        return $"blob://{container ?? string.Empty}/{blobName}";
    }

    private readonly record struct FileIngestionOutcome(bool Success, int ChunksIndexed, string? Error)
    {
        public static FileIngestionOutcome FromSuccess(int chunks) => new(true, chunks, null);

        public static FileIngestionOutcome FromFailure(string error) => new(false, 0, error);
    }
}
