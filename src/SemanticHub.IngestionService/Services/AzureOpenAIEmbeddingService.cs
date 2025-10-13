using Microsoft.Extensions.AI;
using SemanticHub.IngestionService.Configuration;

namespace SemanticHub.IngestionService.Services;

/// <summary>
/// Generates embeddings using Azure OpenAI deployment.
/// </summary>
public class AzureOpenAIEmbeddingService
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly EmbeddingGenerationOptions _embeddingOptions;
    private readonly IngestionOptions _options;
    private readonly ILogger<AzureOpenAIEmbeddingService> _logger;

    public AzureOpenAIEmbeddingService(
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IngestionOptions options,
        ILogger<AzureOpenAIEmbeddingService> logger)
    {
        _embeddingGenerator = embeddingGenerator;
        _options = options;
        _logger = logger;
        _embeddingOptions = new EmbeddingGenerationOptions();

        if (options.AzureSearch.VectorDimensions > 0)
        {
            _embeddingOptions.AdditionalProperties ??= [];
            _embeddingOptions.AdditionalProperties["dimensions"] = options.AzureSearch.VectorDimensions;
        }
    }

    public async Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default)
    {
        if (inputs.Count == 0)
        {
            return [];
        }

        if (string.IsNullOrEmpty(_options.AzureOpenAI.EmbeddingDeployment))
        {
            throw new InvalidOperationException("Azure OpenAI embedding deployment is not configured.");
        }

        var response = await _embeddingGenerator.GenerateAsync(
            inputs,
            _embeddingOptions,
            cancellationToken);

        var embeddings = new List<float[]>(response.Count);

        foreach (var item in response)
        {
            embeddings.Add(item.Vector.ToArray());
        }

        if (embeddings.Count != inputs.Count)
        {
            _logger.LogWarning("Embedding count {EmbeddingCount} does not match input count {InputCount}", embeddings.Count, inputs.Count);
        }

        return embeddings;
    }
}
