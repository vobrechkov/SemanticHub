using System.Diagnostics;
using System.Diagnostics.Metrics;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using SemanticHub.IngestionService.Configuration;
using SemanticHub.IngestionService.Models;
using SemanticHub.IngestionService.Diagnostics;
using System.Text.Json;

namespace SemanticHub.IngestionService.Services;

/// <summary>
/// Writes chunked documents into Azure AI Search.
/// </summary>
public class AzureSearchIndexer(
    SearchClient searchClient,
    IngestionOptions options,
    ILogger<AzureSearchIndexer> logger)
{
    public async Task UploadChunksAsync(
        IReadOnlyList<DocumentChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        if (chunks.Count == 0)
        {
            return;
        }

        using var activity = IngestionTelemetry.ActivitySource.StartActivity("UploadChunks");
        activity?.SetTag("ingestion.index", options.AzureSearch.IndexName);
        activity?.SetTag("ingestion.chunk.count", chunks.Count);

        var stopwatch = Stopwatch.StartNew();
        var tags = new TagList
        {
            { "index", options.AzureSearch.IndexName },
            { "status", "success" }
        };

        var batch = IndexDocumentsBatch.Create(
            chunks.Select(chunk => IndexDocumentsAction.MergeOrUpload(ToSearchDocument(chunk))).ToArray());

        try
        {
            await searchClient.IndexDocumentsAsync(batch, cancellationToken: cancellationToken);

            stopwatch.Stop();
            activity?.SetStatus(ActivityStatusCode.Ok);

            IngestionTelemetry.SearchUploadSeconds.Record(stopwatch.Elapsed.TotalSeconds, tags);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

            var failureTags = new TagList
            {
                { "index", options.AzureSearch.IndexName },
                { "status", "failed" }
            };

            IngestionTelemetry.SearchUploadSeconds.Record(stopwatch.Elapsed.TotalSeconds, failureTags);

            logger.LogError(ex, "Failed to upload {ChunkCount} chunks to index {IndexName}", chunks.Count, options.AzureSearch.IndexName);
            throw;
        }
    }

    private SearchDocument ToSearchDocument(DocumentChunk chunk)
    {
        var document = CreateBaseDocument(chunk);
        AddOptionalFields(document, chunk);
        AddVectorField(document, chunk);
        AddMetadataField(document, chunk);
        return document;
    }

    private SearchDocument CreateBaseDocument(DocumentChunk chunk)
    {
        return new SearchDocument
        {
            [options.AzureSearch.KeyField] = chunk.Id,
            [options.AzureSearch.ContentField] = chunk.Content,
            ["sourceUrl"] = chunk.Metadata?.SourceUrl,
            ["sourceType"] = chunk.Metadata?.SourceType,
            ["ingestedAt"] = chunk.Metadata?.IngestedAt ?? DateTimeOffset.UtcNow,
            ["tags"] = chunk.Metadata?.Tags ?? []
        };
    }

    private void AddOptionalFields(SearchDocument document, DocumentChunk chunk)
    {
        SetIfConfigured(document, options.AzureSearch.ParentDocumentField, chunk.ParentDocumentId);
        SetIfConfigured(document, options.AzureSearch.TitleField, chunk.Metadata?.Title ?? chunk.Title ?? chunk.ParentDocumentId);
        SetIfConfigured(document, options.AzureSearch.SummaryField, BuildSummary(chunk));
        SetIfConfigured(document, options.AzureSearch.ChunkIndexField, chunk.ChunkIndex);

        if (!string.IsNullOrEmpty(options.AzureSearch.ChunkTitleField))
        {
            SetIfConfigured(document, options.AzureSearch.ChunkTitleField, chunk.Title ?? chunk.Metadata?.Title);
        }
    }

    private void AddVectorField(SearchDocument document, DocumentChunk chunk)
    {
        if (!string.IsNullOrEmpty(options.AzureSearch.VectorField) && chunk.ContentVector is { Length: > 0 })
        {
            document[options.AzureSearch.VectorField] = chunk.ContentVector;
        }
    }

    private void AddMetadataField(SearchDocument document, DocumentChunk chunk)
    {
        if (string.IsNullOrEmpty(options.AzureSearch.MetadataField))
        {
            return;
        }

        var metadataPayload = chunk.Metadata?.CustomMetadata?.Count > 0
            ? JsonSerializer.Serialize(chunk.Metadata.CustomMetadata)
            : null;

        if (metadataPayload is not null)
        {
            document[options.AzureSearch.MetadataField] = metadataPayload;
        }
    }

    private static void SetIfConfigured(SearchDocument document, string? fieldName, object? value)
    {
        if (string.IsNullOrEmpty(fieldName) || value is null)
        {
            return;
        }

        if (value is string s && string.IsNullOrWhiteSpace(s))
        {
            return;
        }

        document[fieldName] = value;
    }

    private static string BuildSummary(DocumentChunk chunk)
    {
        if (!string.IsNullOrWhiteSpace(chunk.Metadata?.Description))
        {
            return chunk.Metadata.Description!;
        }

        if (string.IsNullOrWhiteSpace(chunk.Content))
        {
            return string.Empty;
        }

        var clean = chunk.Content.Replace('\n', ' ');
        return clean.Length <= 320 ? clean : clean[..320] + "...";
    }
}
