using SemanticHub.IngestionService.Domain.Aggregates;
using SemanticHub.IngestionService.Models;

namespace SemanticHub.IngestionService.Domain.Mappers;

/// <summary>
/// Converts API transport models into domain aggregates.
/// </summary>
public static class IngestionRequestMapper
{
    public static WebPageIngestion ToDomain(this WebPageIngestionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var url = CreateUriOrThrow(request.Url);

        var metadata = IngestionMetadata.Create(
            request.DocumentId,
            request.Title,
            "webpage",
            url,
            request.Tags,
            request.Metadata);

        return new WebPageIngestion(metadata, url)
        {
            TitleOverride = request.Title
        };
    }

    public static HtmlDocumentIngestion ToDomain(this HtmlIngestionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var sourceUri = CreateUri(request.SourceUrl);

        var metadata = IngestionMetadata.Create(
            request.DocumentId,
            request.Title,
            "html",
            sourceUri,
            request.Tags,
            request.Metadata);

        return new HtmlDocumentIngestion(metadata, request.Content, sourceUri);
    }

    public static MarkdownDocumentIngestion ToDomain(this MarkdownIngestionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var sourceUri = CreateUri(request.SourceUrl);

        var metadata = IngestionMetadata.Create(
            request.DocumentId,
            request.Title,
            string.IsNullOrWhiteSpace(request.SourceType) ? "markdown" : request.SourceType,
            sourceUri,
            request.Tags,
            request.Metadata);

        return new MarkdownDocumentIngestion(metadata, request.Content, sourceUri);
    }

    public static BulkMarkdownIngestion ToDomain(this BlobIngestionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var metadata = IngestionMetadata.Create(
            documentId: null,
            title: request.BlobPath,
            sourceType: "blob",
            sourceUri: null,
            tags: request.Tags,
            metadata: request.Metadata);

        return new BulkMarkdownIngestion(metadata, request.BlobPath, request.ContainerName);
    }

    private static Uri CreateUriOrThrow(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return uri;
        }

        throw new ArgumentException($"Invalid URL '{url}'", nameof(url));
    }

    private static Uri? CreateUri(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            ? uri
            : null;
    }
}
