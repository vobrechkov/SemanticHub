using OpenAI.Embeddings;
using SemanticHub.IngestionService.Configuration;

namespace SemanticHub.IngestionService.Services;

/// <summary>
/// Generates embeddings using Azure OpenAI deployment.
/// </summary>
public class AzureOpenAIEmbeddingService
{
    private readonly EmbeddingClient _embeddingClient;
    private readonly EmbeddingGenerationOptions _embeddingOptions;
    private readonly IngestionOptions _options;
    private readonly ILogger<AzureOpenAIEmbeddingService> _logger;

    public AzureOpenAIEmbeddingService(
        EmbeddingClient embeddingClient,
        IngestionOptions options,
        ILogger<AzureOpenAIEmbeddingService> logger)
    {
        _embeddingClient = embeddingClient ?? throw new ArgumentNullException(nameof(embeddingClient));
        _options = options;
        _logger = logger;
        _embeddingOptions = new EmbeddingGenerationOptions
        {
            Dimensions = options.AzureSearch.VectorDimensions > 0
                ? options.AzureSearch.VectorDimensions
                : null
        };
    }

    public async Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default)
    {
        if (inputs.Count == 0)
        {
            return Array.Empty<float[]>();
        }

        if (string.IsNullOrEmpty(_options.AzureOpenAI.EmbeddingDeployment))
        {
            throw new InvalidOperationException("Azure OpenAI embedding deployment is not configured.");
        }

        var response = await _embeddingClient.GenerateEmbeddingsAsync(
            inputs,
            _embeddingOptions,
            cancellationToken);

        var collection = response.Value;
        var embeddings = new List<float[]>(collection.Count);

        foreach (var item in collection)
        {
            embeddings.Add(item.ToFloats().ToArray());
        }

        if (embeddings.Count != inputs.Count)
        {
            _logger.LogWarning("Embedding count {EmbeddingCount} does not match input count {InputCount}", embeddings.Count, inputs.Count);
        }

        return embeddings;
    }
}
