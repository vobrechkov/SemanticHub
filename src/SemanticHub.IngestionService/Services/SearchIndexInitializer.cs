using Azure;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using SemanticHub.IngestionService.Configuration;

namespace SemanticHub.IngestionService.Services;

/// <summary>
/// Ensures the Azure AI Search index required for RAG exists with the correct schema.
/// </summary>
public class SearchIndexInitializer(
    SearchIndexClient indexClient,
    IngestionOptions options,
    ILogger<SearchIndexInitializer> logger)
{
    private const string VectorAlgorithmName = "semantic-vector-config";
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _initialized;

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

            logger.LogInformation("Creating Azure AI Search index '{IndexName}'", options.AzureSearch.IndexName);
            var index = BuildIndexDefinition();
            await indexClient.CreateIndexAsync(index, cancellationToken);

            _initialized = true;
            logger.LogInformation("Azure AI Search index '{IndexName}' created", options.AzureSearch.IndexName);
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
            await indexClient.GetIndexAsync(options.AzureSearch.IndexName, cancellationToken);
            logger.LogDebug("Azure AI Search index '{IndexName}' already exists", options.AzureSearch.IndexName);
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
            new SimpleField(options.AzureSearch.KeyField, SearchFieldDataType.String)
            {
                IsKey = true,
                IsFilterable = true
            },
            new SearchableField(options.AzureSearch.ContentField)
            {
                AnalyzerName = LexicalAnalyzerName.EnLucene
            }
        };

        if (!string.IsNullOrEmpty(options.AzureSearch.TitleField))
        {
            searchFields.Add(new SearchableField(options.AzureSearch.TitleField)
            {
                IsFilterable = true,
                IsSortable = true
            });
        }

        if (!string.IsNullOrEmpty(options.AzureSearch.SummaryField))
        {
            searchFields.Add(new SearchableField(options.AzureSearch.SummaryField)
            {
                AnalyzerName = LexicalAnalyzerName.EnLucene
            });
        }

        if (!string.IsNullOrEmpty(options.AzureSearch.ParentDocumentField))
        {
            searchFields.Add(new SimpleField(options.AzureSearch.ParentDocumentField, SearchFieldDataType.String)
            {
                IsFilterable = true,
                IsSortable = true
            });
        }

        if (!string.IsNullOrEmpty(options.AzureSearch.ChunkTitleField))
        {
            searchFields.Add(new SearchableField(options.AzureSearch.ChunkTitleField)
            {
                AnalyzerName = LexicalAnalyzerName.EnLucene
            });
        }

        if (!string.IsNullOrEmpty(options.AzureSearch.ChunkIndexField))
        {
            searchFields.Add(new SimpleField(options.AzureSearch.ChunkIndexField, SearchFieldDataType.Int32)
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

        if (!string.IsNullOrEmpty(options.AzureSearch.MetadataField))
        {
            searchFields.Add(new SimpleField(options.AzureSearch.MetadataField, SearchFieldDataType.String));
        }

        if (!string.IsNullOrEmpty(options.AzureSearch.VectorField))
        {
            var vectorField = new SearchField(
                options.AzureSearch.VectorField,
                SearchFieldDataType.Collection(SearchFieldDataType.Single))
            {
                IsSearchable = true,
                VectorSearchDimensions = options.AzureSearch.VectorDimensions,
                VectorSearchProfileName = VectorAlgorithmName
            };

            searchFields.Add(vectorField);
        }

        var index = new SearchIndex(options.AzureSearch.IndexName)
        {
            Fields = searchFields
        };

        if (options.AzureSearch.EnableSemanticRanker &&
            !string.IsNullOrEmpty(options.AzureSearch.SemanticConfiguration))
        {
            index.SemanticSearch = new SemanticSearch
            {
                Configurations =
                {
                    new SemanticConfiguration(
                        options.AzureSearch.SemanticConfiguration,
                        new SemanticPrioritizedFields
                        {
                            TitleField = new SemanticField(options.AzureSearch.TitleField),
                            ContentFields =
                            {
                                new SemanticField(options.AzureSearch.ContentField)
                            },
                            KeywordsFields =
                            {
                                new SemanticField("tags")
                            }
                        })
                }
            };
        }

        if (!string.IsNullOrEmpty(options.AzureSearch.VectorField))
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
