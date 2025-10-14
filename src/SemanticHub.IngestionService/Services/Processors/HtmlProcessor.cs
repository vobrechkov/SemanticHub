using System.Diagnostics;
using System.Linq;
using HtmlAgilityPack;
using SemanticHub.IngestionService.Domain.Ports;
using SemanticHub.IngestionService.Domain.Results;
using SemanticHub.IngestionService.Diagnostics;
using SemanticHub.IngestionService.Models;

namespace SemanticHub.IngestionService.Services.Processors;

/// <summary>
/// Handles ingestion of HTML-originating content by sanitising markup, converting to Markdown, and delegating to the markdown processor.
/// </summary>
public class HtmlProcessor(
    ILogger<HtmlProcessor> logger,
    IMarkdownConverter markdownConverter,
    IMarkdownProcessor markdownProcessor) : IHtmlProcessor
{
    private static readonly string[] StructuralNodesToStrip = ["script", "style", "nav", "header", "footer", "aside"];
    private static readonly string[] ClassMarkersToStrip = ["navigation", "sidebar", "footer", "header", "breadcrumb"];

    public Task<DocumentIngestionResult> IngestHtmlAsync(
        HtmlIngestionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateHtmlRequest(request);

        using var activity = IngestionTelemetry.ActivitySource.StartActivity("IngestHtml");
        activity?.SetTag("ingestion.sourceType", "html");

        logger.LogInformation("Ingesting HTML content");

        try
        {
            var scrapedPage = CreateScrapedPageFromHtml(request);
            activity?.AddEvent(new ActivityEvent("HtmlSanitized", tags: new ActivityTagsCollection
            {
                { "length", scrapedPage.HtmlContent.Length }
            }));

            var markdownRequest = BuildMarkdownRequestFromHtml(request, scrapedPage);
            activity?.SetTag("ingestion.html.contentLength", markdownRequest.Content.Length);

            return markdownProcessor.IngestAsync(markdownRequest, cancellationToken);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogError(ex, "Error ingesting HTML content");
            throw;
        }
    }

    public async Task<DocumentIngestionResult> IngestWebPageAsync(
        WebPageIngestionRequest request,
        ScrapedPage scrapedPage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(scrapedPage);

        if (!string.IsNullOrWhiteSpace(request.Title))
        {
            scrapedPage.Title = request.Title!;
        }

        scrapedPage.HtmlContent = NormalizeHtml(scrapedPage.HtmlContent, scrapedPage.Metadata);

        var markdownContent = markdownConverter.ConvertToMarkdown(scrapedPage);
        logger.LogInformation(
            "Converted scraped page {Url} to markdown ({Length} chars)",
            scrapedPage.Url ?? request.Url,
            markdownContent.Length);

        var markdownRequest = BuildMarkdownRequestFromWebPage(request, scrapedPage, markdownContent);
        return await markdownProcessor.IngestAsync(markdownRequest, cancellationToken);
    }

    private static void ValidateHtmlRequest(HtmlIngestionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
        {
            throw new ArgumentException("Request content must not be empty.", nameof(request));
        }
    }

    private ScrapedPage CreateScrapedPageFromHtml(HtmlIngestionRequest request)
    {
        var metadata = new Dictionary<string, string>();

        var sanitized = NormalizeHtml(request.Content, metadata);
        var page = new ScrapedPage
        {
            Url = request.SourceUrl ?? "manual",
            Title = request.Title ?? ResolveTitleFromMetadata(metadata) ?? "Untitled HTML Document",
            HtmlContent = sanitized,
            StatusCode = 200,
            ScrapedAt = DateTime.UtcNow,
            Metadata = metadata
        };

        return page;
    }

    private MarkdownIngestionRequest BuildMarkdownRequestFromHtml(
        HtmlIngestionRequest request,
        ScrapedPage scrapedPage)
    {
        var markdownContent = markdownConverter.ConvertToMarkdown(scrapedPage);
        return new MarkdownIngestionRequest
        {
            DocumentId = request.DocumentId,
            Title = request.Title ?? scrapedPage.Title,
            SourceUrl = request.SourceUrl,
            SourceType = "html",
            Tags = request.Tags,
            Metadata = request.Metadata,
            Content = markdownContent
        };
    }

    private static MarkdownIngestionRequest BuildMarkdownRequestFromWebPage(
        WebPageIngestionRequest request,
        ScrapedPage scrapedPage,
        string markdownContent)
    {
        return new MarkdownIngestionRequest
        {
            DocumentId = request.DocumentId,
            Title = request.Title ?? scrapedPage.Title,
            SourceUrl = scrapedPage.Url ?? request.Url,
            SourceType = "webpage",
            Tags = request.Tags,
            Metadata = request.Metadata,
            Content = markdownContent
        };
    }

    private string NormalizeHtml(string html, IDictionary<string, string> metadata)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);

        RemoveStructuralNodes(document);
        RemoveNodesByClass(document);
        RemoveComments(document);
        ExtractMetadata(document, metadata);

        return document.DocumentNode.OuterHtml;
    }

    private static void RemoveStructuralNodes(HtmlDocument document)
    {
        foreach (var node in StructuralNodesToStrip)
        {
            var matches = document.DocumentNode.SelectNodes($"//{node}");
            if (matches == null)
            {
                continue;
            }

            foreach (var match in matches)
            {
                match.Remove();
            }
        }
    }

    private static void RemoveNodesByClass(HtmlDocument document)
    {
        foreach (var marker in ClassMarkersToStrip)
        {
            var nodes = document.DocumentNode.SelectNodes($"//*[contains(@class, '{marker}')]");
            if (nodes == null)
            {
                continue;
            }

            foreach (var node in nodes)
            {
                node.Remove();
            }
        }
    }

    private static void RemoveComments(HtmlDocument document)
    {
        var comments = document.DocumentNode
            .Descendants()
            .Where(n => n.NodeType == HtmlNodeType.Comment)
            .ToList();

        foreach (var comment in comments)
        {
            comment.Remove();
        }
    }

    private static void ExtractMetadata(HtmlDocument document, IDictionary<string, string> metadata)
    {
        var metaNodes = document.DocumentNode.SelectNodes("//meta[@name]");
        if (metaNodes != null)
        {
            foreach (var meta in metaNodes)
            {
                var name = meta.GetAttributeValue("name", string.Empty);
                var content = meta.GetAttributeValue("content", string.Empty);
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(content))
                {
                    continue;
                }

                metadata[name] = content;
            }
        }

        var ogTitle = document.DocumentNode.SelectSingleNode("//meta[@property='og:title']");
        if (ogTitle != null)
        {
            var content = ogTitle.GetAttributeValue("content", string.Empty);
            if (!string.IsNullOrWhiteSpace(content))
            {
                metadata["og:title"] = content;
            }
        }
    }

    private static string? ResolveTitleFromMetadata(IDictionary<string, string> metadata)
    {
        if (metadata.TryGetValue("og:title", out var ogTitle) && !string.IsNullOrWhiteSpace(ogTitle))
        {
            return ogTitle;
        }

        if (metadata.TryGetValue("title", out var title) && !string.IsNullOrWhiteSpace(title))
        {
            return title;
        }

        return null;
    }
}
