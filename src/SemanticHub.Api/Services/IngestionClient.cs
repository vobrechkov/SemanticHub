using SemanticHub.Api.Models;

namespace SemanticHub.Api.Services;

/// <summary>
/// Typed client for communicating with the ingestion service.
/// </summary>
public class IngestionClient(HttpClient httpClient, ILogger<IngestionClient> logger)
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ILogger<IngestionClient> _logger = logger;

    public async Task<IngestionResponse> IngestMarkdownAsync(
        MarkdownIngestionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var response = await _httpClient.PostAsJsonAsync(
            "/ingestion/markdown",
            request,
            cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            var payload = await response.Content.ReadFromJsonAsync<IngestionResponse>(
                cancellationToken: cancellationToken);

            return payload ?? new IngestionResponse
            {
                Success = true,
                DocumentId = request.DocumentId ?? string.Empty,
                IndexName = string.Empty,
                ChunksIndexed = 0
            };
        }

        var errorText = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning("Ingestion API returned {Status}: {Error}", response.StatusCode, errorText);

        return new IngestionResponse
        {
            Success = false,
            DocumentId = request.DocumentId ?? string.Empty,
            ErrorMessage = string.IsNullOrWhiteSpace(errorText)
                ? $"Ingestion service returned status {(int)response.StatusCode}"
                : errorText
        };
    }

    public async Task<IngestionResponse> IngestWebPageAsync(
        WebPageIngestionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var response = await _httpClient.PostAsJsonAsync(
            "/ingestion/webpage",
            request,
            cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            var payload = await response.Content.ReadFromJsonAsync<IngestionResponse>(
                cancellationToken: cancellationToken);

            return payload ?? new IngestionResponse
            {
                Success = true,
                DocumentId = request.DocumentId ?? request.Url,
                IndexName = string.Empty,
                ChunksIndexed = 0
            };
        }

        var errorText = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning("Web ingestion API returned {Status}: {Error}", response.StatusCode, errorText);

        return new IngestionResponse
        {
            Success = false,
            DocumentId = request.DocumentId ?? request.Url,
            ErrorMessage = string.IsNullOrWhiteSpace(errorText)
                ? $"Ingestion service returned status {(int)response.StatusCode}"
                : errorText
        };
    }

    public async Task<OpenApiIngestionResponse> IngestOpenApiAsync(
        OpenApiIngestionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var response = await _httpClient.PostAsJsonAsync(
            "/ingestion/openapi",
            request,
            cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            var payload = await response.Content.ReadFromJsonAsync<OpenApiIngestionResponse>(
                cancellationToken: cancellationToken);

            return payload ?? new OpenApiIngestionResponse
            {
                Success = true,
                SpecSource = request.SpecSource,
                EndpointsProcessed = 0,
                TotalEndpoints = 0,
                TotalChunksIndexed = 0
            };
        }

        var errorText = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning("OpenAPI ingestion API returned {Status}: {Error}", response.StatusCode, errorText);

        return new OpenApiIngestionResponse
        {
            Success = false,
            SpecSource = request.SpecSource,
            ErrorMessage = string.IsNullOrWhiteSpace(errorText)
                ? $"Ingestion service returned status {(int)response.StatusCode}"
                : errorText
        };
    }
}
