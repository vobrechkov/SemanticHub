using ReverseMarkdown;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using SemanticHub.IngestionService.Models;
using SemanticHub.IngestionService.Domain.Ports;

namespace SemanticHub.IngestionService.Services;

/// <summary>
/// Converts HTML to Markdown with YAML frontmatter
/// </summary>
public class MarkdownConverter : IMarkdownConverter
{
    private readonly ILogger<MarkdownConverter> _logger;
    private readonly Converter _htmlToMarkdown;
    private readonly ISerializer _yamlSerializer;

    public MarkdownConverter(ILogger<MarkdownConverter> logger)
    {
        _logger = logger;

        // Configure ReverseMarkdown converter
        _htmlToMarkdown = new Converter(new Config
        {
            UnknownTags = Config.UnknownTagsOption.Bypass,
            GithubFlavored = true,
            RemoveComments = true,
            SmartHrefHandling = true
        });

        // Configure YAML serializer
        _yamlSerializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
    }

    /// <summary>
    /// Convert scraped HTML page to Markdown with YAML frontmatter.
    /// Note: HTML cleaning is now handled by HtmlProcessor before this step.
    /// </summary>
    public string ConvertToMarkdown(ScrapedPage scrapedPage)
    {
        _logger.LogInformation("Converting HTML to Markdown for: {Url}", scrapedPage.Url);

        try
        {
            // HTML is already cleaned by HtmlProcessor, convert directly to Markdown
            var markdownContent = _htmlToMarkdown.Convert(scrapedPage.HtmlContent);

            // Build YAML frontmatter
            var frontmatter = BuildFrontmatter(scrapedPage);

            // Combine frontmatter and content
            var result = $"---\n{frontmatter}---\n\n{markdownContent}";

            _logger.LogInformation("Successfully converted to Markdown: {Url}", scrapedPage.Url);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error converting HTML to Markdown for: {Url}", scrapedPage.Url);
            throw;
        }
    }

    /// <summary>
    /// Convert scraped pages to Markdown documents
    /// </summary>
    public List<string> ConvertToMarkdownBatch(List<ScrapedPage> scrapedPages)
    {
        _logger.LogInformation("Converting {Count} pages to Markdown", scrapedPages.Count);

        var results = new List<string>();
        foreach (var page in scrapedPages)
        {
            try
            {
                var markdown = ConvertToMarkdown(page);
                results.Add(markdown);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to convert page: {Url}", page.Url);
                // Continue with other pages
            }
        }

        return results;
    }


    /// <summary>
    /// Build YAML frontmatter from scraped page metadata
    /// </summary>
    private string BuildFrontmatter(ScrapedPage scrapedPage)
    {
        var frontmatterData = new Dictionary<string, object>
        {
            ["title"] = scrapedPage.Title,
            ["url"] = scrapedPage.Url,
            ["sourceType"] = "webpage",
            ["scrapedAt"] = scrapedPage.ScrapedAt.ToString("O") // ISO 8601 format
        };

        // Add metadata from page
        if (scrapedPage.Metadata.TryGetValue("description", out var description))
        {
            frontmatterData["description"] = description;
        }

        if (scrapedPage.Metadata.TryGetValue("author", out var author))
        {
            frontmatterData["author"] = author;
        }

        if (scrapedPage.Metadata.TryGetValue("keywords", out var keywords))
        {
            // Split keywords into array
            var keywordArray = keywords.Split(',', StringSplitOptions.TrimEntries);
            frontmatterData["keywords"] = keywordArray;
        }

        if (scrapedPage.Metadata.TryGetValue("published", out var published))
        {
            frontmatterData["published"] = published;
        }

        // Add any additional metadata
        foreach (var kvp in scrapedPage.Metadata)
        {
            if (!frontmatterData.ContainsKey(kvp.Key))
            {
                frontmatterData[kvp.Key] = kvp.Value;
            }
        }

        return _yamlSerializer.Serialize(frontmatterData);
    }

    /// <summary>
    /// Parse YAML frontmatter from Markdown content
    /// </summary>
    public Dictionary<string, object>? ParseFrontmatter(string markdown)
    {
        try
        {
            // Check if document has frontmatter
            if (!markdown.StartsWith("---"))
            {
                return null;
            }

            // Find the end of frontmatter
            var endIndex = markdown.IndexOf("---", 3);
            if (endIndex == -1)
            {
                return null;
            }

            // Extract frontmatter
            var frontmatterYaml = markdown.Substring(3, endIndex - 3).Trim();

            // Deserialize YAML
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            return deserializer.Deserialize<Dictionary<string, object>>(frontmatterYaml);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse YAML frontmatter");
            return null;
        }
    }

    /// <summary>
    /// Extract content without frontmatter
    /// </summary>
    public string ExtractContent(string markdown)
    {
        if (!markdown.StartsWith("---"))
        {
            return markdown;
        }

        var endIndex = markdown.IndexOf("---", 3);
        if (endIndex == -1)
        {
            return markdown;
        }

        return markdown.Substring(endIndex + 3).Trim();
    }
}
