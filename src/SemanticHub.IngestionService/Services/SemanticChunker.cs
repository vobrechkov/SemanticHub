using SemanticHub.IngestionService.Models;
using System.Text;
using System.Text.RegularExpressions;

namespace SemanticHub.IngestionService.Services;

/// <summary>
/// Intelligent semantic chunking service for Markdown documents
/// Chunks based on document structure (headers, paragraphs) rather than fixed sizes
/// </summary>
public class SemanticChunker(
    ILogger<SemanticChunker> logger,
    int minChunkSize = 200,
    int targetChunkSize = 400,
    int maxChunkSize = 500,
    double overlapPercentage = 0.1)
{

    /// <summary>
    /// Chunk a Markdown document semantically
    /// </summary>
    public List<DocumentChunk> ChunkMarkdown(
        string markdownContent,
        string documentId,
        DocumentMetadata metadata)
    {
        logger.LogInformation("Chunking document: {DocumentId}", documentId);

        var chunks = new List<DocumentChunk>();
        var sections = ParseMarkdownSections(markdownContent);

        int chunkIndex = 0;
        int position = 0;

        foreach (var section in sections)
        {
            // If section is small enough, create a single chunk
            if (EstimateTokenCount(section.Content) <= targetChunkSize)
            {
                var chunk = new DocumentChunk
                {
                    Id = $"{documentId}_chunk_{chunkIndex}",
                    ParentDocumentId = documentId,
                    ChunkIndex = chunkIndex,
                    Title = section.Title,
                    Content = section.Content,
                    Metadata = metadata,
                    TokenCount = EstimateTokenCount(section.Content),
                    StartPosition = position,
                    EndPosition = position + section.Content.Length
                };

                chunks.Add(chunk);
                chunkIndex++;
                position += section.Content.Length;
            }
            else
            {
                // Section is too large, split into smaller chunks with overlap
                var subChunks = SplitLargeSection(section, documentId, chunkIndex, position, metadata);
                chunks.AddRange(subChunks);
                chunkIndex += subChunks.Count;
                position += section.Content.Length;
            }
        }

        // Filter out any empty or whitespace-only chunks
        var validChunks = chunks.Where(c => IsValidChunk(c)).ToList();

        if (validChunks.Count < chunks.Count)
        {
            logger.LogWarning(
                "Filtered {FilteredCount} empty chunks from document {DocumentId}. Valid chunks: {ValidCount}",
                chunks.Count - validChunks.Count,
                documentId,
                validChunks.Count);
        }

        logger.LogInformation("Created {Count} chunks for document: {DocumentId}", validChunks.Count, documentId);
        return validChunks;
    }

    /// <summary>
    /// Parse Markdown into semantic sections based on headers
    /// </summary>
    private List<MarkdownSection> ParseMarkdownSections(string markdown)
    {
        var sections = new List<MarkdownSection>();
        var lines = markdown.Split('\n');

        var currentSection = new StringBuilder();
        string? currentTitle = null;
        int currentLevel = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            // Check if line is a header
            var headerMatch = Regex.Match(line, @"^(#{1,6})\s+(.+)$");

            if (headerMatch.Success)
            {
                // Save previous section if it has content
                if (currentSection.Length > 0)
                {
                    sections.Add(new MarkdownSection
                    {
                        Title = currentTitle ?? "Untitled",
                        Content = currentSection.ToString().Trim(),
                        Level = currentLevel
                    });
                    currentSection.Clear();
                }

                // Start new section
                currentLevel = headerMatch.Groups[1].Value.Length;
                currentTitle = headerMatch.Groups[2].Value.Trim();
                currentSection.AppendLine(line);
            }
            else
            {
                currentSection.AppendLine(line);
            }
        }

        // Add the last section
        if (currentSection.Length > 0)
        {
            sections.Add(new MarkdownSection
            {
                Title = currentTitle ?? "Untitled",
                Content = currentSection.ToString().Trim(),
                Level = currentLevel
            });
        }

        return sections;
    }

    /// <summary>
    /// Split a large section into smaller chunks with overlap using accumulator pattern
    /// </summary>
    private List<DocumentChunk> SplitLargeSection(
        MarkdownSection section,
        string documentId,
        int startChunkIndex,
        int startPosition,
        DocumentMetadata metadata)
    {
        var chunks = new List<DocumentChunk>();
        var paragraphs = SplitIntoParagraphs(section.Content);

        int chunkIndex = startChunkIndex;
        int currentPosition = startPosition;

        // Create accumulator without initial overlap for first chunk in section
        var accumulator = new ChunkAccumulator(
            minChunkSize,
            targetChunkSize,
            maxChunkSize,
            overlapPercentage,
            EstimateTokenCount,
            initialOverlap: null);

        foreach (var paragraph in paragraphs)
        {
            var paragraphTokens = EstimateTokenCount(paragraph);

            // If single paragraph exceeds max size, split it by sentences
            if (paragraphTokens > maxChunkSize)
            {
                // Finalize current chunk if it has content
                if (accumulator.HasContent)
                {
                    var chunk = accumulator.Finalize(documentId, chunkIndex++, section.Title, currentPosition, metadata);
                    if (chunk != null)
                    {
                        chunks.Add(chunk);
                    }

                    // Reset with overlap for next chunk
                    accumulator.Reset(includeOverlap: true);
                }

                // Split paragraph by sentences and process
                var sentences = SplitIntoSentences(paragraph);
                foreach (var sentence in sentences)
                {
                    if (!accumulator.TryAdd(sentence))
                    {
                        // Finalize current chunk
                        var chunk = accumulator.Finalize(documentId, chunkIndex++, section.Title, currentPosition, metadata);
                        if (chunk != null)
                        {
                            chunks.Add(chunk);
                        }

                        // Reset with overlap and try again
                        accumulator.Reset(includeOverlap: true);

                        // If still doesn't fit, force add it (sentence is huge)
                        if (!accumulator.TryAdd(sentence))
                        {
                            accumulator.ForceAdd(sentence);
                        }
                    }
                }
            }
            else
            {
                // Try to add paragraph to current chunk
                if (!accumulator.TryAdd(paragraph))
                {
                    // Finalize current chunk
                    var chunk = accumulator.Finalize(documentId, chunkIndex++, section.Title, currentPosition, metadata);
                    if (chunk != null)
                    {
                        chunks.Add(chunk);
                    }

                    // Reset with overlap and add paragraph to new chunk
                    accumulator.Reset(includeOverlap: true);

                    // Try adding to fresh chunk
                    if (!accumulator.TryAdd(paragraph))
                    {
                        // Paragraph is too large even for fresh chunk, force add
                        accumulator.ForceAdd(paragraph);
                    }
                }
            }
        }

        // Add final chunk if it has content
        if (accumulator.HasContent)
        {
            var finalChunk = accumulator.Finalize(documentId, chunkIndex, section.Title, currentPosition, metadata);
            if (finalChunk != null)
            {
                chunks.Add(finalChunk);
            }
        }

        return chunks;
    }

    /// <summary>
    /// Split text into paragraphs
    /// </summary>
    private List<string> SplitIntoParagraphs(string text)
    {
        // Split by double newlines (paragraph boundaries)
        return text
            .Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();
    }

    /// <summary>
    /// Split text into sentences
    /// </summary>
    private List<string> SplitIntoSentences(string text)
    {
        // Simple sentence splitting (can be improved with NLP libraries)
        return Regex
            .Split(text, @"(?<=[.!?])\s+")
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }

    /// <summary>
    /// Create a document chunk
    /// </summary>
    private DocumentChunk CreateChunk(
        string documentId,
        int chunkIndex,
        string? title,
        string content,
        int startPosition,
        DocumentMetadata metadata)
    {
        return new DocumentChunk
        {
            Id = $"{documentId}_chunk_{chunkIndex}",
            ParentDocumentId = documentId,
            ChunkIndex = chunkIndex,
            Title = title,
            Content = content,
            Metadata = metadata,
            TokenCount = EstimateTokenCount(content),
            StartPosition = startPosition,
            EndPosition = startPosition + content.Length
        };
    }

    /// <summary>
    /// Estimate token count (rough approximation: 1 token ≈ 4 characters)
    /// </summary>
    private int EstimateTokenCount(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        // Simple approximation: ~4 characters per token
        // This is a rough estimate; for precise counting, use a tokenizer library
        return (int)Math.Ceiling(text.Length / 4.0);
    }

    /// <summary>
    /// Validates that a chunk has meaningful content (not empty or whitespace-only)
    /// </summary>
    private static bool IsValidChunk(DocumentChunk chunk)
    {
        return !string.IsNullOrWhiteSpace(chunk.Content) && chunk.Content.Trim().Length > 0;
    }

    /// <summary>
    /// Batch chunk multiple documents
    /// </summary>
    public List<DocumentChunk> ChunkDocuments(
        List<(string content, string documentId, DocumentMetadata metadata)> documents)
    {
        logger.LogInformation("Chunking {Count} documents", documents.Count);

        var allChunks = new List<DocumentChunk>();

        foreach (var (content, documentId, metadata) in documents)
        {
            try
            {
                var chunks = ChunkMarkdown(content, documentId, metadata);
                allChunks.AddRange(chunks);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to chunk document: {DocumentId}", documentId);
            }
        }

        logger.LogInformation("Created total of {Count} chunks from {DocCount} documents",
            allChunks.Count, documents.Count);

        return allChunks;
    }
}

/// <summary>
/// Represents a semantic section in a Markdown document
/// </summary>
internal class MarkdownSection
{
    public required string Title { get; set; }
    public required string Content { get; set; }
    public int Level { get; set; }
}
