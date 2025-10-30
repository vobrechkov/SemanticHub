using System.Diagnostics;
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

        // Filter out empty/whitespace inputs with warning instead of throwing
        var normalizedInputs = new List<string>();
        var filteredCount = 0;

        for (var index = 0; index < inputs.Count; index++)
        {
            var value = inputs[index];
            if (string.IsNullOrWhiteSpace(value))
            {
                _logger.LogWarning(
                    "Embedding input at index {Index} is null or whitespace. Filtering it out.",
                    index);
                filteredCount++;
                continue;
            }

            normalizedInputs.Add(value);
        }

        if (filteredCount > 0)
        {
            _logger.LogWarning(
                "Filtered {FilteredCount} empty inputs out of {TotalCount} total inputs",
                filteredCount,
                inputs.Count);
        }

        if (normalizedInputs.Count == 0)
        {
            _logger.LogError("All embedding inputs were empty or whitespace after filtering");
            throw new ArgumentException("All embedding inputs are null or whitespace.", nameof(inputs));
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

            _logger.LogInformation(
                "Requesting embeddings for {InputCount} inputs using deployment {Deployment}. Batch size limit: {BatchSize}",
                normalizedInputs.Count,
                _options.AzureOpenAI.EmbeddingDeployment,
                MaxEmbeddingsBatchSize);

            for (var offset = 0; offset < normalizedInputs.Count; offset += MaxEmbeddingsBatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var batchSize = Math.Min(MaxEmbeddingsBatchSize, normalizedInputs.Count - offset);
                var batchInputs = normalizedInputs.GetRange(offset, batchSize);
                var batchStopwatch = Stopwatch.StartNew();
                var currentBatch = batchCount + 1;

                _logger.LogDebug(
                    "Embedding batch {BatchNumber} starting. Offset: {Offset}, BatchSize: {BatchSize}",
                    currentBatch,
                    offset,
                    batchSize);

                activity?.AddEvent(new ActivityEvent("EmbeddingBatchStarted", tags: new ActivityTagsCollection
                {
                    { "batchNumber", currentBatch },
                    { "batchSize", batchSize },
                    { "offset", offset }
                }));

                var response = await _embeddingGenerator.GenerateAsync(
                    batchInputs,
                    _embeddingOptions,
                    cancellationToken);

                batchStopwatch.Stop();

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

                activity?.AddEvent(new ActivityEvent("EmbeddingBatchCompleted", tags: new ActivityTagsCollection
                {
                    { "batchNumber", currentBatch },
                    { "batchSize", batchSize },
                    { "responseCount", response.Count },
                    { "durationMs", batchStopwatch.Elapsed.TotalMilliseconds }
                }));

                _logger.LogDebug(
                    "Embedding batch {BatchNumber} completed in {DurationMs} ms with {ResponseCount} responses",
                    currentBatch,
                    batchStopwatch.Elapsed.TotalMilliseconds,
                    response.Count);

                batchCount++;
            }

            // Ensure there are no gaps caused by mismatched counts.
            for (var index = 0; index < embeddings.Length; index++)
            {
                embeddings[index] ??= [];
            }

            stopwatch.Stop();
            var successTags = baseTags;
            successTags.Add("status", "success");

            activity?.SetTag("ingestion.embedding.outputCount", embeddings.Length);
            activity?.SetTag("ingestion.embedding.batchCount", batchCount);
            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.AddEvent(new ActivityEvent("EmbeddingGenerationCompleted", tags: new ActivityTagsCollection
            {
                { "totalDurationMs", stopwatch.Elapsed.TotalMilliseconds },
                { "batchCount", batchCount }
            }));

            IngestionTelemetry.EmbeddingGenerationSeconds.Record(stopwatch.Elapsed.TotalSeconds, successTags);
            IngestionTelemetry.EmbeddingsGenerated.Add(embeddings.Length, successTags);

            return embeddings;
        }
        catch (OperationCanceledException oce)
        {
            stopwatch.Stop();
            var cancelTags = baseTags;
            cancelTags.Add("status", "cancelled");

            activity?.SetStatus(ActivityStatusCode.Error, "cancelled");
            activity?.AddEvent(new ActivityEvent("EmbeddingGenerationCancelled", tags: new ActivityTagsCollection
            {
                { "elapsedMs", stopwatch.Elapsed.TotalMilliseconds }
            }));

            IngestionTelemetry.EmbeddingGenerationSeconds.Record(stopwatch.Elapsed.TotalSeconds, cancelTags);

            _logger.LogWarning(oce, "Embedding generation was cancelled for deployment {Deployment}", _options.AzureOpenAI.EmbeddingDeployment);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var failureTags = baseTags;
            failureTags.Add("status", "failed");

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddEvent(new ActivityEvent("EmbeddingGenerationFailed", tags: new ActivityTagsCollection
            {
                { "elapsedMs", stopwatch.Elapsed.TotalMilliseconds },
                { "exceptionType", ex.GetType().Name }
            }));

            IngestionTelemetry.EmbeddingGenerationSeconds.Record(stopwatch.Elapsed.TotalSeconds, failureTags);

            _logger.LogError(ex, "Failed to generate embeddings using deployment {Deployment}", _options.AzureOpenAI.EmbeddingDeployment);
            throw;
        }
    }
}
