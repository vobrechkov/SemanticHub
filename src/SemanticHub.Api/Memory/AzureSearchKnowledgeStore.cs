using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.AI;
using SemanticHub.Api.Configuration;
using System.Text.Json;
using System.Linq;

namespace SemanticHub.Api.Memory;

/// <summary>
/// Azure AI Search backed implementation used for Retrieval Augmented Generation flows.
/// Supports hybrid (vector + semantic) search for high quality grounding.
/// </summary>
public sealed class AzureSearchKnowledgeStore(
    SearchClient searchClient,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    AgentFrameworkOptions options,
    ILogger<AzureSearchKnowledgeStore> logger)
    : IKnowledgeStore
{
    private readonly SearchClient _searchClient = searchClient;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator = embeddingGenerator;
    private readonly AgentFrameworkOptions _options = options;
    private readonly ILogger<AzureSearchKnowledgeStore> _logger = logger;
    private readonly EmbeddingGenerationOptions _embeddingOptions = new();

    private readonly string _keyField = options.Memory.AzureSearch.KeyField;
    private readonly string _contentField = options.Memory.AzureSearch.ContentField;
    private readonly string? _titleField = options.Memory.AzureSearch.TitleField;
    private readonly string? _summaryField = options.Memory.AzureSearch.SummaryField;
    private readonly string? _semanticConfig = options.Memory.AzureSearch.SemanticConfiguration;
    private readonly string? _vectorField = options.Memory.AzureSearch.VectorField;
    private readonly int _vectorKNearestNeighbors = Math.Max(1, options.Memory.AzureSearch.VectorKNearestNeighbors);
    private readonly string _parentDocumentField = options.Memory.AzureSearch.ParentDocumentField;
    private readonly string? _chunkTitleField = options.Memory.AzureSearch.ChunkTitleField;
    private readonly string? _chunkIndexField = options.Memory.AzureSearch.ChunkIndexField;
    private readonly string? _metadataField = options.Memory.AzureSearch.MetadataField;

    private bool VectorSearchEnabled =>
        !string.IsNullOrEmpty(_vectorField) &&
        !string.IsNullOrEmpty(_options.AzureOpenAI.EmbeddingDeployment);

    public async Task<IReadOnlyList<KnowledgeRecord>> SearchAsync(
        string query,
        int limit,
        double minRelevance,
        CancellationToken cancellationToken = default)
    {
        var effectiveLimit = Math.Max(1, limit);
        var effectiveMinRelevance = Math.Clamp(minRelevance, 0.0, 1.0);

        var searchOptions = CreateSearchOptions(effectiveLimit);
        await TryAddVectorQueryAsync(searchOptions, query, cancellationToken);

        var effectiveQuery = string.IsNullOrWhiteSpace(query) ? "*" : query;

        var response = await _searchClient.SearchAsync<SearchDocument>(
            effectiveQuery,
            searchOptions,
            cancellationToken);

        var results = response.Value.GetResults().ToList();
        if (results.Count == 0)
        {
            return [];
        }

        return BuildKnowledgeRecords(results, effectiveMinRelevance);
    }

    private IReadOnlyList<KnowledgeRecord> BuildKnowledgeRecords(
        List<SearchResult<SearchDocument>> results,
        double minRelevance)
    {
        var maxScore = results.Max(r => r.Score ?? 0d);
        var normalizedResults = new List<KnowledgeRecord>(results.Count);

        foreach (var result in results)
        {
            var record = TryCreateKnowledgeRecord(result, maxScore, minRelevance);
            if (record != null)
            {
                normalizedResults.Add(record);
            }
        }

        return normalizedResults;
    }

    private KnowledgeRecord? TryCreateKnowledgeRecord(
        SearchResult<SearchDocument> result,
        double maxScore,
        double minRelevance)
    {
        var normalizedScore = maxScore > 0 ? (result.Score ?? 0d) / maxScore : 0d;
        if (normalizedScore < minRelevance)
        {
            return null;
        }

        var documentId = ResolveDocumentId(result.Document);
        if (string.IsNullOrEmpty(documentId))
        {
            _logger.LogWarning("Search result missing document identifier (key='{KeyField}', parent='{ParentField}')", _keyField, _parentDocumentField);
            return null;
        }

        var content = GetFieldAsString(result.Document, _contentField) ?? string.Empty;
        var title = ResolveOptionalField(result.Document, _titleField);
        var summary = ResolveOptionalField(result.Document, _summaryField);
        var metadata = ExtractMetadata(result.Document);

        return new KnowledgeRecord(
            new KnowledgeDocument(documentId, title, summary),
            content,
            result.Score ?? 0d,
            normalizedScore,
            metadata);
    }

    private static string? ResolveOptionalField(SearchDocument document, string? fieldName)
        => fieldName != null ? GetFieldAsString(document, fieldName) : null;

    public async Task<KnowledgeDocument?> TryGetDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var searchOptions = CreateSearchOptions(limit: 1);
            if (!string.IsNullOrEmpty(_parentDocumentField))
            {
                searchOptions.Filter = $"{_parentDocumentField} eq '{EscapeFilterValue(documentId)}'";
            }

            var response = await _searchClient.SearchAsync<SearchDocument>(
                "*",
                searchOptions,
                cancellationToken);

            var document = response.Value.GetResults().FirstOrDefault()?.Document;
            if (document == null)
            {
                return null;
            }

            var title = _titleField != null ? GetFieldAsString(document, _titleField) : null;
            var summary = _summaryField != null ? GetFieldAsString(document, _summaryField) : null;

            return new KnowledgeDocument(documentId, title, summary);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogDebug("Document with id '{DocumentId}' not found in Azure AI Search", documentId);
            return null;
        }
    }

    public async Task<IReadOnlyList<KnowledgeDocument>> ListDocumentsAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        var documents = new List<KnowledgeDocument>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var searchOptions = CreateSearchOptions(limit * 3); // over-fetch to account for duplicate parent IDs
        searchOptions.OrderBy.Clear();

        var response = await _searchClient.SearchAsync<SearchDocument>(
            "*",
            searchOptions,
            cancellationToken);

        await foreach (var result in response.Value.GetResultsAsync().WithCancellation(cancellationToken))
        {
            var parentId = ResolveDocumentId(result.Document);
            if (string.IsNullOrEmpty(parentId) || !seen.Add(parentId))
            {
                continue;
            }

            var title = _titleField != null ? GetFieldAsString(result.Document, _titleField) : null;
            var summary = _summaryField != null ? GetFieldAsString(result.Document, _summaryField) : null;

            documents.Add(new KnowledgeDocument(parentId, title, summary));

            if (documents.Count >= limit)
            {
                break;
            }
        }

        return documents;
    }

    private SearchOptions CreateSearchOptions(int limit)
    {
        var options = new SearchOptions
        {
            Size = Math.Max(1, limit),
            IncludeTotalCount = true,
            QueryType = SearchQueryType.Simple
        };

        if (!string.IsNullOrEmpty(_semanticConfig))
        {
            options.QueryType = SearchQueryType.Semantic;
            options.SemanticSearch ??= new SemanticSearchOptions();
            options.SemanticSearch.SemanticConfigurationName = _semanticConfig;
        }

        AddSelectField(options, _keyField);
        AddSelectField(options, _contentField);
        AddSelectField(options, _titleField);
        AddSelectField(options, _summaryField);
        AddSelectField(options, _parentDocumentField);
        AddSelectField(options, _chunkTitleField);
        AddSelectField(options, _chunkIndexField);
        AddSelectField(options, _metadataField);
        AddSelectField(options, "sourceUrl");
        AddSelectField(options, "sourceType");
        AddSelectField(options, "ingestedAt");
        AddSelectField(options, "tags");

        return options;
    }

    private async Task TryAddVectorQueryAsync(
        SearchOptions options,
        string query,
        CancellationToken cancellationToken)
    {
        if (!VectorSearchEnabled || string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        try
        {
            var embeddingResult = await _embeddingGenerator.GenerateAsync(
                new[] { query },
                _embeddingOptions,
                cancellationToken);

            if (embeddingResult.Count == 0)
            {
                _logger.LogWarning("Embedding generation returned no data for query '{Query}'", query);
                return;
            }

            var embedding = embeddingResult[0].Vector.ToArray();

            options.VectorSearch ??= new VectorSearchOptions();

            var vectorQuery = new VectorizedQuery(embedding)
            {
                Fields = { _vectorField! },
                KNearestNeighborsCount = _vectorKNearestNeighbors
            };

            options.VectorSearch.Queries.Add(vectorQuery);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Vector query generation failed; falling back to semantic search only.");
        }
    }

    private IReadOnlyDictionary<string, object?> ExtractMetadata(SearchDocument document)
    {
        var metadata = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        AddChunkMetadata(document, metadata);
        AddParentDocumentMetadata(document, metadata);
        AddStandardFields(document, metadata);
        MergeCustomMetadata(document, metadata);

        return metadata;
    }

    private void AddChunkMetadata(SearchDocument document, Dictionary<string, object?> metadata)
    {
        if (!string.IsNullOrEmpty(_chunkTitleField))
        {
            var chunkTitle = GetFieldAsString(document, _chunkTitleField);
            if (!string.IsNullOrEmpty(chunkTitle))
            {
                metadata["chunkTitle"] = chunkTitle;
            }
        }

        if (!string.IsNullOrEmpty(_chunkIndexField))
        {
            var chunkIndexValue = GetField(document, _chunkIndexField);
            if (chunkIndexValue != null)
            {
                metadata["chunkIndex"] = chunkIndexValue;
            }
        }
    }

    private void AddParentDocumentMetadata(SearchDocument document, Dictionary<string, object?> metadata)
    {
        var parentId = GetFieldAsString(document, _parentDocumentField);
        if (!string.IsNullOrEmpty(parentId))
        {
            metadata["parentDocumentId"] = parentId;
        }
    }

    private static void AddStandardFields(SearchDocument document, Dictionary<string, object?> metadata)
    {
        if (document.TryGetValue("sourceUrl", out var sourceUrl) && sourceUrl is not null)
        {
            metadata["sourceUrl"] = sourceUrl;
        }

        if (document.TryGetValue("sourceType", out var sourceType) && sourceType is not null)
        {
            metadata["sourceType"] = sourceType;
        }

        if (document.TryGetValue("ingestedAt", out var ingestedAt) && ingestedAt is not null)
        {
            metadata["ingestedAt"] = ingestedAt;
        }

        if (document.TryGetValue("tags", out var tags) && tags is IEnumerable<object> tagCollection)
        {
            metadata["tags"] = tagCollection.Where(t => t is not null).Select(t => t.ToString()).ToArray();
        }
    }

    private void MergeCustomMetadata(SearchDocument document, Dictionary<string, object?> metadata)
    {
        if (string.IsNullOrEmpty(_metadataField))
        {
            return;
        }

        var metadataJson = GetFieldAsString(document, _metadataField);
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return;
        }

        try
        {
            var dictionary = JsonSerializer.Deserialize<Dictionary<string, object?>>(metadataJson);
            if (dictionary != null)
            {
                foreach (var kvp in dictionary)
                {
                    metadata[kvp.Key] = kvp.Value;
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "Failed to parse metadata JSON from field '{MetadataField}'", _metadataField);
        }
    }

    private string? ResolveDocumentId(SearchDocument document)
    {
        var parentId = GetFieldAsString(document, _parentDocumentField);
        if (!string.IsNullOrWhiteSpace(parentId))
        {
            return parentId;
        }

        return GetFieldAsString(document, _keyField);
    }

    private static string EscapeFilterValue(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);

    private static object? GetField(SearchDocument document, string? fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            return null;
        }

        return document.TryGetValue(fieldName, out var value) ? value : null;
    }

    private static string? GetFieldAsString(SearchDocument document, string? fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            return null;
        }

        if (!document.TryGetValue(fieldName, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            string s => s,
            DateTimeOffset dto => dto.ToString("O"),
            IEnumerable<object> enumerable => string.Join(", ", enumerable.Select(e => e?.ToString()).Where(s => !string.IsNullOrEmpty(s))),
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    private static void AddSelectField(SearchOptions options, string? fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            return;
        }

        if (!options.Select.Contains(fieldName, StringComparer.OrdinalIgnoreCase))
        {
            options.Select.Add(fieldName);
        }
    }
}
