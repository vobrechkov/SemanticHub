using System;
using Microsoft.Extensions.Logging.Abstractions;
using SemanticHub.IngestionService.Models;
using SemanticHub.IngestionService.Services;
using Xunit;

namespace SemanticHub.Tests;

public class MarkdownConverterTests
{
    [Fact]
    public void ConvertToMarkdown_ShouldStripHtmlTags()
    {
        const string html = """
            <main class="doc-content">
                <h1 id="account-service-api">
                    Account Service API <span class="light-text">| Administrative - SOAP</span>
                </h1>
                <p>
                    The Account Service provides the clients with account data and functionality.
                </p>
                <ul>
                    <li>Clients shall format SOAP messages in a Document/Literal format over HTTPS.</li>
                </ul>
            </main>
            """;

        var page = new ScrapedPage
        {
            Url = "https://example.org/accountsoap",
            Title = "Account Service API",
            HtmlContent = html
        };

        var converter = new MarkdownConverter(NullLogger<MarkdownConverter>.Instance);

        var markdown = converter.ConvertToMarkdown(page);

        Assert.DoesNotContain("<h1", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<li>", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\n#", markdown);
        Assert.Contains("Account Service API | Administrative - SOAP", markdown);
        Assert.Contains("- Clients shall format SOAP messages", markdown);
    }
}
