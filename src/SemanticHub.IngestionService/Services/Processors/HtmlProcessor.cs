using System.Diagnostics;
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
    // Only strip elements that are truly non-content (scripts, styles, navigation menus)
    private static readonly string[] StructuralNodesToStrip = ["script", "style", "nav"];
    
    // Match exact class names or class names as complete words (not substrings)
    // These should be highly specific to avoid removing content
    private static readonly string[] ExactClassNamesToStrip = 
    [
        "navigation",
        "navbar", 
        "nav-bar",
        "sidebar",
        "side-bar",
        "breadcrumb",
        "breadcrumbs",
        "menu",
        "nav-menu"
    ];

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

        var originalLength = document.DocumentNode.OuterHtml.Length;
        
        RemoveStructuralNodes(document);
        RemoveNodesByClass(document);
        RemoveComments(document);
        ExtractMetadata(document, metadata);

        var normalizedLength = document.DocumentNode.OuterHtml.Length;
        var reductionPercent = originalLength > 0 
            ? ((originalLength - normalizedLength) / (double)originalLength * 100) 
            : 0;

        logger.LogDebug(
            "HTML normalization: {OriginalLength} -> {NormalizedLength} chars ({ReductionPercent:F1}% reduction)",
            originalLength,
            normalizedLength,
            reductionPercent);

        if (normalizedLength < 100)
        {
            logger.LogWarning(
                "HTML normalization resulted in very small content ({Length} chars). Original was {OriginalLength} chars. Content may have been stripped too aggressively.",
                normalizedLength,
                originalLength);
        }

        return document.DocumentNode.OuterHtml;
    }

    private static void RemoveStructuralNodes(HtmlDocument document)
    {
        foreach (var nodeType in StructuralNodesToStrip)
        {
            var matches = document.DocumentNode.SelectNodes($"//{nodeType}");
            if (matches == null)
            {
                continue;
            }

            foreach (var match in matches)
            {
                match.Remove();
            }
        }
        
        // Remove header/footer elements that are NOT within article or main content
        // This preserves article headers (titles) while removing page headers/footers
        RemoveNonContentHeadersFooters(document);
    }

    private static void RemoveNonContentHeadersFooters(HtmlDocument document)
    {
        // Remove top-level headers and footers (page chrome)
        // Keep headers/footers within article or main tags (likely content)
        var topLevelHeaders = document.DocumentNode.SelectNodes(
            "//header[not(ancestor::article) and not(ancestor::main) and not(ancestor::*[@role='main'])]");
        if (topLevelHeaders != null)
        {
            foreach (var header in topLevelHeaders)
            {
                // Check if it contains actual content (article title, etc.) by looking for h1-h6
                var hasHeadings = header.SelectNodes(".//h1 | .//h2 | .//h3 | .//h4 | .//h5 | .//h6");
                if (hasHeadings == null || hasHeadings.Count == 0)
                {
                    // No headings, likely page chrome, safe to remove
                    header.Remove();
                }
                // Otherwise keep it - might be article title
            }
        }

        var topLevelFooters = document.DocumentNode.SelectNodes(
            "//footer[not(ancestor::article) and not(ancestor::main) and not(ancestor::*[@role='main'])]");
        if (topLevelFooters != null)
        {
            foreach (var footer in topLevelFooters)
            {
                footer.Remove();
            }
        }
        
        // Remove aside elements that don't appear to be related content
        var asides = document.DocumentNode.SelectNodes("//aside");
        if (asides != null)
        {
            foreach (var aside in asides)
            {
                // Keep asides that have significant text content (might be related info)
                var textLength = aside.InnerText?.Trim().Length ?? 0;
                if (textLength < 100)
                {
                    aside.Remove();
                }
            }
        }
    }

    private static void RemoveNodesByClass(HtmlDocument document)
    {
        foreach (var className in ExactClassNamesToStrip)
        {
            // Match complete class names using word boundaries
            // This XPath looks for class attributes where the className appears as a complete word
            // Matches: class="navigation", class="foo navigation bar", etc.
            // Does NOT match: class="main-navigation", class="page-navigation-menu"
            var xpath = $"//*[contains(concat(' ', normalize-space(@class), ' '), ' {className} ')]";
            var nodes = document.DocumentNode.SelectNodes(xpath);
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
