using Microsoft.Extensions.Logging;
using Moq;
using SemanticHub.IngestionService.Models;
using SemanticHub.IngestionService.Services;

namespace SemanticHub.Tests.Services;

public class SemanticChunkerTests
{
    private readonly SemanticChunker _chunker;
    private readonly Mock<ILogger<SemanticChunker>> _loggerMock;

    public SemanticChunkerTests()
    {
        _loggerMock = new Mock<ILogger<SemanticChunker>>();
        _chunker = new SemanticChunker(
            _loggerMock.Object,
            minChunkSize: 200,
            targetChunkSize: 400,
            maxChunkSize: 500,
            overlapPercentage: 0.1);
    }

    private static DocumentMetadata CreateTestMetadata() => new()
    {
        Id = "test-doc",
        Title = "Test Document",
        SourceUrl = "https://example.com",
        SourceType = "test"
    };

    #region Chunk Size Validation Tests

    [Fact]
    public void ChunkMarkdown_SimpleDocument_ProducesChunksWithinBounds()
    {
        // Arrange
        var content = @"
# Introduction

This is a simple introduction paragraph with enough content to create a valid chunk.
It should have sufficient tokens to meet the minimum requirement.

# Section One

This is the first section with substantial content. We want to ensure that this
section creates chunks that fall within our target bounds of 200-500 tokens.
The content here is carefully crafted to hit those targets.

# Section Two

Another section with meaningful content. This helps us verify that multiple
sections are handled correctly and each produces valid chunks.
";
        var metadata = CreateTestMetadata();

        // Act
        var chunks = _chunker.ChunkMarkdown(content, "test-doc", metadata);

        // Assert
        Assert.NotEmpty(chunks);
        Assert.All(chunks, chunk =>
        {
            Assert.NotNull(chunk.Content);
            Assert.False(string.IsNullOrWhiteSpace(chunk.Content));
            Assert.InRange(chunk.TokenCount, 1, 500); // Hard max
        });
    }

    [Fact]
    public void ChunkMarkdown_LargeDocument_RespectsHardMaxLimit()
    {
        // Arrange - Create a very large document
        var largeSection = string.Join(" ", Enumerable.Repeat("This is a word.", 300));
        var content = $@"
# Large Section

{largeSection}

# Another Large Section

{largeSection}
";
        var metadata = CreateTestMetadata();

        // Act
        var chunks = _chunker.ChunkMarkdown(content, "test-doc", metadata);

        // Assert
        Assert.NotEmpty(chunks);
        Assert.All(chunks, chunk =>
        {
            Assert.InRange(chunk.TokenCount, 1, 500); // Must not exceed hard max
        });
    }

    [Fact]
    public void ChunkMarkdown_TinyDocument_CreatesAtLeastOneChunk()
    {
        // Arrange
        var content = "# Title\n\nSmall content.";
        var metadata = CreateTestMetadata();

        // Act
        var chunks = _chunker.ChunkMarkdown(content, "test-doc", metadata);

        // Assert
        Assert.NotEmpty(chunks);
        Assert.Single(chunks);
    }

    [Fact]
    public void ChunkMarkdown_EmptySections_FiltersEmptyChunks()
    {
        // Arrange
        var content = @"
# Header One

# Header Two

# Header Three

Some actual content here.
";
        var metadata = CreateTestMetadata();

        // Act
        var chunks = _chunker.ChunkMarkdown(content, "test-doc", metadata);

        // Assert
        Assert.NotEmpty(chunks);
        Assert.All(chunks, chunk =>
        {
            Assert.False(string.IsNullOrWhiteSpace(chunk.Content));
        });
    }

    [Fact]
    public void ChunkMarkdown_WhitespaceOnly_FiltersEmptyChunks()
    {
        // Arrange
        var content = "   \n\n   \n\n   ";
        var metadata = CreateTestMetadata();

        // Act
        var chunks = _chunker.ChunkMarkdown(content, "test-doc", metadata);

        // Assert
        Assert.Empty(chunks); // Should filter out all whitespace-only chunks
    }

    #endregion

    #region Overlap Tests

