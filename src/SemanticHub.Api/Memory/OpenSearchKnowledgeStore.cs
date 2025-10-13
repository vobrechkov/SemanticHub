using Microsoft.Extensions.AI;
using OpenSearch.Client;
using SemanticHub.Api.Configuration;
using System.Text.Json;

namespace SemanticHub.Api.Memory;

/// <summary>
/// OpenSearch/Elasticsearch backed knowledge store implementation for hybrid retrieval.
/// </summary>
public sealed class OpenSearchKnowledgeStore(
    IOpenSearchClient client,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    AgentFrameworkOptions options,
    ILogger<OpenSearchKnowledgeStore> logger)
    : IKnowledgeStore
{
    private readonly IOpenSearchClient _client = client;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator = embeddingGenerator;
    private readonly AgentFrameworkOptions _options = options;
    private readonly ILogger<OpenSearchKnowledgeStore> _logger = logger;
    private readonly EmbeddingGenerationOptions _embeddingOptions = new();

    private OpenSearchMemoryOptions Config => _options.Memory.OpenSearch;

    private bool VectorSearchEnabled =>
        !string.IsNullOrEmpty(Config.VectorField) &&
        !string.IsNullOrEmpty(_options.AzureOpenAI.EmbeddingDeployment);

    public async Task<IReadOnlyList<KnowledgeRecord>> SearchAsync(
        string query,
        int limit,
        double minRelevance,
        CancellationToken cancellationToken = default)
    {
        var effectiveLimit = Math.Max(1, limit);
        var effectiveMinRelevance = Math.Clamp(minRelevance, 0.0, 1.0);

        QueryContainer baseQuery = string.IsNullOrWhiteSpace(query)
            ? new MatchAllQuery()
            : BuildMultiMatchQuery(query);

        double[]? embedding = null;
        if (VectorSearchEnabled && !string.IsNullOrWhiteSpace(query))
        {
            embedding = await TryGenerateEmbeddingAsync(query, cancellationToken);
            if (embedding is null)
            {
                _logger.LogWarning("Embedding generation returned no data for query '{Query}'", query);
            }
        }

        QueryContainer finalQuery = baseQuery;
        if (embedding is not null)
        {
            finalQuery = new ScriptScoreQuery
            {
                Query = baseQuery,
                Script = new InlineScript($"cosineSimilarity(params.query_vector, doc['{Config.VectorField}']) + 1.0")
                {
                    Params = new Dictionary<string, object>
                    {
                        ["query_vector"] = embedding
                    }
                }
            };
        }

        var response = await _client.SearchAsync<Dictionary<string, object?>>(
            s => s.Index(Config.IndexName)
                .TrackTotalHits()
                .Size(effectiveLimit)
                .Source(source => source.Includes(i => i.Fields(GetSelectFields().ToArray())))
                .Query(_ => finalQuery),
            cancellationToken);

        if (!response.IsValid)
        {
            _logger.LogError("OpenSearch query failed: {Error}", response.ServerError?.Error?.Reason ?? response.OriginalException?.Message);
            return [];
        }

        if (response.Hits.Count == 0)
        {
            return [];
        }

        var maxScore = response.Hits.Max(h => h.Score ?? 0d);
        var records = new List<KnowledgeRecord>(response.Hits.Count);

        foreach (var hit in response.Hits)
        {
            if (hit.Source is null)
            {
                continue;
            }

            var normalizedScore = maxScore > 0 ? (hit.Score ?? 0d) / maxScore : 0d;
            if (normalizedScore < effectiveMinRelevance)
            {
                continue;
            }

            var documentId = ResolveDocumentId(hit.Source);
            if (string.IsNullOrEmpty(documentId))
            {
                _logger.LogWarning("Search result missing document identifier (key='{KeyField}', parent='{ParentField}')", Config.KeyField, Config.ParentDocumentField);
                continue;
            }

            var content = GetFieldAsString(hit.Source, Config.ContentField) ?? string.Empty;
            var title = GetFieldAsString(hit.Source, Config.TitleField);
            var summary = GetFieldAsString(hit.Source, Config.SummaryField);
            var metadata = ExtractMetadata(hit.Source);

            records.Add(new KnowledgeRecord(
                new KnowledgeDocument(documentId, title, summary),
                content,
                hit.Score ?? 0d,
                normalizedScore,
                metadata));
        }

        return records;
    }

    public async Task<KnowledgeDocument?> TryGetDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        var response = await _client.SearchAsync<Dictionary<string, object?>>(
            s => s.Index(Config.IndexName)
                .Size(1)
                .Source(source => source.Includes(i => i.Fields(GetSelectFields().ToArray())))
                .Query(q => q.Bool(b => b
                    .Should(
                        sh => sh.Term(t => t.Field(Config.ParentDocumentField).Value(documentId)),
                        sh => sh.Term(t => t.Field(Config.KeyField).Value(documentId)))
                    .MinimumShouldMatch(1))),
            cancellationToken);

        if (!response.IsValid || response.Hits.Count == 0)
        {
            return null;
        }

        var source = response.Hits.FirstOrDefault()?.Source;
        if (source is null)
        {
            return null;
        }

        var title = GetFieldAsString(source, Config.TitleField);
        var summary = GetFieldAsString(source, Config.SummaryField);

        return new KnowledgeDocument(documentId, title, summary);
    }

    public async Task<IReadOnlyList<KnowledgeDocument>> ListDocumentsAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        var documents = new List<KnowledgeDocument>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var response = await _client.SearchAsync<Dictionary<string, object?>>(
            s => s.Index(Config.IndexName)
                .TrackTotalHits(false)
                .Size(Math.Max(1, limit * 3))
                .Source(source => source.Includes(i => i.Fields(GetSelectFields().ToArray())))
                .Query(q => new MatchAllQuery()),
            cancellationToken);

        if (!response.IsValid || response.Hits.Count == 0)
        {
            return [];
        }

        foreach (var hit in response.Hits)
        {
            if (hit.Source is null)
            {
                continue;
            }

            var parentId = ResolveDocumentId(hit.Source);
            if (string.IsNullOrEmpty(parentId) || !seen.Add(parentId))
            {
                continue;
            }

            var title = GetFieldAsString(hit.Source, Config.TitleField);
            var summary = GetFieldAsString(hit.Source, Config.SummaryField);
            documents.Add(new KnowledgeDocument(parentId, title, summary));

            if (documents.Count >= limit)
            {
                break;
            }
        }

        return documents;
    }

    private QueryContainer BuildMultiMatchQuery(string query)
    {
        var fields = new List<Field> { Config.ContentField };

        if (!string.IsNullOrEmpty(Config.TitleField))
        {
            fields.Add(Config.TitleField);
        }

        if (!string.IsNullOrEmpty(Config.SummaryField))
        {
            fields.Add(Config.SummaryField);
        }

        return new MultiMatchQuery
        {
            Query = query,
            Fields = fields.ToArray(),
            Type = TextQueryType.BestFields
        };
    }

    private async Task<double[]?> TryGenerateEmbeddingAsync(
        string query,
        CancellationToken cancellationToken)
    {
        try
        {
            var embeddingResult = await _embeddingGenerator.GenerateAsync(
                new[] { query },
                _embeddingOptions,
                cancellationToken);

            if (embeddingResult.Count == 0)
            {
                return null;
            }

            var vector = embeddingResult[0].Vector.ToArray();
            return Array.ConvertAll(vector, static v => (double)v);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Vector query generation failed; falling back to text-only search.");
            return null;
        }
    }

    private IEnumerable<string> GetSelectFields()
    {
        yield return Config.KeyField;
        yield return Config.ContentField;

        if (!string.IsNullOrEmpty(Config.TitleField))
        {
            yield return Config.TitleField!;
        }

        if (!string.IsNullOrEmpty(Config.SummaryField))
        {
            yield return Config.SummaryField!;
        }

        if (!string.IsNullOrEmpty(Config.ParentDocumentField))
        {
            yield return Config.ParentDocumentField!;
        }

        if (!string.IsNullOrEmpty(Config.ChunkTitleField))
        {
            yield return Config.ChunkTitleField!;
        }

        if (!string.IsNullOrEmpty(Config.ChunkIndexField))
        {
            yield return Config.ChunkIndexField!;
        }

        if (!string.IsNullOrEmpty(Config.MetadataField))
        {
            yield return Config.MetadataField!;
        }

        yield return "sourceUrl";
        yield return "sourceType";
        yield return "ingestedAt";
        yield return "tags";
    }

    private IReadOnlyDictionary<string, object?> ExtractMetadata(IDictionary<string, object?> source)
    {
        var metadata = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrEmpty(Config.ChunkTitleField))
        {
            var chunkTitle = GetFieldAsString(source, Config.ChunkTitleField);
            if (!string.IsNullOrEmpty(chunkTitle))
            {
                metadata["chunkTitle"] = chunkTitle;
            }
        }

        if (!string.IsNullOrEmpty(Config.ChunkIndexField))
        {
            if (source.TryGetValue(Config.ChunkIndexField!, out var chunkIndexValue) && chunkIndexValue is not null)
            {
                metadata["chunkIndex"] = chunkIndexValue;
            }
        }

        var parentId = GetFieldAsString(source, Config.ParentDocumentField);
        if (!string.IsNullOrEmpty(parentId))
        {
            metadata["parentDocumentId"] = parentId;
        }

        if (source.TryGetValue("sourceUrl", out var sourceUrl) && sourceUrl is not null)
        {
            metadata["sourceUrl"] = sourceUrl;
        }

        if (source.TryGetValue("sourceType", out var sourceType) && sourceType is not null)
        {
            metadata["sourceType"] = sourceType;
        }

        if (source.TryGetValue("ingestedAt", out var ingestedAt) && ingestedAt is not null)
        {
            metadata["ingestedAt"] = ingestedAt;
        }

        if (source.TryGetValue("tags", out var tags) && tags is IEnumerable<object> tagCollection)
        {
            metadata["tags"] = tagCollection.Where(t => t is not null).Select(t => t?.ToString()).ToArray();
        }

        if (!string.IsNullOrEmpty(Config.MetadataField))
        {
            var metadataJson = GetFieldAsString(source, Config.MetadataField);
            if (!string.IsNullOrWhiteSpace(metadataJson))
            {
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
                    _logger.LogDebug(ex, "Failed to parse metadata JSON from field '{MetadataField}'", Config.MetadataField);
                }
            }
        }

        return metadata;
    }

    private static string? ResolveDocumentId(IDictionary<string, object?> source, string fieldName)
        => GetFieldAsString(source, fieldName);

    private string? ResolveDocumentId(IDictionary<string, object?> source)
    {
        var parentId = ResolveDocumentId(source, Config.ParentDocumentField);
        if (!string.IsNullOrWhiteSpace(parentId))
        {
            return parentId;
        }

        return ResolveDocumentId(source, Config.KeyField);
    }

    private static string? GetFieldAsString(IDictionary<string, object?> source, string? fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            return null;
        }

        if (!source.TryGetValue(fieldName, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            string s => s,
            DateTimeOffset dto => dto.ToString("O"),
            IEnumerable<object> enumerable => string.Join(", ", enumerable.Select(e => e?.ToString()).Where(static s => !string.IsNullOrEmpty(s))),
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
        };
    }
}
