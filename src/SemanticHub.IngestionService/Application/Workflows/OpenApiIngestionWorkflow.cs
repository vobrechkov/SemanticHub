using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using Microsoft.Extensions.Logging;
using SemanticHub.IngestionService.Diagnostics;
using SemanticHub.IngestionService.Domain.Aggregates;
using SemanticHub.IngestionService.Domain.OpenApi;
using SemanticHub.IngestionService.Domain.Ports;
using SemanticHub.IngestionService.Domain.Results;
using SemanticHub.IngestionService.Domain.Workflows;
using SemanticHub.IngestionService.Models;

namespace SemanticHub.IngestionService.Application.Workflows;

/// <summary>
/// Coordinates the ingestion of OpenAPI specifications into the knowledge store.
/// </summary>
public sealed class OpenApiIngestionWorkflow(
    ILogger<OpenApiIngestionWorkflow> logger,
    IOpenApiSpecLocator specLocator,
    IOpenApiSpecParser specParser,
    IOpenApiMarkdownGenerator markdownGenerator,
    IOpenApiDocumentSplitter documentSplitter,
    IMarkdownProcessor markdownProcessor)
    : IIngestionWorkflow<OpenApiSpecificationIngestion, OpenApiIngestionResult>
{
    public async Task<OpenApiIngestionResult> ExecuteAsync(
        OpenApiSpecificationIngestion request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var activity = IngestionTelemetry.ActivitySource.StartActivity("Workflow.OpenApiIngestion");
        activity?.SetTag("ingestion.workflow", "openapi");
        activity?.SetTag("ingestion.openapi.specSource", request.SpecSource);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var specDocument = await specLocator.LocateAsync(request, cancellationToken);
            var specification = await specParser.ParseAsync(specDocument, cancellationToken);

            if (specification.Endpoints.Count == 0)
            {
                stopwatch.Stop();
                activity?.SetStatus(ActivityStatusCode.Error, "NoEndpoints");
                logger.LogWarning("No endpoints discovered in OpenAPI specification {Source}", request.SpecSource);

                return new OpenApiIngestionResult
                {
                    Success = false,
                    SpecSource = specDocument.Source,
                    EndpointsProcessed = 0,
                    TotalEndpoints = 0,
                    TotalChunksIndexed = 0,
                    Message = "No endpoints found in the OpenAPI specification.",
                    Errors = new List<string>
                    {
                        "The specification did not contain any operations."
                    }
                };
            }

            var summary = await IngestEndpointsAsync(request, specDocument, specification, activity, cancellationToken);

            stopwatch.Stop();
            activity?.SetTag("ingestion.openapi.processed", summary.SuccessfulEndpoints);
            activity?.SetTag("ingestion.openapi.totalEndpoints", specification.Endpoints.Count);
            activity?.SetTag("ingestion.openapi.totalChunks", summary.TotalChunksIndexed);
            activity?.SetTag("ingestion.openapi.durationMs", stopwatch.Elapsed.TotalMilliseconds);
            activity?.SetStatus(summary.SuccessfulEndpoints > 0 ? ActivityStatusCode.Ok : ActivityStatusCode.Error);

            if (summary.SuccessfulEndpoints == 0)
            {
                IngestionTelemetry.IngestionFailures.Add(1, CreateTelemetryTags(request, "failed"));
            }

            return BuildResult(specDocument, specification, summary, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            IngestionTelemetry.IngestionFailures.Add(1, CreateTelemetryTags(request, "exception"));
            logger.LogError(ex, "OpenAPI ingestion workflow failed for {Source}", request.SpecSource);
            throw;
        }
    }

    private async Task<IngestionSummary> IngestEndpointsAsync(
        OpenApiSpecificationIngestion request,
        OpenApiSpecDocument specDocument,
        OpenApiSpecificationDocument specification,
        Activity? parentActivity,
        CancellationToken cancellationToken)
    {
        var summary = new IngestionSummary(specification.Endpoints.Count);

        foreach (var endpoint in specification.Endpoints)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var endpointActivity = IngestionTelemetry.ActivitySource.StartActivity("Workflow.OpenApiIngestion.Endpoint");
            endpointActivity?.SetTag("ingestion.openapi.method", endpoint.Method);
            endpointActivity?.SetTag("ingestion.openapi.path", endpoint.Path);

            try
            {
                var markdown = markdownGenerator.Generate(specification, endpoint);
                var documents = documentSplitter.Split(specification, endpoint, markdown);

                var endpointOutcome = await IngestEndpointDocumentsAsync(
                    request,
                    specDocument,
                    specification,
                    endpoint,
                    documents,
                    cancellationToken);

                if (endpointOutcome.Success)
                {
                    summary.RegisterSuccess(endpointOutcome.ChunksIndexed);
                    endpointActivity?.SetStatus(ActivityStatusCode.Ok);
                    IngestionTelemetry.OpenApiEndpointsProcessed.Add(
                        1,
                        CreateTelemetryTags(request, "indexed", endpoint));
                }
                else
                {
                    summary.RegisterFailure(endpointOutcome.ErrorMessage);
                    endpointActivity?.SetStatus(ActivityStatusCode.Error, endpointOutcome.ErrorMessage);
                    IngestionTelemetry.IngestionFailures.Add(
                        1,
                        CreateTelemetryTags(request, "endpoint-failed", endpoint));
                }
            }
            catch (Exception ex)
            {
                var message = $"Error ingesting {endpoint.Method} {endpoint.Path}: {ex.Message}";
                summary.RegisterFailure(message);
                endpointActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                IngestionTelemetry.IngestionFailures.Add(1, CreateTelemetryTags(request, "endpoint-exception", endpoint));
                parentActivity?.AddEvent(new ActivityEvent(
                    "OpenApiEndpointFailed",
                    tags: new ActivityTagsCollection
                    {
                        { "method", endpoint.Method },
                        { "path", endpoint.Path },
                        { "error", ex.Message }
                    }));

                logger.LogError(ex, "Error ingesting endpoint {Method} {Path}", endpoint.Method, endpoint.Path);
            }
        }

        return summary;
    }

    private async Task<EndpointOutcome> IngestEndpointDocumentsAsync(
        OpenApiSpecificationIngestion request,
        OpenApiSpecDocument specDocument,
        OpenApiSpecificationDocument specification,
        Models.OpenApiEndpoint endpoint,
        IReadOnlyList<OpenApiEndpointDocument> documents,
        CancellationToken cancellationToken)
    {
        var allSucceeded = true;
        var totalChunks = 0;
        var errors = new List<string>();

        foreach (var document in documents)
        {
            var markdownRequest = BuildMarkdownRequest(request, specDocument, specification, document);
            var result = await markdownProcessor.IngestAsync(markdownRequest, cancellationToken);

            if (result.Success)
            {
                totalChunks += result.ChunksIndexed;
                continue;
            }

            allSucceeded = false;
            var message = $"Failed to ingest {endpoint.Method} {endpoint.Path}: {result.Message}";
            errors.Add(message);
            logger.LogWarning(message);
        }

        var errorMessage = string.Join("; ", errors);

        return allSucceeded
            ? EndpointOutcome.CreateSuccess(totalChunks)
            : EndpointOutcome.CreateFailure(errorMessage, totalChunks);
    }

    private MarkdownIngestionRequest BuildMarkdownRequest(
        OpenApiSpecificationIngestion request,
        OpenApiSpecDocument specDocument,
        OpenApiSpecificationDocument specification,
        OpenApiEndpointDocument document)
    {
        var endpoint = document.Endpoint;
        var documentId = BuildDocumentId(request, endpoint, document);

        var tags = request.Metadata.Tags
            .Concat(endpoint.Tags)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var metadata = new Dictionary<string, object>(request.Metadata.CustomMetadata, StringComparer.OrdinalIgnoreCase)
        {
            ["openapi:method"] = endpoint.Method,
            ["openapi:path"] = endpoint.Path,
            ["openapi:specVersion"] = specification.Version,
            ["openapi:segmentIndex"] = document.SegmentIndex,
            ["openapi:segmentCount"] = document.TotalSegments
        };

        if (!string.IsNullOrWhiteSpace(endpoint.OperationId))
        {
            metadata["openapi:operationId"] = endpoint.OperationId!;
        }

        if (!string.IsNullOrWhiteSpace(specification.Title))
        {
            metadata["openapi:specTitle"] = specification.Title;
        }

        return new MarkdownIngestionRequest
        {
            DocumentId = documentId,
            Title = BuildDocumentTitle(endpoint, document),
            SourceUrl = specDocument.SourceUri?.ToString() ?? specDocument.Source,
            SourceType = request.Metadata.SourceType,
            Tags = tags,
            Metadata = metadata,
            Content = document.Markdown
        };
    }

    private static string BuildDocumentId(
        OpenApiSpecificationIngestion request,
        Models.OpenApiEndpoint endpoint,
        OpenApiEndpointDocument document)
    {
        var prefix = request.DocumentIdPrefix ?? request.Metadata.DocumentId;
        var baseId = string.IsNullOrWhiteSpace(prefix)
            ? endpoint.Id
            : $"{prefix}_{endpoint.Id}";

        if (document.TotalSegments > 1)
        {
            baseId = $"{baseId}_part{document.SegmentIndex}";
        }

        return baseId
            .Replace(" ", "_", StringComparison.Ordinal)
            .Replace("__", "_", StringComparison.Ordinal);
    }

    private static string BuildDocumentTitle(Models.OpenApiEndpoint endpoint, OpenApiEndpointDocument document)
    {
        var title = $"{endpoint.Method} {endpoint.Path}";
        if (document.TotalSegments > 1)
        {
            title += $" (Part {document.SegmentIndex}/{document.TotalSegments})";
        }

        return title;
    }

    private OpenApiIngestionResult BuildResult(
        OpenApiSpecDocument specDocument,
        OpenApiSpecificationDocument specification,
        IngestionSummary summary,
        TimeSpan duration)
    {
        var message = summary.SuccessfulEndpoints > 0
            ? $"Successfully ingested {summary.SuccessfulEndpoints} of {summary.TotalEndpoints} endpoints with {summary.TotalChunksIndexed} total chunks in {duration.TotalSeconds:F2}s."
            : "Failed to ingest any endpoints from the specification.";

        if (summary.Errors.Count > 0)
        {
            message += $" Errors: {string.Join("; ", summary.Errors)}";
        }

        return new OpenApiIngestionResult
        {
            Success = summary.SuccessfulEndpoints > 0,
            SpecSource = specDocument.Source,
            EndpointsProcessed = summary.SuccessfulEndpoints,
            TotalEndpoints = summary.TotalEndpoints,
            TotalChunksIndexed = summary.TotalChunksIndexed,
            Message = message,
            Errors = summary.Errors.ToList()
        };
    }

    private static TagList CreateTelemetryTags(
        OpenApiSpecificationIngestion request,
        string status,
        Models.OpenApiEndpoint? endpoint = null)
    {
        var tags = new TagList
        {
            { "status", status },
            { "sourceType", "openapi" }
        };

        if (endpoint is not null)
        {
            tags.Add("method", endpoint.Method);
            tags.Add("path", endpoint.Path);
        }

        if (request.Resource.SourceUri is not null && !string.IsNullOrWhiteSpace(request.Resource.SourceUri.Host))
        {
            tags.Add("host", request.Resource.SourceUri.Host);
        }

        return tags;
    }

    private sealed record IngestionSummary(int TotalEndpoints)
    {
        private readonly List<string> _errors = new();

        public int SuccessfulEndpoints { get; private set; }

        public int TotalChunksIndexed { get; private set; }

        public IReadOnlyList<string> Errors => _errors;

        public void RegisterSuccess(int chunksIndexed)
        {
            SuccessfulEndpoints++;
            TotalChunksIndexed += chunksIndexed;
        }

        public void RegisterFailure(string? error)
        {
            if (!string.IsNullOrWhiteSpace(error))
            {
                _errors.Add(error);
            }
        }
    }

    private readonly record struct EndpointOutcome(bool Success, int ChunksIndexed, string ErrorMessage)
    {
        public static EndpointOutcome CreateSuccess(int chunks) => new(true, chunks, string.Empty);

        public static EndpointOutcome CreateFailure(string error, int chunks) => new(false, chunks, error);
    }
}
