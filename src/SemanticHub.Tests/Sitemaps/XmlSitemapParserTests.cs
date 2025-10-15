using Microsoft.Extensions.Logging;
using Moq;
using SemanticHub.IngestionService.Services.Sitemaps;

namespace SemanticHub.Tests.Sitemaps;

public class XmlSitemapParserTests
{
    [Fact]
    public void Parse_UrlSet_ReturnsEntries()
    {
        var parser = new XmlSitemapParser(Mock.Of<ILogger<XmlSitemapParser>>());
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
              <url>
                <loc>https://example.com/</loc>
                <lastmod>2024-08-01</lastmod>
                <changefreq>daily</changefreq>
                <priority>0.8</priority>
              </url>
              <url>
                <loc>/about</loc>
                <changefreq>monthly</changefreq>
              </url>
            </urlset>
            """;

        var result = parser.Parse(new Uri("https://example.com/sitemap.xml"), xml);

        Assert.Equal(2, result.Entries.Count);
        Assert.Empty(result.ChildSitemaps);
        Assert.Equal("https://example.com/", result.Entries[0].Location.AbsoluteUri);
        Assert.Equal("daily", result.Entries[0].ChangeFrequency);
        Assert.Equal(0.8, result.Entries[0].Priority);
        Assert.Equal("https://example.com/about", result.Entries[1].Location.AbsoluteUri);
    }

    [Fact]
    public void Parse_SitemapIndex_ReturnsChildSitemaps()
    {
        var parser = new XmlSitemapParser(Mock.Of<ILogger<XmlSitemapParser>>());
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <sitemapindex xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
              <sitemap>
                <loc>https://example.com/posts.xml</loc>
              </sitemap>
              <sitemap>
                <loc>blog.xml</loc>
              </sitemap>
            </sitemapindex>
            """;

        var result = parser.Parse(new Uri("https://example.com/root.xml"), xml);

        Assert.Empty(result.Entries);
        Assert.Equal(2, result.ChildSitemaps.Count);
        Assert.Equal("https://example.com/posts.xml", result.ChildSitemaps[0].AbsoluteUri);
        Assert.Equal("https://example.com/blog.xml", result.ChildSitemaps[1].AbsoluteUri);
    }
}
