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
}
