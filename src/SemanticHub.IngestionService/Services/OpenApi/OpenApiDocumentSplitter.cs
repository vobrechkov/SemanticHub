using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;
using SemanticHub.IngestionService.Configuration;
using SemanticHub.IngestionService.Domain.OpenApi;
using SemanticHub.IngestionService.Domain.Ports;
using SemanticHub.IngestionService.Models;

namespace SemanticHub.IngestionService.Services.OpenApi;

public sealed class OpenApiDocumentSplitter : IOpenApiDocumentSplitter
{
    private readonly ILogger<OpenApiDocumentSplitter> _logger;
    private readonly int _maxSegmentLength;

    public OpenApiDocumentSplitter(
        ILogger<OpenApiDocumentSplitter> logger,
        OpenApiIngestionOptions options)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);

        if (options.MaxMarkdownSegmentLength <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.MaxMarkdownSegmentLength),
                options.MaxMarkdownSegmentLength,
                "MaxMarkdownSegmentLength must be greater than zero.");
        }

        _logger = logger;
        _maxSegmentLength = options.MaxMarkdownSegmentLength;
    }

    public IReadOnlyList<OpenApiEndpointDocument> Split(
        OpenApiSpecificationDocument specification,
        OpenApiEndpoint endpoint,
        string markdown)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(markdown);

        if (markdown.Length <= _maxSegmentLength)
        {
            return new[]
            {
                new OpenApiEndpointDocument(endpoint, markdown, 1, 1)
            };
        }

        var segments = SplitByHeadings(markdown);
        if (segments.Count == 0)
        {
            segments = SplitByLength(markdown);
        }

        var documents = new List<OpenApiEndpointDocument>(segments.Count);
        for (var i = 0; i < segments.Count; i++)
        {
            documents.Add(new OpenApiEndpointDocument(endpoint, segments[i], i + 1, segments.Count));
        }

        _logger.LogInformation(
            "Split Markdown for {Method} {Path} into {Count} segments (length {Length}).",
            endpoint.Method,
            endpoint.Path,
            documents.Count,
            markdown.Length);

        return documents;
    }

    private List<string> SplitByHeadings(string markdown)
    {
        var segments = new List<string>();
        var current = new StringBuilder();
        var reader = new StringReader(markdown);
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            var shouldStartNewSegment =
                current.Length > 0 &&
                current.Length >= _maxSegmentLength &&
                line.StartsWith("##", StringComparison.Ordinal);

            if (shouldStartNewSegment)
            {
                segments.Add(current.ToString());
                current.Clear();
            }

            current.AppendLine(line);
        }

        if (current.Length > 0)
        {
            segments.Add(current.ToString());
        }

        return segments.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
    }

    private List<string> SplitByLength(string markdown)
    {
        var segments = new List<string>();
        for (var index = 0; index < markdown.Length; index += _maxSegmentLength)
        {
            var length = Math.Min(_maxSegmentLength, markdown.Length - index);
            segments.Add(markdown.Substring(index, length));
        }

        return segments;
    }
}
