using Azure;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using SemanticHub.IngestionService.Configuration;

namespace SemanticHub.IngestionService.Services;

/// <summary>
/// Ensures the Azure AI Search index required for RAG exists with the correct schema.
/// </summary>
public class SearchIndexInitializer
{
    private const string VectorAlgorithmName = "semantic-vector-config";
    private readonly SearchIndexClient _indexClient;
    private readonly IngestionOptions _options;
    private readonly ILogger<SearchIndexInitializer> _logger;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _initialized;

    public SearchIndexInitializer(
        SearchIndexClient indexClient,
        IngestionOptions options,
        ILogger<SearchIndexInitializer> logger)
    {
        _indexClient = indexClient;
        _options = options;
        _logger = logger;
    }

    public async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            if (await IndexExistsAsync(cancellationToken))
            {
                _initialized = true;
                return;
            }

            _logger.LogInformation("Creating Azure AI Search index '{IndexName}'", _options.AzureSearch.IndexName);
            var index = BuildIndexDefinition();
            await _indexClient.CreateIndexAsync(index, cancellationToken);

            _initialized = true;
            _logger.LogInformation("Azure AI Search index '{IndexName}' created", _options.AzureSearch.IndexName);
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    private async Task<bool> IndexExistsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _indexClient.GetIndexAsync(_options.AzureSearch.IndexName, cancellationToken);
            _logger.LogDebug("Azure AI Search index '{IndexName}' already exists", _options.AzureSearch.IndexName);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
    }

    private SearchIndex BuildIndexDefinition()
    {
        var searchFields = new List<SearchField>
        {
            new SimpleField(_options.AzureSearch.KeyField, SearchFieldDataType.String)
            {
                IsKey = true,
                IsFilterable = true
            },
            new SearchableField(_options.AzureSearch.ContentField)
            {
                AnalyzerName = LexicalAnalyzerName.EnLucene
            }
        };

        if (!string.IsNullOrEmpty(_options.AzureSearch.TitleField))
        {
            searchFields.Add(new SearchableField(_options.AzureSearch.TitleField)
            {
                IsFilterable = true,
                IsSortable = true
            });
        }

        if (!string.IsNullOrEmpty(_options.AzureSearch.SummaryField))
        {
            searchFields.Add(new SearchableField(_options.AzureSearch.SummaryField)
            {
                AnalyzerName = LexicalAnalyzerName.EnLucene
            });
        }

        if (!string.IsNullOrEmpty(_options.AzureSearch.ParentDocumentField))
        {
            searchFields.Add(new SimpleField(_options.AzureSearch.ParentDocumentField, SearchFieldDataType.String)
            {
                IsFilterable = true,
                IsSortable = true
            });
        }

        if (!string.IsNullOrEmpty(_options.AzureSearch.ChunkTitleField))
        {
            searchFields.Add(new SearchableField(_options.AzureSearch.ChunkTitleField)
            {
                AnalyzerName = LexicalAnalyzerName.EnLucene
            });
        }

        if (!string.IsNullOrEmpty(_options.AzureSearch.ChunkIndexField))
        {
            searchFields.Add(new SimpleField(_options.AzureSearch.ChunkIndexField, SearchFieldDataType.Int32)
            {
                IsFilterable = true,
                IsSortable = true
            });
        }

        searchFields.Add(new SimpleField("sourceUrl", SearchFieldDataType.String)
        {
            IsFilterable = true
        });

        searchFields.Add(new SimpleField("sourceType", SearchFieldDataType.String)
        {
            IsFilterable = true,
            IsSortable = true
        });

        searchFields.Add(new SimpleField("ingestedAt", SearchFieldDataType.DateTimeOffset)
        {
            IsFilterable = true,
            IsSortable = true
        });

        searchFields.Add(new SearchField("tags", SearchFieldDataType.Collection(SearchFieldDataType.String))
        {
            IsFilterable = true,
            IsFacetable = true
        });

        if (!string.IsNullOrEmpty(_options.AzureSearch.MetadataField))
        {
            searchFields.Add(new SimpleField(_options.AzureSearch.MetadataField, SearchFieldDataType.String));
        }

        if (!string.IsNullOrEmpty(_options.AzureSearch.VectorField))
        {
            var vectorField = new SearchField(
                _options.AzureSearch.VectorField,
                SearchFieldDataType.Collection(SearchFieldDataType.Single))
            {
                IsSearchable = true,
                VectorSearchDimensions = _options.AzureSearch.VectorDimensions,
                VectorSearchProfileName = VectorAlgorithmName
            };

            searchFields.Add(vectorField);
        }

        var index = new SearchIndex(_options.AzureSearch.IndexName)
        {
            Fields = searchFields
        };

        if (!string.IsNullOrEmpty(_options.AzureSearch.SemanticConfiguration))
        {
            index.SemanticSearch = new SemanticSearch
            {
                Configurations =
                {
                    new SemanticConfiguration(
                        _options.AzureSearch.SemanticConfiguration,
                        new SemanticPrioritizedFields
                        {
                            TitleField = new SemanticField(_options.AzureSearch.TitleField),
                            ContentFields =
                            {
                                new SemanticField(_options.AzureSearch.ContentField)
                            },
                            KeywordsFields =
                            {
                                new SemanticField("tags")
                            }
                        })
                }
            };
        }

        if (!string.IsNullOrEmpty(_options.AzureSearch.VectorField))
        {
            index.VectorSearch = new VectorSearch
            {
                Profiles =
                {
                    new VectorSearchProfile(VectorAlgorithmName, VectorAlgorithmName)
                },
                Algorithms =
                {
                    new HnswAlgorithmConfiguration(VectorAlgorithmName)
                }
            };
        }

        return index;
    }
}
