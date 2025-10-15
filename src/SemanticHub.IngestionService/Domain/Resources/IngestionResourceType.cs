namespace SemanticHub.IngestionService.Domain.Resources;

/// <summary>
/// Identifies the source format or origin for an ingestion resource.
/// </summary>
public enum IngestionResourceType
{
    Unknown = 0,
    WebPage = 1,
    Html = 2,
    Markdown = 3,
    MarkdownArchive = 4,
    BlobMarkdown = 5,
    BlobHtml = 6,
    OpenApi = 7,
    Sitemap = 8
}