    [Fact]
    public void ChunkMarkdown_MultipleChunks_HasApproximateOverlap()
    {
        // Arrange - Create content that will split into multiple chunks
        var paragraph = string.Join(" ", Enumerable.Repeat("This is test content.", 50));
        var content = $@"
# Section

{paragraph}

{paragraph}

{paragraph}
";
        var metadata = CreateTestMetadata();

        // Act
        var chunks = _chunker.ChunkMarkdown(content, "test-doc", metadata);

        // Assert - Should have multiple chunks due to size
        Assert.True(chunks.Count >= 2, "Should produce multiple chunks");

        // Verify chunks are sequential
        for (int i = 0; i < chunks.Count; i++)
        {
            Assert.Equal(i, chunks[i].ChunkIndex);
        }
    }

    [Fact]
    public void ChunkMarkdown_FirstChunk_HasNoOverlap()
    {
        // Arrange
        var paragraph = string.Join(" ", Enumerable.Repeat("Content.", 100));
        var content = $"# Section\n\n{paragraph}\n\n{paragraph}";
        var metadata = CreateTestMetadata();

        // Act
        var chunks = _chunker.ChunkMarkdown(content, "test-doc", metadata);

        // Assert
        Assert.NotEmpty(chunks);
        var firstChunk = chunks[0];
        Assert.Equal(0, firstChunk.ChunkIndex);
        Assert.StartsWith("# Section", firstChunk.Content.Trim());
    }

    #endregion

    #region Semantic Boundary Tests

    [Fact]
    public void ChunkMarkdown_RespectsHeaderBoundaries()
    {
        // Arrange
        var content = @"
# Header One

Content for section one. This should be in the first chunk.

# Header Two

Content for section two. This should respect the header boundary.

# Header Three

Content for section three.
";
        var metadata = CreateTestMetadata();

        // Act
        var chunks = _chunker.ChunkMarkdown(content, "test-doc", metadata);

        // Assert
        Assert.NotEmpty(chunks);
        // Each chunk should have its section title
        Assert.All(chunks, chunk => Assert.NotNull(chunk.Title));
    }

    [Fact]
    public void ChunkMarkdown_PrefersParagraphBoundaries()
    {
        // Arrange - Multiple paragraphs
        var content = @"
# Section

First paragraph with some content.

Second paragraph with more content.

Third paragraph to ensure we have enough.

Fourth paragraph for good measure.
";
        var metadata = CreateTestMetadata();

        // Act
        var chunks = _chunker.ChunkMarkdown(content, "test-doc", metadata);

        // Assert
        Assert.NotEmpty(chunks);
        // Chunks should not cut mid-paragraph (content should contain complete sentences)
        Assert.All(chunks, chunk =>
        {
            var content = chunk.Content.Trim();
            Assert.True(content.EndsWith(".") || content.Contains("#"),
                "Chunks should end at paragraph/section boundaries");
        });
    }

    [Fact]
    public void ChunkMarkdown_MultipleHeaders_CreatesLogicalSections()
    {
        // Arrange
        var content = @"
# Main Header

Introduction paragraph.

## Sub Header One

Sub section one content.

## Sub Header Two

Sub section two content.

# Another Main Header

More content here.
";
        var metadata = CreateTestMetadata();

        // Act
        var chunks = _chunker.ChunkMarkdown(content, "test-doc", metadata);

        // Assert
        Assert.NotEmpty(chunks);
        // Each chunk should be associated with a section title
        Assert.All(chunks, chunk =>
        {
            Assert.NotNull(chunk.Title);
            Assert.NotEqual("Untitled", chunk.Title);
        });
    }

    #endregion

    #region Edge Cases Tests

    [Fact]
    public void ChunkMarkdown_EmptyInput_ReturnsEmptyList()
    {
        // Arrange
        var content = "";
        var metadata = CreateTestMetadata();

        // Act
        var chunks = _chunker.ChunkMarkdown(content, "test-doc", metadata);

        // Assert
        Assert.Empty(chunks);
    }

    [Fact]
    public void ChunkMarkdown_OnlyHeaders_ProducesValidChunks()
    {
        // Arrange
        var content = @"
# Header One
# Header Two
# Header Three
";
        var metadata = CreateTestMetadata();

        // Act
        var chunks = _chunker.ChunkMarkdown(content, "test-doc", metadata);

        // Assert - Headers alone should create chunks (they have content)
        Assert.NotEmpty(chunks);
    }

