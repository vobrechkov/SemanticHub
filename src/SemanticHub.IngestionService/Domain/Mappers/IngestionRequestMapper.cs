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

    public static OpenApiSpecificationIngestion ToDomain(this OpenApiIngestionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.SpecSource))
        {
            throw new ArgumentException("SpecSource must not be empty.", nameof(request));
        }

        var trimmedPrefix = string.IsNullOrWhiteSpace(request.DocumentIdPrefix)
            ? null
            : request.DocumentIdPrefix.Trim();

        var normalizedSpecSource = NormalizeSpecSource(request.SpecSource, out var specUri);

        var title = !string.IsNullOrWhiteSpace(trimmedPrefix)
            ? trimmedPrefix
            : ResolveTitleFromSpec(normalizedSpecSource, specUri);

        var metadata = IngestionMetadata.Create(
            trimmedPrefix,
            title,
            "openapi",
            specUri,
            request.Tags,
            request.Metadata);

        return new OpenApiSpecificationIngestion(metadata, normalizedSpecSource, trimmedPrefix);
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

    public static SitemapIngestion ToDomain(this SitemapIngestionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var sitemapUri = CreateUriOrThrow(request.SitemapUrl);

        var metadata = IngestionMetadata.Create(
            request.DocumentIdPrefix,
            request.DocumentIdPrefix ?? sitemapUri.Host,
            "sitemap",
            sitemapUri,
            request.Tags,
            request.Metadata);

        var allowedHosts = (request.AllowedHosts ?? [])
            .Where(host => !string.IsNullOrWhiteSpace(host))
            .Select(host => host.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var settings = new SitemapIngestionSettings
        {
            AllowedHosts = allowedHosts,
            MaxDepth = request.MaxDepth,
            MaxPages = request.MaxPages,
            RespectRobotsTxt = request.RespectRobotsTxt,
            ThrottleMilliseconds = request.ThrottleMilliseconds
        };

        return new SitemapIngestion(metadata, sitemapUri, settings);
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

    private static string NormalizeSpecSource(string specSource, out Uri? specUri)
    {
        var trimmed = specSource.Trim();
        specUri = null;

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri))
        {
            specUri = absoluteUri;
            return trimmed;
        }

        if (!Path.IsPathRooted(trimmed))
        {
            trimmed = Path.GetFullPath(trimmed);
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteFileUri))
        {
            specUri = absoluteFileUri;
        }

        return trimmed;
    }

    private static string ResolveTitleFromSpec(string normalizedSpecSource, Uri? specUri)
    {
        if (specUri is not null)
        {
            if (string.Equals(specUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(specUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return specUri.Host;
            }

            if (specUri.IsFile)
            {
                return Path.GetFileNameWithoutExtension(specUri.LocalPath) ?? "OpenAPI Specification";
            }

            return specUri.Host ?? specUri.ToString();
        }

        var fileName = Path.GetFileNameWithoutExtension(normalizedSpecSource);
        return string.IsNullOrWhiteSpace(fileName) ? "OpenAPI Specification" : fileName;
    }
}
