using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using SemanticHub.IngestionService.Configuration;
using SemanticHub.IngestionService.Models;
using System.Text.Json;

namespace SemanticHub.IngestionService.Services;

/// <summary>
/// Writes chunked documents into Azure AI Search.
/// </summary>
public class AzureSearchIndexer
{
    private readonly SearchClient _searchClient;
    private readonly IngestionOptions _options;
    private readonly ILogger<AzureSearchIndexer> _logger;

    public AzureSearchIndexer(
        SearchClient searchClient,
        IngestionOptions options,
        ILogger<AzureSearchIndexer> logger)
    {
        _searchClient = searchClient;
        _options = options;
        _logger = logger;
    }

    public async Task UploadChunksAsync(
        IReadOnlyList<DocumentChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        if (chunks.Count == 0)
        {
            return;
        }

        var batch = IndexDocumentsBatch.Create(
            chunks.Select(chunk => IndexDocumentsAction.MergeOrUpload(ToSearchDocument(chunk))).ToArray());

        await _searchClient.IndexDocumentsAsync(batch, cancellationToken: cancellationToken);
    }

    private SearchDocument ToSearchDocument(DocumentChunk chunk)
    {
        var document = new SearchDocument
        {
            [_options.AzureSearch.KeyField] = chunk.Id,
            [_options.AzureSearch.ContentField] = chunk.Content,
            ["sourceUrl"] = chunk.Metadata?.SourceUrl,
            ["sourceType"] = chunk.Metadata?.SourceType,
            ["ingestedAt"] = chunk.Metadata?.IngestedAt ?? DateTimeOffset.UtcNow,
            ["tags"] = chunk.Metadata?.Tags ?? new List<string>()
        };

        SetIfConfigured(_options.AzureSearch.ParentDocumentField, chunk.ParentDocumentId);
        SetIfConfigured(_options.AzureSearch.TitleField, chunk.Metadata?.Title ?? chunk.Title ?? chunk.ParentDocumentId);
        SetIfConfigured(_options.AzureSearch.SummaryField, BuildSummary(chunk));
        SetIfConfigured(_options.AzureSearch.ChunkIndexField, chunk.ChunkIndex);

        if (!string.IsNullOrEmpty(_options.AzureSearch.ChunkTitleField))
        {
            SetIfConfigured(_options.AzureSearch.ChunkTitleField, chunk.Title ?? chunk.Metadata?.Title);
        }

        if (!string.IsNullOrEmpty(_options.AzureSearch.VectorField) && chunk.ContentVector is { Length: > 0 })
        {
            document[_options.AzureSearch.VectorField] = chunk.ContentVector;
        }

        if (!string.IsNullOrEmpty(_options.AzureSearch.MetadataField))
        {
            var metadataPayload = chunk.Metadata?.CustomMetadata?.Count > 0
                ? JsonSerializer.Serialize(chunk.Metadata.CustomMetadata)
                : null;

            if (metadataPayload is not null)
            {
                document[_options.AzureSearch.MetadataField] = metadataPayload;
            }
        }

        return document;

        void SetIfConfigured(string? fieldName, object? value)
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
