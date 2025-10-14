using System.Diagnostics;
using SemanticHub.IngestionService.Diagnostics;
using SemanticHub.IngestionService.Models;
using SemanticHub.IngestionService.Tools;
using SemanticHub.IngestionService.Domain.Ports;
using SemanticHub.IngestionService.Domain.Results;

namespace SemanticHub.IngestionService.Services.Processors;

public interface IOpenApiProcessor
{
    Task<OpenApiIngestionResult> IngestAsync(
        OpenApiIngestionRequest request,
        CancellationToken cancellationToken = default);

    bool LooksLikeSpecification(string content);
}

/// <summary>
/// Handles ingestion of OpenAPI specifications by parsing endpoints and delegating markdown ingestion.
/// </summary>
public class OpenApiProcessor(
    ILogger<OpenApiProcessor> logger,
    OpenApiIngestionTool ingestionTool,
    IMarkdownProcessor markdownProcessor) : IOpenApiProcessor
{
    public async Task<OpenApiIngestionResult> IngestAsync(
        OpenApiIngestionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateOpenApiRequest(request);

        using var activity = IngestionTelemetry.ActivitySource.StartActivity("IngestOpenApi");
        activity?.SetTag("ingestion.sourceType", "openapi");
        activity?.SetTag("ingestion.request.specSource", request.SpecSource);

        logger.LogInformation("Ingesting OpenAPI spec from: {Source}", request.SpecSource);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var endpoints = await ParseOpenApiEndpointsAsync(request, activity, cancellationToken);
            if (endpoints.Count == 0)
            {
                stopwatch.Stop();
                return BuildEmptyOpenApiResult(request, activity);
            }

            var markdownDocs = ingestionTool.ConvertEndpointsToMarkdown(endpoints);
            var summary = await IngestOpenApiEndpointsAsync(
                request,
                endpoints,
                markdownDocs,
                activity,
                cancellationToken);

            stopwatch.Stop();
            UpdateOpenApiTelemetry(activity, summary, stopwatch);

            return BuildOpenApiResult(request, endpoints.Count, summary, activity);
        }
        catch (Exception ex)
        {
            HandleOpenApiIngestionException(ex, request, activity, stopwatch);
            throw;
        }
    }

    public bool LooksLikeSpecification(string content)
    {
        return content.Contains("openapi:", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("swagger:", StringComparison.OrdinalIgnoreCase) ||
               (content.Contains("\"openapi\"", StringComparison.OrdinalIgnoreCase) &&
                content.Contains("\"info\"", StringComparison.OrdinalIgnoreCase)) ||
               (content.Contains("\"swagger\"", StringComparison.OrdinalIgnoreCase) &&
                content.Contains("\"info\"", StringComparison.OrdinalIgnoreCase));
    }

    private static void ValidateOpenApiRequest(OpenApiIngestionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SpecSource))
        {
            throw new ArgumentException("Request SpecSource must not be empty.", nameof(request));
        }
    }

    private async Task<List<OpenApiEndpoint>> ParseOpenApiEndpointsAsync(
        OpenApiIngestionRequest request,
        Activity? activity,
        CancellationToken cancellationToken)
    {
        var endpoints = await ingestionTool.ParseOpenApiSpecAsync(request.SpecSource!, cancellationToken);
        activity?.SetTag("ingestion.openapi.totalEndpoints", endpoints.Count);

        if (endpoints.Count > 0)
        {
            IngestionTelemetry.OpenApiEndpointsProcessed.Add(endpoints.Count, CreateOpenApiTags(request, "parsed"));
            logger.LogInformation("Found {Count} endpoints in OpenAPI spec", endpoints.Count);
            return endpoints;
        }

        activity?.SetStatus(ActivityStatusCode.Error, "NoEndpoints");
        IngestionTelemetry.IngestionFailures.Add(1, CreateOpenApiTags(request, "no-endpoints"));
        logger.LogWarning("No endpoints found in OpenAPI spec: {Source}", request.SpecSource);
        return endpoints;
    }

    private static OpenApiIngestionResult BuildEmptyOpenApiResult(
        OpenApiIngestionRequest request,
        Activity? activity)
    {
        activity?.SetStatus(ActivityStatusCode.Error, "NoEndpoints");

        return new OpenApiIngestionResult
        {
            Success = false,
            SpecSource = request.SpecSource!,
            EndpointsProcessed = 0,
            TotalEndpoints = 0,
            TotalChunksIndexed = 0,
            Message = "No endpoints found in the OpenAPI specification."
        };
    }

    private async Task<EndpointIngestionSummary> IngestOpenApiEndpointsAsync(
        OpenApiIngestionRequest request,
        IReadOnlyList<OpenApiEndpoint> endpoints,
        IReadOnlyList<string> markdownDocs,
        Activity? activity,
        CancellationToken cancellationToken)
    {
        var successful = 0;
        var totalChunks = 0;
        var errors = new List<string>();

        var count = Math.Min(endpoints.Count, markdownDocs.Count);
        for (var i = 0; i < count; i++)
        {
            var outcome = await IngestSingleEndpointAsync(
                request,
                endpoints[i],
                markdownDocs[i],
                activity,
                cancellationToken);

            if (outcome.Success)
            {
                successful++;
                totalChunks += outcome.Chunks;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(outcome.Error))
            {
                errors.Add(outcome.Error);
            }
        }

        return new EndpointIngestionSummary(successful, totalChunks, errors);
    }

    private async Task<EndpointIngestionOutcome> IngestSingleEndpointAsync(
        OpenApiIngestionRequest request,
        OpenApiEndpoint endpoint,
        string markdown,
        Activity? parentActivity,
        CancellationToken cancellationToken)
    {
        using var endpointActivity = IngestionTelemetry.ActivitySource.StartActivity("IngestOpenApiEndpoint");
        endpointActivity?.SetTag("ingestion.openapi.method", endpoint.Method);
        endpointActivity?.SetTag("ingestion.openapi.path", endpoint.Path);

        try
        {
            var markdownRequest = BuildMarkdownRequestFromEndpoint(request, endpoint, markdown);
            var result = await markdownProcessor.IngestAsync(markdownRequest, cancellationToken);

            if (result.Success)
            {
                endpointActivity?.SetStatus(ActivityStatusCode.Ok);
                return EndpointIngestionOutcome.FromSuccess(result.ChunksIndexed);
            }

            endpointActivity?.SetStatus(ActivityStatusCode.Error, result.Message);
            return EndpointIngestionOutcome.FromFailure($"Failed to ingest {endpoint.Method} {endpoint.Path}: {result.Message}");
        }
        catch (Exception ex)
        {
            endpointActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogError(ex, "Error ingesting endpoint {Method} {Path}", endpoint.Method, endpoint.Path);
            parentActivity?.AddEvent(new ActivityEvent("EndpointIngestionFailed", tags: new ActivityTagsCollection
            {
                { "method", endpoint.Method },
                { "path", endpoint.Path },
                { "error", ex.Message }
            }));

            return EndpointIngestionOutcome.FromFailure($"Error ingesting {endpoint.Method} {endpoint.Path}: {ex.Message}");
        }
    }

    private static MarkdownIngestionRequest BuildMarkdownRequestFromEndpoint(
        OpenApiIngestionRequest request,
        OpenApiEndpoint endpoint,
        string markdown)
    {
        var documentId = string.IsNullOrWhiteSpace(request.DocumentIdPrefix)
            ? endpoint.Id
            : $"{request.DocumentIdPrefix}_{endpoint.Id}";

        return new MarkdownIngestionRequest
        {
            DocumentId = documentId,
            Title = $"{endpoint.Method} {endpoint.Path}",
            SourceUrl = request.SpecSource,
            SourceType = "openapi",
            Tags = request.Tags,
            Metadata = request.Metadata,
            Content = markdown
        };
    }

    private static void UpdateOpenApiTelemetry(
        Activity? activity,
        EndpointIngestionSummary summary,
        Stopwatch stopwatch)
    {
        activity?.SetTag("ingestion.openapi.processed", summary.SuccessfulCount);
        activity?.SetTag("ingestion.openapi.totalChunks", summary.TotalChunks);
        activity?.SetTag("ingestion.openapi.durationMs", stopwatch.Elapsed.TotalMilliseconds);
    }

    private OpenApiIngestionResult BuildOpenApiResult(
        OpenApiIngestionRequest request,
        int totalEndpoints,
        EndpointIngestionSummary summary,
        Activity? activity)
    {
        var success = summary.SuccessfulCount > 0;
        var message = success
            ? $"Successfully ingested {summary.SuccessfulCount} of {totalEndpoints} endpoints with {summary.TotalChunks} total chunks."
            : "Failed to ingest any endpoints.";

        if (summary.Errors.Count > 0)
        {
            message += $" Errors: {string.Join("; ", summary.Errors)}";
        }

        var statusTag = success ? "success" : "failed";
        var resultTags = CreateOpenApiTags(request, statusTag);

        if (success)
        {
            IngestionTelemetry.OpenApiEndpointsProcessed.Add(summary.SuccessfulCount, CreateOpenApiTags(request, "indexed"));
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        else
        {
            IngestionTelemetry.IngestionFailures.Add(1, resultTags);
            activity?.SetStatus(ActivityStatusCode.Error, "NoEndpointsIndexed");
        }

        return new OpenApiIngestionResult
        {
            Success = success,
            SpecSource = request.SpecSource!,
            EndpointsProcessed = summary.SuccessfulCount,
            TotalEndpoints = totalEndpoints,
            TotalChunksIndexed = summary.TotalChunks,
            Message = message,
            Errors = summary.Errors
        };
    }

    private void HandleOpenApiIngestionException(
        Exception exception,
        OpenApiIngestionRequest request,
        Activity? activity,
        Stopwatch stopwatch)
    {
        stopwatch.Stop();
        activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
        IngestionTelemetry.IngestionFailures.Add(1, CreateOpenApiTags(request, "failed"));
        logger.LogError(exception, "Error ingesting OpenAPI spec {Source}", request.SpecSource);
    }

    private TagList CreateOpenApiTags(OpenApiIngestionRequest request, string status)
    {
        var tags = new TagList();
        tags.Add("status", status);
        tags.Add("sourceType", "openapi");
        if (!string.IsNullOrWhiteSpace(request.SpecSource) &&
            Uri.TryCreate(request.SpecSource, UriKind.Absolute, out var uri))
        {
            tags.Add("host", uri.Host);
        }

        return tags;
    }

    private readonly record struct EndpointIngestionOutcome(bool Success, int Chunks, string? Error)
    {
        public static EndpointIngestionOutcome FromSuccess(int chunks) => new(true, chunks, null);
        public static EndpointIngestionOutcome FromFailure(string? error) => new(false, 0, error);
    }

    private sealed record EndpointIngestionSummary(int SuccessfulCount, int TotalChunks, List<string> Errors);
}