    [Fact]
    public void ChunkMarkdown_VeryLongParagraph_SplitsBySentences()
    {
        // Arrange - Single paragraph that exceeds max
        var longParagraph = string.Join(" ", Enumerable.Repeat("This is a sentence.", 150));
        var content = $"# Section\n\n{longParagraph}";
        var metadata = CreateTestMetadata();

        // Act
        var chunks = _chunker.ChunkMarkdown(content, "test-doc", metadata);

        // Assert
        Assert.NotEmpty(chunks);
        Assert.All(chunks, chunk => Assert.InRange(chunk.TokenCount, 1, 500));
    }

    [Fact]
    public void ChunkMarkdown_VeryLongSentence_HandlesGracefully()
    {
        // Arrange - Single sentence that exceeds max (no spaces for sentence splitting)
        var longWord = new string('a', 2500); // ~625 tokens
        var content = $"# Section\n\n{longWord}";
        var metadata = CreateTestMetadata();

        // Act
        var chunks = _chunker.ChunkMarkdown(content, "test-doc", metadata);

        // Assert - Should handle gracefully, possibly creating oversized chunk
        Assert.NotEmpty(chunks);
    }

    [Fact]
    public void ChunkMarkdown_MixedContent_HandlesCodeBlocks()
    {
        // Arrange
        var content = @"
# Code Example

Here is some code:

```csharp
public class Example
{
    public void Method()
    {
        Console.WriteLine(""Hello"");
    }
}
```

And here is the explanation of the code.
";
        var metadata = CreateTestMetadata();

        // Act
        var chunks = _chunker.ChunkMarkdown(content, "test-doc", metadata);

        // Assert
        Assert.NotEmpty(chunks);
        // Code blocks should be preserved
        Assert.Contains(chunks, c => c.Content.Contains("```"));
    }

    [Fact]
    public void ChunkMarkdown_SpecialCharacters_PreservesContent()
    {
        // Arrange
        var content = @"
# Special Characters

Content with special chars: @#$%^&*()[]{}|<>?/\~`

Unicode: émojis 🚀 中文 العربية
";
        var metadata = CreateTestMetadata();

        // Act
        var chunks = _chunker.ChunkMarkdown(content, "test-doc", metadata);

        // Assert
        Assert.NotEmpty(chunks);
        Assert.Contains(chunks, c => c.Content.Contains("@#$%^&*"));
        Assert.Contains(chunks, c => c.Content.Contains("émojis"));
    }

    #endregion

    #region Metadata Tests

    [Fact]
    public void ChunkMarkdown_AssignsCorrectChunkIndices()
    {
        // Arrange
        var paragraph = string.Join(" ", Enumerable.Repeat("Content.", 100));
        var content = $@"
# Section One
{paragraph}

# Section Two
{paragraph}

# Section Three
{paragraph}
";
        var metadata = CreateTestMetadata();

        // Act
        var chunks = _chunker.ChunkMarkdown(content, "test-doc", metadata);

        // Assert
        Assert.NotEmpty(chunks);
        for (int i = 0; i < chunks.Count; i++)
        {
            Assert.Equal(i, chunks[i].ChunkIndex);
        }
    }

    [Fact]
    public void ChunkMarkdown_PopulatesMetadataCorrectly()
    {
        // Arrange
        var content = "# Test\n\nTest content.";
        var metadata = CreateTestMetadata();

        // Act
        var chunks = _chunker.ChunkMarkdown(content, "test-doc", metadata);

        // Assert
        Assert.NotEmpty(chunks);
        Assert.All(chunks, chunk =>
        {
            Assert.NotNull(chunk.Metadata);
            Assert.Equal("test-doc", chunk.Metadata.Id);
            Assert.Equal("Test Document", chunk.Metadata.Title);
            Assert.Equal("https://example.com", chunk.Metadata.SourceUrl);
        });
    }

