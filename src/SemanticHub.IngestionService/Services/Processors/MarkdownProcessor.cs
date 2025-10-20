using System.Diagnostics;
using SemanticHub.IngestionService.Configuration;
using SemanticHub.IngestionService.Diagnostics;
using SemanticHub.IngestionService.Models;
using SemanticHub.IngestionService.Domain.Ports;
using SemanticHub.IngestionService.Domain.Results;

namespace SemanticHub.IngestionService.Services.Processors;

/// <summary>
/// Handles ingestion of Markdown documents by chunking, embedding, and indexing content.
/// </summary>
public class MarkdownProcessor(
    ILogger<MarkdownProcessor> logger,
    SearchIndexInitializer indexInitializer,
    SemanticChunker chunker,
    AzureOpenAIEmbeddingService embeddingService,
    AzureSearchIndexer indexer,
    IngestionOptions options,
    IMarkdownConverter markdownConverter) : IMarkdownProcessor
{
    public async Task<DocumentIngestionResult> IngestAsync(
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
            var frontmatter = markdownConverter.ParseFrontmatter(request.Content);
            if (frontmatter != null)
            {
                MergeFrontmatter(metadata, frontmatter);
                activity?.AddEvent(new ActivityEvent("FrontmatterMerged"));
            }

            var content = StripFrontmatter(request.Content);

            logger.LogInformation(
                "Chunking document {DocumentId}. Length: {Length} characters",
                metadata.Id,
                content.Length);
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

            // Filter out any chunks with empty content as additional defense
            var validChunks = chunks.Where(c => !string.IsNullOrWhiteSpace(c.Content)).ToList();
            if (validChunks.Count < chunks.Count)
            {
                logger.LogWarning(
                    "Filtered {FilteredCount} empty chunks for document {DocumentId}",
                    chunks.Count - validChunks.Count,
                    metadata.Id);
            }

            if (validChunks.Count == 0)
            {
                logger.LogWarning("No valid chunks after filtering for document {DocumentId}", metadata.Id);

                var failureResult = new DocumentIngestionResult
                {
                    Success = false,
                    DocumentId = metadata.Id,
                    IndexName = options.AzureSearch.IndexName,
                    ChunksIndexed = 0,
                    Message = "No valid content chunks after filtering."
                };

                RecordFailure(metadata, stopwatch, "no_valid_chunks", activity);
                return failureResult;
            }

            var embeddings = await embeddingService.GenerateEmbeddingsAsync(
                validChunks.Select(c => c.Content).ToList(),
                cancellationToken);

            activity?.SetTag("ingestion.embedding.count", embeddings.Count);

            for (var i = 0; i < validChunks.Count && i < embeddings.Count; i++)
            {
                validChunks[i].ContentVector = embeddings[i];
            }

            logger.LogInformation(
                "Uploading {Count} chunks for document {DocumentId}",
                validChunks.Count,
                metadata.Id);

            await indexer.UploadChunksAsync(validChunks, cancellationToken);

            var result = new DocumentIngestionResult
            {
                Success = true,
                DocumentId = metadata.Id,
                IndexName = options.AzureSearch.IndexName,
                ChunksIndexed = validChunks.Count,
                Message = $"Document '{metadata.Id}' ingested into index '{options.AzureSearch.IndexName}'."
            };

            RecordSuccess(metadata, stopwatch, validChunks.Count, activity);
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
