using SemanticHub.IngestionService.Domain.Resources;

namespace SemanticHub.IngestionService.Domain.Aggregates;

/// <summary>
/// Root aggregate for ingestion workflows.
/// </summary>
public abstract record IngestionRequest(IngestionMetadata Metadata, IngestionResource Resource)
{
    public Guid CorrelationId { get; init; } = Guid.NewGuid();

    public DateTime RequestedAtUtc { get; init; } = DateTime.UtcNow;
}

public sealed record WebPageIngestion(IngestionMetadata Metadata, Uri Url)
    : IngestionRequest(Metadata, IngestionResource.FromWebPage(Url))
{
    public string? TitleOverride { get; init; }
}

public sealed record HtmlDocumentIngestion(IngestionMetadata Metadata, string HtmlContent, Uri? SourceUri = null)
    : IngestionRequest(Metadata, IngestionResource.FromHtml(HtmlContent, SourceUri));

public sealed record MarkdownDocumentIngestion(IngestionMetadata Metadata, string MarkdownContent, Uri? SourceUri = null)
    : IngestionRequest(Metadata, IngestionResource.FromMarkdown(MarkdownContent, SourceUri));

public sealed record BulkMarkdownIngestion(
    IngestionMetadata Metadata,
    string BlobPath,
    string? ContainerName)
    : IngestionRequest(Metadata, IngestionResource.FromBlobMarkdown(BlobPath))
{
    public string? ContainerName { get; init; } = ContainerName;
}

public sealed record SitemapIngestion(
    IngestionMetadata Metadata,
    Uri SitemapUri,
    SitemapIngestionSettings Settings)
    : IngestionRequest(Metadata, IngestionResource.FromSitemap(SitemapUri));

public sealed record OpenApiSpecificationIngestion : IngestionRequest
{
    public OpenApiSpecificationIngestion(
        IngestionMetadata metadata,
        string specSource,
        string? documentIdPrefix)
        : base(metadata, IngestionResource.FromOpenApi(specSource))
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (string.IsNullOrWhiteSpace(specSource))
        {
            throw new ArgumentException("Specification source must not be empty.", nameof(specSource));
        }

        SpecSource = specSource.Trim();
        DocumentIdPrefix = string.IsNullOrWhiteSpace(documentIdPrefix)
            ? null
            : documentIdPrefix.Trim();
    }

    public string SpecSource { get; }

    public string? DocumentIdPrefix { get; }
}
