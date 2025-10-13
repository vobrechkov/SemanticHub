using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.AI;
using SemanticHub.IngestionService.Configuration;
using SemanticHub.IngestionService.Diagnostics;

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

        using var activity = IngestionTelemetry.ActivitySource.StartActivity("GenerateEmbeddings");
        activity?.SetTag("ingestion.embedding.deployment", _options.AzureOpenAI.EmbeddingDeployment);
        activity?.SetTag("ingestion.embedding.inputCount", inputs.Count);

        var stopwatch = Stopwatch.StartNew();
        var baseTags = new TagList
        {
            { "deployment", _options.AzureOpenAI.EmbeddingDeployment }
        };

        try
        {
            var response = await _embeddingGenerator.GenerateAsync(
                inputs,
                _embeddingOptions,
                cancellationToken);

            var embeddings = new List<float[]>(response.Count);

            foreach (var item in response)
            {
                embeddings.Add(item.Vector.ToArray());
            }

            stopwatch.Stop();
            var successTags = baseTags;
            successTags.Add("status", "success");

            activity?.SetTag("ingestion.embedding.outputCount", embeddings.Count);
            activity?.SetStatus(ActivityStatusCode.Ok);

            IngestionTelemetry.EmbeddingGenerationSeconds.Record(stopwatch.Elapsed.TotalSeconds, successTags);
            IngestionTelemetry.EmbeddingsGenerated.Add(embeddings.Count, successTags);

            if (embeddings.Count != inputs.Count)
            {
                _logger.LogWarning("Embedding count {EmbeddingCount} does not match input count {InputCount}", embeddings.Count, inputs.Count);
            }

            return embeddings;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var failureTags = baseTags;
            failureTags.Add("status", "failed");

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

            IngestionTelemetry.EmbeddingGenerationSeconds.Record(stopwatch.Elapsed.TotalSeconds, failureTags);

            _logger.LogError(ex, "Failed to generate embeddings using deployment {Deployment}", _options.AzureOpenAI.EmbeddingDeployment);
            throw;
        }
    }
}
