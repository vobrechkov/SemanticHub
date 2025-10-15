using SemanticHub.IngestionService.Models;

namespace SemanticHub.IngestionService.Domain.OpenApi;

/// <summary>
/// Represents the raw specification content resolved from a caller provided source.
/// </summary>
public sealed record OpenApiSpecDocument
{
    public OpenApiSpecDocument(string source, string content, Uri? sourceUri)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException("Specification source must not be empty.", nameof(source));
        }

        Source = source.Trim();
        Content = content ?? throw new ArgumentNullException(nameof(content));
        SourceUri = sourceUri;
    }

    public string Source { get; }

    public string Content { get; }

    public Uri? SourceUri { get; }
}

/// <summary>
/// Parsed representation of an OpenAPI specification with the extracted endpoints.
/// </summary>
public sealed record OpenApiSpecificationDocument
{
    public OpenApiSpecificationDocument(
        string title,
        string version,
        string source,
        IReadOnlyList<OpenApiEndpoint> endpoints)
    {
        Title = string.IsNullOrWhiteSpace(title) ? "OpenAPI Specification" : title.Trim();
        Version = string.IsNullOrWhiteSpace(version) ? "1.0" : version.Trim();
        Source = string.IsNullOrWhiteSpace(source) ? "unknown" : source.Trim();
        Endpoints = endpoints ?? throw new ArgumentNullException(nameof(endpoints));
    }

    public string Title { get; }

    public string Version { get; }

    public string Source { get; }

    public IReadOnlyList<OpenApiEndpoint> Endpoints { get; }
}

/// <summary>
/// Represents a Markdown document generated for a specific OpenAPI endpoint.
/// </summary>
public sealed record OpenApiEndpointDocument
{
    public OpenApiEndpointDocument(
        OpenApiEndpoint endpoint,
        string markdown,
        int segmentIndex,
        int totalSegments)
    {
        Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        Markdown = markdown ?? throw new ArgumentNullException(nameof(markdown));
        SegmentIndex = segmentIndex;
        TotalSegments = totalSegments;
    }

    public OpenApiEndpoint Endpoint { get; }

    public string Markdown { get; }

    public int SegmentIndex { get; }

    public int TotalSegments { get; }
}
