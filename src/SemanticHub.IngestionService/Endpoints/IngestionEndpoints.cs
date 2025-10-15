using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Linq;
using SemanticHub.IngestionService.Domain.Aggregates;
using SemanticHub.IngestionService.Domain.Mappers;
using SemanticHub.IngestionService.Domain.Results;
using SemanticHub.IngestionService.Domain.Workflows;
using SemanticHub.IngestionService.Models;

namespace SemanticHub.IngestionService.Endpoints;

/// <summary>
/// Minimal API endpoints for ingestion workflows.
/// </summary>
public class IngestionEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/ingestion")
            .WithTags("Ingestion")
            .WithOpenApi();

        group.MapPost("/markdown", HandleMarkdownIngestionAsync)
            .WithName("IngestMarkdown")
            .WithSummary("Ingest Markdown content into Azure AI Search")
            .WithDescription("Chunks, embeds, and indexes Markdown content so it can be retrieved by MAF agents.");

        group.MapPost("/webpage", HandleWebPageIngestionAsync)
            .WithName("IngestWebPage")
            .WithSummary("Scrape a web page and ingest its content into Azure AI Search")
            .WithDescription("Fetches a web page, converts it to Markdown, then chunks, embeds, and indexes it for retrieval.");

        group.MapPost("/openapi", HandleOpenApiIngestionAsync)
            .WithName("IngestOpenApi")
            .WithSummary("Parse an OpenAPI specification and ingest its endpoints into Azure AI Search")
            .WithDescription("Parses an OpenAPI YAML or JSON specification, converts each endpoint to Markdown, then chunks, embeds, and indexes them for retrieval.");

        group.MapPost("/blob", HandleBlobIngestionAsync)
            .WithName("IngestFromBlob")
            .WithSummary("Ingest documents from Azure Blob Storage into Azure AI Search")
            .WithDescription("Reads files from Azure Blob Storage (.md, .yaml, .json, .html) and processes them using the appropriate ingestion mechanism. Returns immediately with Accepted status.");

        group.MapPost("/html", HandleHtmlIngestionAsync)
            .WithName("IngestHtml")
            .WithSummary("Ingest HTML content into Azure AI Search")
            .WithDescription("Converts HTML to Markdown, then chunks, embeds, and indexes it for retrieval.");

        group.MapPost("/sitemap", HandleSitemapIngestionAsync)
            .WithName("IngestFromSitemap")
            .WithSummary("Traverse a sitemap and ingest discovered pages")
            .WithDescription("Fetches sitemap documents, applies filtering and throttling policies, scrapes each page, and indexes the resulting content.");
    }

    private static async Task<IResult> HandleMarkdownIngestionAsync(
        MarkdownIngestionRequest request,
        IIngestionWorkflow<MarkdownDocumentIngestion> workflow,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Content))
        {
            return Results.BadRequest(new { error = "Request content must not be empty." });
        }

        var outcome = await workflow.ExecuteAsync(request.ToDomain(), cancellationToken);
        return Results.Ok(MapOutcome(outcome));
    }

    private static async Task<IResult> HandleWebPageIngestionAsync(
        WebPageIngestionRequest request,
        IIngestionWorkflow<WebPageIngestion> workflow,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Url))
        {
            return Results.BadRequest(new { error = "Request URL must not be empty." });
        }

        var outcome = await workflow.ExecuteAsync(request.ToDomain(), cancellationToken);
        return Results.Ok(MapOutcome(outcome));
    }

    private static async Task<IResult> HandleOpenApiIngestionAsync(
        OpenApiIngestionRequest request,
        IIngestionWorkflow<OpenApiSpecificationIngestion, OpenApiIngestionResult> workflow,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.SpecSource))
        {
            return Results.BadRequest(new { error = "Request SpecSource must not be empty." });
        }

        var domainRequest = request.ToDomain();
        var result = await workflow.ExecuteAsync(domainRequest, cancellationToken);

        var response = new
        {
            Success = result.Success,
            SpecSource = result.SpecSource,
            EndpointsProcessed = result.EndpointsProcessed,
            TotalEndpoints = result.TotalEndpoints,
            TotalChunksIndexed = result.TotalChunksIndexed,
            Message = result.Message,
            Errors = result.Errors.Any() ? result.Errors : null,
            ErrorMessage = result.Success ? null : result.Message
        };

        return Results.Ok(response);
    }

    private static Task<IResult> HandleBlobIngestionAsync(
        BlobIngestionRequest request,
        IIngestionWorkflow<BulkMarkdownIngestion, BlobIngestionResult> workflow,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.BlobPath))
        {
            return Task.FromResult(Results.BadRequest(new { error = "Request BlobPath must not be empty." }) as IResult);
        }

        // Start ingestion asynchronously and return immediately
        _ = Task.Run(async () =>
        {
            try
            {
                await workflow.ExecuteAsync(request.ToDomain(), cancellationToken);
            }
            catch (Exception ex)
            {
                // Log error but don't throw since this is fire-and-forget
                Console.Error.WriteLine($"Background blob ingestion failed: {ex.Message}");
            }
        }, cancellationToken);

        var response = new
        {
            Status = "Accepted",
            Message = $"Blob ingestion started for path: {request.BlobPath}. Processing will continue in the background.",
            BlobPath = request.BlobPath
        };

        return Task.FromResult(Results.Accepted("/ingestion/blob", response) as IResult);
    }

    private static async Task<IResult> HandleHtmlIngestionAsync(
        HtmlIngestionRequest request,
        IIngestionWorkflow<HtmlDocumentIngestion> workflow,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Content))
        {
            return Results.BadRequest(new { error = "Request content must not be empty." });
        }

        var outcome = await workflow.ExecuteAsync(request.ToDomain(), cancellationToken);
        return Results.Ok(MapOutcome(outcome));
    }

    private static async Task<IResult> HandleSitemapIngestionAsync(
        SitemapIngestionRequest request,
        IIngestionWorkflow<SitemapIngestion, SitemapIngestionResult> workflow,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.SitemapUrl))
        {
            return Results.BadRequest(new { error = "Request SitemapUrl must not be empty." });
        }

        var result = await workflow.ExecuteAsync(request.ToDomain(), cancellationToken);

        var response = new
        {
            Success = result.TotalFailed == 0,
            SitemapUrl = result.SitemapUrl,
            TotalDiscovered = result.TotalDiscovered,
            TotalFiltered = result.TotalFiltered,
            TotalIngested = result.TotalIngested,
            TotalFailed = result.TotalFailed,
            DurationMs = result.Duration.TotalMilliseconds,
            Message = result.Message,
            Errors = result.Errors.Count > 0 ? result.Errors : null
        };

        return Results.Ok(response);
    }

    private static IngestionResponse MapOutcome(IngestionOutcome outcome)
    {
        if (outcome.LegacyResult is not null)
        {
            return MapResult(outcome.LegacyResult);
        }

        return new IngestionResponse
        {
            Success = outcome.Success,
            DocumentId = outcome.Document?.DocumentId ?? string.Empty,
            IndexName = outcome.Document?.IndexName,
            ChunksIndexed = outcome.Document?.Metrics.ChunkCount ?? 0,
            Message = outcome.Error?.Message,
            ErrorMessage = outcome.Success ? null : outcome.Error?.Message
        };
    }

    private static IngestionResponse MapResult(DocumentIngestionResult result) => new()
    {
        Success = result.Success,
        DocumentId = result.DocumentId,
        IndexName = result.IndexName,
        ChunksIndexed = result.ChunksIndexed,
        Message = result.Message,
        ErrorMessage = result.Success ? null : result.Message
    };
}
