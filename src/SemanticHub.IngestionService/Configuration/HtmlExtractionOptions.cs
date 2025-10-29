namespace SemanticHub.IngestionService.Configuration;

/// <summary>
/// Configuration options for HTML content extraction and cleaning.
/// </summary>
public class HtmlExtractionOptions
{
    /// <summary>
    /// Minimum confidence score (0.0 to 1.0) required to consider an element as main content.
    /// Default: 0.6
    /// </summary>
    public double MinConfidenceThreshold { get; set; } = 0.6;

    /// <summary>
    /// Maximum acceptable link density (0.0 to 1.0) for content elements.
    /// Elements with higher link density are likely navigation/sidebars.
    /// Default: 0.3
    /// </summary>
    public double MaxLinkDensity { get; set; } = 0.3;

    /// <summary>
    /// Minimum text length (in characters) for content to be considered valid.
    /// Default: 25
    /// </summary>
    public int MinTextLength { get; set; } = 25;

    /// <summary>
    /// Minimum number of paragraphs expected in main content.
    /// Default: 3
    /// </summary>
    public int MinParagraphCount { get; set; } = 3;

    /// <summary>
    /// Enable aggressive boilerplate removal (may remove some valid content).
    /// Default: false
    /// </summary>
    public bool AggressiveCleaning { get; set; } = false;

    /// <summary>
    /// CSS selectors for elements to always remove (beyond defaults).
    /// </summary>
    public List<string> RemoveSelectors { get; set; } = [];

    /// <summary>
    /// CSS selectors to prioritize when searching for main content.
    /// </summary>
    public List<string> ContentSelectors { get; set; } = [];

    /// <summary>
    /// Additional CSS class names to remove (exact match).
    /// </summary>
    public List<string> RemoveClassNames { get; set; } = [];

    /// <summary>
    /// Patterns for negative class/ID matching (regex).
    /// </summary>
    public string? NegativePattern { get; set; }

    /// <summary>
    /// Patterns for positive class/ID matching (regex).
    /// </summary>
    public string? PositivePattern { get; set; }

    /// <summary>
    /// Include images in the extracted content.
    /// Default: true
    /// </summary>
    public bool IncludeImages { get; set; } = true;

    /// <summary>
    /// Include links in the extracted content.
    /// Default: true
    /// </summary>
    public bool IncludeLinks { get; set; } = true;

    /// <summary>
    /// Include tables in the extracted content.
    /// Default: true
    /// </summary>
    public bool IncludeTables { get; set; } = true;

    /// <summary>
    /// Convert relative URLs to absolute URLs using the page's base URL.
    /// Default: true
    /// </summary>
    public bool ResolveRelativeUrls { get; set; } = true;

    /// <summary>
    /// Remove elements with high link density even if they score well otherwise.
    /// Default: true
    /// </summary>
    public bool PenalizeLinkDensity { get; set; } = true;

    /// <summary>
    /// Gets the default extraction options with comprehensive removal rules.
    /// </summary>
    public static HtmlExtractionOptions Default => new()
    {
        RemoveSelectors =
        [
            "script",
            "style",
            "noscript",
            "nav",
            "header:not(article header):not(main header)",
            "footer:not(article footer):not(main footer)",
            "aside",
            ".advertisement",
            ".ad",
            ".ads",
            ".ad-wrapper",
            ".ad-container",
            ".sidebar",
            ".widget",
            ".comments",
            "#comments",
            ".comment-section",
            ".social-share",
            ".share-buttons",
            ".related-posts",
            ".recommended",
            ".newsletter",
            ".subscribe",
            ".cookie-notice",
            ".cookie-banner",
            ".gdpr",
            ".popup",
            ".modal",
            ".breadcrumb",
            ".breadcrumbs",
            ".pagination",
            ".pager",
            "#disqus_thread",
            ".disqus"
        ],
        ContentSelectors =
        [
            "article",
            "main",
            "[role=main]",
            "#content",
            ".content",
            "#main",
            ".main",
            ".post-content",
            ".entry-content",
            ".article-content",
            ".article-body",
            ".post-body"
        ],
        RemoveClassNames =
        [
            "navigation",
            "navbar",
            "nav-bar",
            "nav-menu",
            "sidebar",
            "side-bar",
            "breadcrumb",
            "breadcrumbs",
            "menu",
            "advertisement",
            "ad-block",
            "promo",
            "banner",
            "social",
            "share",
            "related",
            "recommended",
            "cookie-notice",
            "newsletter",
            "subscribe",
            "comment-form",
            "reply-form"
        ]
    };
}
