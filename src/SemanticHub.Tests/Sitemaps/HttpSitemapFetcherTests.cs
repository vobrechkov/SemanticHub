using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging;
using Moq;
using SemanticHub.IngestionService.Configuration;
using SemanticHub.IngestionService.Services.Sitemaps;

namespace SemanticHub.Tests.Sitemaps;

public class HttpSitemapFetcherTests
{
    [Fact]
    public async Task FetchAsync_DecompressesGzipContent()
    {
        const string sitemap = """
            <?xml version=\"1.0\"?><urlset><url><loc>https://example.com/</loc></url></urlset>
            """;

        var handler = new StubHandler(request =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Gzip(sitemap))
            };
            response.Content.Headers.ContentEncoding.Add("gzip");
            return response;
        });

        var options = new IngestionOptions();
        var fetcher = new HttpSitemapFetcher(new HttpClient(handler), Mock.Of<ILogger<HttpSitemapFetcher>>(), options);

        var result = await fetcher.FetchAsync(new Uri("https://example.com/sitemap.xml"), CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Document);
        Assert.Contains("urlset", result.Document!.Content);
    }

    [Fact]
    public async Task FetchAsync_RejectsOversizedContent()
    {
        var handler = new StubHandler(request =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(new string('x', 1024))
            };
            response.Content.Headers.ContentLength = 1024;
            return response;
        });

        var options = new IngestionOptions();
        options.Sitemap.MaxSitemapBytes = 100;

        var fetcher = new HttpSitemapFetcher(new HttpClient(handler), Mock.Of<ILogger<HttpSitemapFetcher>>(), options);

        var result = await fetcher.FetchAsync(new Uri("https://example.com/sitemap.xml"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, result.StatusCode);
    }

    [Fact]
    public async Task FetchAsync_ReturnsFailureForNonSuccessStatus()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var fetcher = new HttpSitemapFetcher(new HttpClient(handler), Mock.Of<ILogger<HttpSitemapFetcher>>(), new IngestionOptions());

        var result = await fetcher.FetchAsync(new Uri("https://example.com/sitemap.xml"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
    }

    private static byte[] Gzip(string value)
    {
        using var memory = new MemoryStream();
        using (var gzip = new GZipStream(memory, CompressionMode.Compress, leaveOpen: true))
        using (var writer = new StreamWriter(gzip, Encoding.UTF8))
        {
            writer.Write(value);
        }

        return memory.ToArray();
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responder(request));
        }
    }
}
