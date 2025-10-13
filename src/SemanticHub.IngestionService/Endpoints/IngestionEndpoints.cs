using Microsoft.AspNetCore.Routing;
using System.Linq;
using SemanticHub.IngestionService.Models;
using SemanticHub.IngestionService.Services;
using Microsoft.AspNetCore.Http;

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
    }

    private static async Task<IResult> HandleMarkdownIngestionAsync(
        MarkdownIngestionRequest request,
        DocumentIngestionService ingestionService,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Content))
        {
            return Results.BadRequest(new { error = "Request content must not be empty." });
        }

        var result = await ingestionService.IngestMarkdownAsync(request, cancellationToken);
        return Results.Ok(MapResult(result));
    }

    private static async Task<IResult> HandleWebPageIngestionAsync(
        WebPageIngestionRequest request,
        DocumentIngestionService ingestionService,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Url))
        {
            return Results.BadRequest(new { error = "Request URL must not be empty." });
        }

        var result = await ingestionService.IngestWebPageAsync(request, cancellationToken);
        return Results.Ok(MapResult(result));
    }

    private static async Task<IResult> HandleOpenApiIngestionAsync(
        OpenApiIngestionRequest request,
        DocumentIngestionService ingestionService,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.SpecSource))
        {
            return Results.BadRequest(new { error = "Request SpecSource must not be empty." });
        }

        var result = await ingestionService.IngestOpenApiAsync(request, cancellationToken);

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
        DocumentIngestionService ingestionService,
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
                await ingestionService.IngestFromBlobAsync(request, cancellationToken);
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
        DocumentIngestionService ingestionService,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Content))
        {
            return Results.BadRequest(new { error = "Request content must not be empty." });
        }

        var result = await ingestionService.IngestHtmlAsync(request, cancellationToken);
        return Results.Ok(MapResult(result));
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