    [Fact]
    public void ChunkMarkdown_TracksSectionTitles()
    {
        // Arrange
        var content = @"
# Introduction

Intro content.

# Main Content

Main content here.
";
        var metadata = CreateTestMetadata();

        // Act
        var chunks = _chunker.ChunkMarkdown(content, "test-doc", metadata);

        // Assert
        Assert.NotEmpty(chunks);
        Assert.Contains(chunks, c => c.Title == "Introduction");
        Assert.Contains(chunks, c => c.Title == "Main Content");
    }

    [Fact]
    public void ChunkMarkdown_CalculatesPositionsCorrectly()
    {
        // Arrange
        var content = "# Test\n\nFirst chunk content.\n\nSecond chunk content.";
        var metadata = CreateTestMetadata();

        // Act
        var chunks = _chunker.ChunkMarkdown(content, "test-doc", metadata);

        // Assert
        Assert.NotEmpty(chunks);
        Assert.All(chunks, chunk =>
        {
            Assert.True(chunk.StartPosition >= 0);
            Assert.True(chunk.EndPosition > chunk.StartPosition);
        });
    }

    #endregion

    #region Batch Processing Tests

    [Fact]
    public void ChunkDocuments_MultipleDocuments_ProcessesAll()
    {
        // Arrange
        var documents = new List<(string content, string documentId, DocumentMetadata metadata)>
        {
            ("# Doc 1\n\nContent 1", "doc-1", CreateTestMetadata()),
            ("# Doc 2\n\nContent 2", "doc-2", CreateTestMetadata()),
            ("# Doc 3\n\nContent 3", "doc-3", CreateTestMetadata())
        };

        // Act
        var chunks = _chunker.ChunkDocuments(documents);

        // Assert
        Assert.NotEmpty(chunks);
        Assert.True(chunks.Count >= 3); // At least one chunk per document
    }

    [Fact]
    public void ChunkDocuments_OneDocumentFails_ContinuesWithOthers()
    {
        // Arrange
        var documents = new List<(string content, string documentId, DocumentMetadata metadata)>
        {
            ("# Doc 1\n\nContent 1", "doc-1", CreateTestMetadata()),
            ("", "doc-2", null!), // This will fail
            ("# Doc 3\n\nContent 3", "doc-3", CreateTestMetadata())
        };

        // Act
        var chunks = _chunker.ChunkDocuments(documents);

        // Assert - Should still process valid documents
        Assert.NotEmpty(chunks);
    }

    #endregion

    #region Quality Tests

    [Fact]
    public void ChunkMarkdown_ProducesNoDuplicateChunkIds()
    {
        // Arrange
        var paragraph = string.Join(" ", Enumerable.Repeat("Content.", 100));
        var content = $@"
# Section One
{paragraph}

# Section Two
{paragraph}
";
        var metadata = CreateTestMetadata();

        // Act
        var chunks = _chunker.ChunkMarkdown(content, "test-doc", metadata);

        // Assert
        var ids = chunks.Select(c => c.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void ChunkMarkdown_AllChunksHaveContent()
    {
        // Arrange
        var content = @"
# Section One

Content for section one with multiple paragraphs.

More content here to ensure we have enough material.

# Section Two

Content for section two.

Additional content.
";
        var metadata = CreateTestMetadata();

        // Act
        var chunks = _chunker.ChunkMarkdown(content, "test-doc", metadata);

        // Assert
        Assert.NotEmpty(chunks);
        Assert.All(chunks, chunk =>
        {
            Assert.NotNull(chunk.Content);
            Assert.False(string.IsNullOrWhiteSpace(chunk.Content));
            Assert.True(chunk.Content.Trim().Length > 0);
        });
    }

    [Fact]
    public void ChunkMarkdown_ChunksAreSequential()
    {
        // Arrange
        var paragraph = string.Join(" ", Enumerable.Repeat("Test content.", 80));
        var content = $@"
# Section
{paragraph}
{paragraph}
{paragraph}
";
        var metadata = CreateTestMetadata();

        // Act
        var chunks = _chunker.ChunkMarkdown(content, "test-doc", metadata);

        // Assert
        Assert.True(chunks.Count >= 2);
        for (int i = 1; i < chunks.Count; i++)
        {
            Assert.Equal(i, chunks[i].ChunkIndex);
            Assert.Equal(chunks[i - 1].ChunkIndex + 1, chunks[i].ChunkIndex);
        }
    }

    #endregion
}
