using SemanticHub.IngestionService.Models;

namespace SemanticHub.IngestionService.Domain.Ports;

/// <summary>
/// Converts markup formats and frontmatter metadata.
/// </summary>
public interface IMarkdownConverter
{
    string ConvertToMarkdown(ScrapedPage scrapedPage);

    List<string> ConvertToMarkdownBatch(List<ScrapedPage> scrapedPages);

    Dictionary<string, object>? ParseFrontmatter(string markdown);

    string ExtractContent(string markdown);
}
