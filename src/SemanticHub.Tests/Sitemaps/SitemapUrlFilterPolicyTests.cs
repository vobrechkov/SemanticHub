using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Moq;
using SemanticHub.IngestionService.Configuration;
using SemanticHub.IngestionService.Domain.Aggregates;
using SemanticHub.IngestionService.Domain.Sitemaps;
using SemanticHub.IngestionService.Services.Sitemaps;

namespace SemanticHub.Tests.Sitemaps;

public class SitemapUrlFilterPolicyTests
{
    [Fact]
    public async Task ShouldIncludeAsync_AllowsWhitelistedHost()
    {
        var handler = new StubHttpHandler("User-agent: *\nDisallow:");
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var options = new IngestionOptions();
        var policy = new SitemapUrlFilterPolicy(httpClient, Mock.Of<ILogger<SitemapUrlFilterPolicy>>(), options);

        var metadata = IngestionMetadata.Create(null, "Example", "sitemap", new Uri("https://example.com/sitemap.xml"), null, null);
        var settings = new SitemapIngestionSettings
        {
            AllowedHosts = new[] { "example.com" }
        };
        var context = new SitemapIngestionContext(new Uri("https://example.com/sitemap.xml"), settings, options.Sitemap, metadata);

        var entry = new SitemapEntry
        {
            Location = new Uri("https://example.com/page"),
            ChangeFrequency = "daily",
            LastModified = DateTimeOffset.UtcNow
        };

        var allowed = await policy.ShouldIncludeAsync(entry, context, CancellationToken.None);

        Assert.True(allowed);
    }

    [Fact]
    public async Task ShouldIncludeAsync_BlocksNonWhitelistedHost()
    {
        var handler = new StubHttpHandler("User-agent: *\nDisallow:");
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var options = new IngestionOptions();
        var policy = new SitemapUrlFilterPolicy(httpClient, Mock.Of<ILogger<SitemapUrlFilterPolicy>>(), options);

        var metadata = IngestionMetadata.Create(null, "Example", "sitemap", new Uri("https://example.com/sitemap.xml"), null, null);
        var settings = new SitemapIngestionSettings
        {
            AllowedHosts = new[] { "example.com" }
        };
        var context = new SitemapIngestionContext(new Uri("https://example.com/sitemap.xml"), settings, options.Sitemap, metadata);

        var entry = new SitemapEntry
        {
            Location = new Uri("https://othersite.com/page"),
            ChangeFrequency = "daily",
            LastModified = DateTimeOffset.UtcNow
        };

        var allowed = await policy.ShouldIncludeAsync(entry, context, CancellationToken.None);

        Assert.False(allowed);
    }

    [Fact]
    public async Task ShouldIncludeAsync_RespectsRobotsTxt()
    {
        const string robots = """
            User-agent: *
            Disallow: /blocked
            """;

        var handler = new StubHttpHandler(robots);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var options = new IngestionOptions();
        var policy = new SitemapUrlFilterPolicy(httpClient, Mock.Of<ILogger<SitemapUrlFilterPolicy>>(), options);

        var metadata = IngestionMetadata.Create(null, "Example", "sitemap", new Uri("https://example.com/sitemap.xml"), null, null);
        var settings = new SitemapIngestionSettings();
        var context = new SitemapIngestionContext(new Uri("https://example.com/sitemap.xml"), settings, options.Sitemap, metadata);

        var blockedEntry = new SitemapEntry
        {
            Location = new Uri("https://example.com/blocked/page"),
            ChangeFrequency = "weekly"
        };

        var allowedEntry = new SitemapEntry
        {
            Location = new Uri("https://example.com/open/page"),
            ChangeFrequency = "weekly"
        };

        Assert.False(await policy.ShouldIncludeAsync(blockedEntry, context, CancellationToken.None));
        Assert.True(await policy.ShouldIncludeAsync(allowedEntry, context, CancellationToken.None));
    }

    [Fact]
    public async Task ShouldIncludeAsync_IgnoresRobotsWhenDisabled()
    {
        const string robots = """
            User-agent: *
            Disallow: /
            """;

        var handler = new StubHttpHandler(robots);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var options = new IngestionOptions();
        var policy = new SitemapUrlFilterPolicy(httpClient, Mock.Of<ILogger<SitemapUrlFilterPolicy>>(), options);

        var metadata = IngestionMetadata.Create(null, "Example", "sitemap", new Uri("https://example.com/sitemap.xml"), null, null);
        var settings = new SitemapIngestionSettings
        {
            RespectRobotsTxt = false
        };
        var context = new SitemapIngestionContext(new Uri("https://example.com/sitemap.xml"), settings, options.Sitemap, metadata);

        var entry = new SitemapEntry
        {
            Location = new Uri("https://example.com/blocked"),
            ChangeFrequency = "daily"
        };

        var allowed = await policy.ShouldIncludeAsync(entry, context, CancellationToken.None);

        Assert.True(allowed);
    }

    private sealed class StubHttpHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is null)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            if (request.RequestUri.AbsolutePath.Equals("/robots.txt", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body)
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(string.Empty)
            });
        }
    }
}
