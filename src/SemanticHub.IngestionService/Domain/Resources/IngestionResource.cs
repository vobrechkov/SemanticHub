using System.Diagnostics.CodeAnalysis;

namespace SemanticHub.IngestionService.Domain.Resources;

/// <summary>
/// Captures the resource that will be ingested including source hints and inline payloads.
/// </summary>
public sealed record IngestionResource
{
    private IngestionResource(
        IngestionResourceType type,
        Uri? sourceUri,
        string? blobPath,
        string? content)
    {
        Type = type;
        SourceUri = sourceUri;
        BlobPath = blobPath;
        Content = content;
    }

    public IngestionResourceType Type { get; }

    public Uri? SourceUri { get; }

    public string? BlobPath { get; }

    public string? Content { get; }

    public static IngestionResource FromWebPage(Uri url) =>
        new(IngestionResourceType.WebPage, url, null, null);

    public static IngestionResource FromHtml(string html, Uri? source = null) =>
        new(IngestionResourceType.Html, source, null, html);

    public static IngestionResource FromMarkdown(string markdown, Uri? source = null) =>
        new(IngestionResourceType.Markdown, source, null, markdown);

    public static IngestionResource FromBlobMarkdown(string blobPath, [StringSyntax("Uri")] string? sourceUrl = null) =>
        new(IngestionResourceType.BlobMarkdown, sourceUrl is null ? null : new Uri(sourceUrl), blobPath, null);

    public static IngestionResource FromBlobHtml(string blobPath, [StringSyntax("Uri")] string? sourceUrl = null) =>
        new(IngestionResourceType.BlobHtml, sourceUrl is null ? null : new Uri(sourceUrl), blobPath, null);

    public static IngestionResource FromOpenApi(string specPath) =>
        new(IngestionResourceType.OpenApi, null, specPath, null);

    public static IngestionResource Unknown() =>
        new(IngestionResourceType.Unknown, null, null, null);
}
