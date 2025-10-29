using System.Diagnostics;
using HtmlAgilityPack;
using Microsoft.Extensions.Options;
using SemanticHub.IngestionService.Configuration;
using SemanticHub.IngestionService.Domain.Ports;
using SemanticHub.IngestionService.Domain.Results;
using SemanticHub.IngestionService.Diagnostics;
using SemanticHub.IngestionService.Models;

namespace SemanticHub.IngestionService.Services.Processors;

/// <summary>
/// Handles ingestion of HTML-originating content by extracting main content, sanitising markup, converting to Markdown, and delegating to the markdown processor.
/// Uses Mozilla Readability-style scoring algorithm to identify and extract the primary content area.
/// </summary>
public class HtmlProcessor(
    ILogger<HtmlProcessor> logger,
    IMarkdownConverter markdownConverter,
    IMarkdownProcessor markdownProcessor,
    IOptions<IngestionOptions> options,
    ContentScorer contentScorer) : IHtmlProcessor
{
    private readonly HtmlExtractionOptions _extractionOptions = options.Value.HtmlExtraction;

    // Always remove these structural elements (absolutely non-content)
    private static readonly string[] StructuralNodesToStrip = ["script", "style", "noscript"];

    // Comprehensive list of class names indicating non-content elements
    // Based on patterns from Mozilla Readability, Trafilatura, and other leading extractors
    private static readonly string[] ExactClassNamesToStrip =
    [
        // Navigation
        "navigation",
        "navbar",
        "nav-bar",
        "nav-menu",
        "menu",
        "breadcrumb",
        "breadcrumbs",

        // Layout
        "sidebar",
        "side-bar",
        "widget",

        // Advertising & Promotions
        "advertisement",
        "ad",
        "ads",
        "ad-block",
        "ad-wrapper",
        "ad-container",
        "banner",
        "promo",
        "promotion",
        "sponsor",
        "sponsored",

        // Social & Sharing
        "social",
        "social-share",
        "share",
        "share-buttons",
        "sharing",

        // Comments & Discussion
        "comments",
        "comment",
        "comment-section",
        "comment-form",
        "reply",
        "reply-form",
        "disqus",

        // Related Content
        "related",
        "related-posts",
        "related-articles",
        "recommended",

        // Utilities
        "cookie-notice",
        "cookie-banner",
        "gdpr",
        "newsletter",
        "subscribe",
        "subscription",
        "popup",
        "modal",
        "overlay",

        // Pagination
        "pagination",
        "pager",
        "page-numbers"
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

        // Add base URL to metadata for URL resolution
        if (!string.IsNullOrWhiteSpace(scrapedPage.Url))
        {
            scrapedPage.Metadata["base-url"] = scrapedPage.Url;
        }
        else if (!string.IsNullOrWhiteSpace(request.Url))
        {
            scrapedPage.Metadata["base-url"] = request.Url;
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

        // Step 1: Extract metadata before any modifications
        ExtractMetadata(document, metadata);

        // Step 2: Remove structural noise (scripts, styles, etc.)
        RemoveStructuralNodes(document);
        RemoveNodesByClass(document);
        RemoveNodesById(document);
        RemoveComments(document);

        // Step 3: Extract main content area using scoring algorithm
        var mainContent = ExtractMainContent(document);

        // Step 4: Resolve relative URLs to absolute (if base URL is available)
        if (_extractionOptions.ResolveRelativeUrls && metadata.TryGetValue("base-url", out var baseUrl))
        {
            ResolveRelativeUrls(mainContent, baseUrl);
        }

        // Step 5: Clean conditionally (remove low-value elements)
        if (_extractionOptions.AggressiveCleaning)
        {
            CleanConditionally(mainContent);
        }

        var normalizedLength = mainContent.OuterHtml.Length;
        var reductionPercent = originalLength > 0
            ? ((originalLength - normalizedLength) / (double)originalLength * 100)
            : 0;

        logger.LogDebug(
            "HTML normalization: {OriginalLength} -> {NormalizedLength} chars ({ReductionPercent:F1}% reduction)",
            originalLength,
            normalizedLength,
            reductionPercent);

        if (normalizedLength < _extractionOptions.MinTextLength)
        {
            logger.LogWarning(
                "HTML normalization resulted in very small content ({Length} chars). Original was {OriginalLength} chars. Content may have been stripped too aggressively.",
                normalizedLength,
                originalLength);
        }

        return mainContent.OuterHtml;
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

    private void RemoveNodesByClass(HtmlDocument document)
    {
        // First remove hardcoded class names
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

        // Then remove configurable class names
        foreach (var className in _extractionOptions.RemoveClassNames)
        {
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

    private void RemoveNodesById(HtmlDocument document)
    {
        // Remove elements by ID patterns (common non-content IDs)
        var nonContentIds = new[]
        {
            "header", "footer", "nav", "navigation", "sidebar", "menu",
            "comments", "disqus_thread", "ad", "advertisement", "banner",
            "social", "share", "cookie-notice", "newsletter"
        };

        foreach (var id in nonContentIds)
        {
            var node = document.DocumentNode.SelectSingleNode($"//*[@id='{id}']");
            if (node != null)
            {
                logger.LogTrace("Removing element by ID: {Id}", id);
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

    /// <summary>
    /// Extract main content area using Mozilla Readability-style scoring algorithm.
    /// Identifies the most likely content container and returns it.
    /// </summary>
    private HtmlNode ExtractMainContent(HtmlDocument document)
    {
        // First try configured content selectors (explicit hints)
        foreach (var selector in _extractionOptions.ContentSelectors)
        {
            var node = TrySelectByCssSelector(document, selector);
            if (node != null && contentScorer.GetConfidenceScore(node) >= _extractionOptions.MinConfidenceThreshold)
            {
                logger.LogDebug("Found main content using selector: {Selector}", selector);
                return node;
            }
        }

        // Try semantic HTML5 elements first (most reliable)
        var semanticSelectors = new[] { "article", "main", "[role='main']" };
        foreach (var selector in semanticSelectors)
        {
            var candidates = document.DocumentNode.SelectNodes($"//{selector}");
            if (candidates == null || candidates.Count == 0)
            {
                continue;
            }

            // Score each candidate and pick the best
            var scoredCandidates = candidates
                .Select(node => new { Node = node, Score = contentScorer.CalculateScore(node) })
                .OrderByDescending(x => x.Score)
                .ToList();

            if (scoredCandidates.Count > 0 && scoredCandidates[0].Score > 0)
            {
                logger.LogDebug(
                    "Found main content in <{Element}> with score: {Score}",
                    scoredCandidates[0].Node.Name,
                    scoredCandidates[0].Score);
                return scoredCandidates[0].Node;
            }
        }

        // Fallback: Score all div and section elements
        var divAndSections = document.DocumentNode.SelectNodes("//div | //section");
        if (divAndSections != null && divAndSections.Count > 0)
        {
            var scoredElements = divAndSections
                .Select(node => new
                {
                    Node = node,
                    Score = contentScorer.CalculateScore(node),
                    Confidence = contentScorer.GetConfidenceScore(node)
                })
                .Where(x => x.Confidence >= _extractionOptions.MinConfidenceThreshold)
                .OrderByDescending(x => x.Score)
                .ToList();

            if (scoredElements.Count > 0)
            {
                logger.LogInformation(
                    "Extracted main content from <{Element}> with score: {Score} (confidence: {Confidence:F2})",
                    scoredElements[0].Node.Name,
                    scoredElements[0].Score,
                    scoredElements[0].Confidence);
                return scoredElements[0].Node;
            }
        }

        // Last resort: Use body element
        logger.LogWarning("Could not identify main content area, using entire body");
        return document.DocumentNode.SelectSingleNode("//body") ?? document.DocumentNode;
    }

    /// <summary>
    /// Try to select a node using CSS selector syntax (limited support via XPath conversion).
    /// </summary>
    private static HtmlNode? TrySelectByCssSelector(HtmlDocument document, string selector)
    {
        try
        {
            // Handle common CSS selectors by converting to XPath
            string xpath = selector switch
            {
                var s when s.StartsWith('#') => $"//*[@id='{s[1..]}']",
                var s when s.StartsWith('.') => $"//*[contains(concat(' ', normalize-space(@class), ' '), ' {s[1..]} ')]",
                var s when s.StartsWith('[') && s.Contains("role") => $"//{s}",
                _ => $"//{selector}"
            };

            return document.DocumentNode.SelectSingleNode(xpath);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Resolve relative URLs to absolute URLs using the base URL.
    /// </summary>
    private void ResolveRelativeUrls(HtmlNode content, string baseUrlString)
    {
        if (!Uri.TryCreate(baseUrlString, UriKind.Absolute, out var baseUri))
        {
            logger.LogWarning("Invalid base URL for resolving relative URLs: {BaseUrl}", baseUrlString);
            return;
        }

        // Resolve links
        if (_extractionOptions.IncludeLinks)
        {
            var links = content.SelectNodes(".//a[@href]");
            if (links != null)
            {
                foreach (var link in links)
                {
                    var href = link.GetAttributeValue("href", string.Empty);
                    if (string.IsNullOrWhiteSpace(href) || href.StartsWith('#') || href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
                    {
                        // Remove fragment-only and javascript links
                        link.SetAttributeValue("href", string.Empty);
                        continue;
                    }

                    if (Uri.TryCreate(baseUri, href, out var absoluteUri))
                    {
                        link.SetAttributeValue("href", absoluteUri.ToString());
                    }
                }
            }
        }

        // Resolve images
        if (_extractionOptions.IncludeImages)
        {
            var images = content.SelectNodes(".//img[@src]");
            if (images != null)
            {
                foreach (var img in images)
                {
                    var src = img.GetAttributeValue("src", string.Empty);
                    if (!string.IsNullOrWhiteSpace(src) && Uri.TryCreate(baseUri, src, out var absoluteUri))
                    {
                        img.SetAttributeValue("src", absoluteUri.ToString());
                    }
                }
            }
        }
    }

    /// <summary>
    /// Conditionally remove elements that are likely not content based on various heuristics.
    /// Implements the conditional cleaning logic from Mozilla Readability.
    /// </summary>
    private void CleanConditionally(HtmlNode content)
    {
        var tagsToClean = new[] { "div", "section", "table", "ul", "ol" };

        foreach (var tagName in tagsToClean)
        {
            var nodes = content.SelectNodes($".//{tagName}");
            if (nodes == null)
            {
                continue;
            }

            foreach (var node in nodes.ToList())
            {
                var score = contentScorer.CalculateScore(node);
                var linkDensity = contentScorer.CalculateLinkDensity(node);
                var textContent = (node.InnerText ?? string.Empty).Trim();
                var textLength = textContent.Length;

                bool shouldRemove = false;

                // Remove if score is negative (clearly non-content)
                if (score < 0)
                {
                    shouldRemove = true;
                }

                // Remove if very short and no images
                if (textLength < _extractionOptions.MinTextLength)
                {
                    var hasImages = node.SelectNodes(".//img")?.Count > 0;
                    if (!hasImages)
                    {
                        shouldRemove = true;
                    }
                }

                // Remove if high link density and low score
                if (_extractionOptions.PenalizeLinkDensity &&
                    linkDensity > _extractionOptions.MaxLinkDensity &&
                    score < 25)
                {
                    shouldRemove = true;
                }

                // Remove if more list items than paragraphs (likely navigation)
                var listItems = node.SelectNodes(".//li")?.Count ?? 0;
                var paragraphs = node.SelectNodes(".//p")?.Count ?? 0;
                if (listItems > paragraphs && listItems > 3)
                {
                    shouldRemove = true;
                }

                if (shouldRemove)
                {
                    logger.LogTrace(
                        "Removing {TagName} element conditionally (score: {Score}, linkDensity: {LinkDensity:F2}, textLength: {TextLength})",
                        tagName,
                        score,
                        linkDensity,
                        textLength);
                    node.Remove();
                }
            }
        }
    }
}
