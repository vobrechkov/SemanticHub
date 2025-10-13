using SemanticHub.IngestionService.Configuration;
using SemanticHub.IngestionService.Models;

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
    MarkdownConverter markdownConverter)
{
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

        // Parse YAML frontmatter if provided to enrich metadata.
        var frontmatter = markdownConverter.ParseFrontmatter(request.Content);
        if (frontmatter != null)
        {
            MergeFrontmatter(metadata, frontmatter);
        }

        var content = StripFrontmatter(request.Content);

        logger.LogInformation("Chunking document {DocumentId}. Length: {Length} characters", metadata.Id, content.Length);

        var chunks = chunker.ChunkMarkdown(content, metadata.Id, metadata);
        if (chunks.Count == 0)
        {
            return new DocumentIngestionResult
            {
                Success = false,
                DocumentId = metadata.Id,
                IndexName = options.AzureSearch.IndexName,
                ChunksIndexed = 0,
                Message = "No content chunks produced."
            };
        }

        var embeddings = await embeddingService.GenerateEmbeddingsAsync(
            chunks.Select(c => c.Content).ToList(),
            cancellationToken);

        for (var i = 0; i < chunks.Count && i < embeddings.Count; i++)
        {
            chunks[i].ContentVector = embeddings[i];
        }

        logger.LogInformation("Uploading {Count} chunks for document {DocumentId}", chunks.Count, metadata.Id);

        await indexer.UploadChunksAsync(chunks, cancellationToken);

        return new DocumentIngestionResult
        {
            Success = true,
            DocumentId = metadata.Id,
            IndexName = options.AzureSearch.IndexName,
            ChunksIndexed = chunks.Count,
            Message = $"Document '{metadata.Id}' ingested into index '{options.AzureSearch.IndexName}'."
        };
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
