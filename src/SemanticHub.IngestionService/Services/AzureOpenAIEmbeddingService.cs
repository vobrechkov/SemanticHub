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
    private const int MaxEmbeddingsBatchSize = 16;

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

        var normalizedInputs = new List<string>(inputs.Count);
        for (var index = 0; index < inputs.Count; index++)
        {
            var value = inputs[index];
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException($"Embedding input at index {index} is null or whitespace.", nameof(inputs));
            }

            normalizedInputs.Add(value);
        }

        using var activity = IngestionTelemetry.ActivitySource.StartActivity("GenerateEmbeddings");
        activity?.SetTag("ingestion.embedding.deployment", _options.AzureOpenAI.EmbeddingDeployment);
        activity?.SetTag("ingestion.embedding.inputCount", normalizedInputs.Count);

        var stopwatch = Stopwatch.StartNew();
        var baseTags = new TagList
        {
            { "deployment", _options.AzureOpenAI.EmbeddingDeployment }
        };

        try
        {
            var embeddings = new float[normalizedInputs.Count][];
            var batchCount = 0;

            for (var offset = 0; offset < normalizedInputs.Count; offset += MaxEmbeddingsBatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var batchSize = Math.Min(MaxEmbeddingsBatchSize, normalizedInputs.Count - offset);
                var batchInputs = normalizedInputs.GetRange(offset, batchSize);
                var response = await _embeddingGenerator.GenerateAsync(
                    batchInputs,
                    _embeddingOptions,
                    cancellationToken);

                if (response.Count != batchInputs.Count)
                {
                    _logger.LogWarning(
                        "Embedding response count {ResponseCount} does not match request count {RequestCount}",
                        response.Count,
                        batchInputs.Count);
                }

                for (var index = 0; index < response.Count; index++)
                {
                    embeddings[offset + index] = response[index].Vector.ToArray();
                }

                batchCount++;
            }

            // Ensure there are no gaps caused by mismatched counts.
            for (var index = 0; index < embeddings.Length; index++)
            {
                embeddings[index] ??= Array.Empty<float>();
            }

            stopwatch.Stop();
            var successTags = baseTags;
            successTags.Add("status", "success");

            activity?.SetTag("ingestion.embedding.outputCount", embeddings.Length);
            activity?.SetTag("ingestion.embedding.batchCount", batchCount);
            activity?.SetStatus(ActivityStatusCode.Ok);

            IngestionTelemetry.EmbeddingGenerationSeconds.Record(stopwatch.Elapsed.TotalSeconds, successTags);
            IngestionTelemetry.EmbeddingsGenerated.Add(embeddings.Length, successTags);

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
