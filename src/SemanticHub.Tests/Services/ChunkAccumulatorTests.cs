using SemanticHub.IngestionService.Models;
using SemanticHub.IngestionService.Services;

namespace SemanticHub.Tests.Services;

public class ChunkAccumulatorTests
{
    private static int SimpleTokenEstimator(string text) =>
        string.IsNullOrWhiteSpace(text) ? 0 : (int)Math.Ceiling(text.Length / 4.0);

    private static DocumentMetadata CreateTestMetadata() => new()
    {
        Id = "test-doc",
        Title = "Test Document",
        SourceUrl = "https://example.com",
        SourceType = "test"
    };

    [Fact]
    public void Constructor_ValidParameters_CreatesAccumulator()
    {
        // Arrange & Act
        var accumulator = new ChunkAccumulator(
            minTokenCount: 200,
            targetTokenCount: 400,
            maxTokenCount: 500,
            overlapPercentage: 0.1,
            tokenEstimator: SimpleTokenEstimator);

        // Assert
        Assert.NotNull(accumulator);
        Assert.False(accumulator.HasContent);
        Assert.Equal(0, accumulator.CurrentTokenCount);
    }

    [Theory]
    [InlineData(0, 400, 500)]      // Min <= 0
    [InlineData(-10, 400, 500)]    // Min negative
    [InlineData(400, 400, 500)]    // Min == Target
    [InlineData(450, 400, 500)]    // Min > Target
    [InlineData(200, 400, 400)]    // Target == Max
    [InlineData(200, 500, 400)]    // Target > Max
    public void Constructor_InvalidTokenCounts_ThrowsArgumentException(int min, int target, int max)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new ChunkAccumulator(
            min, target, max, 0.1, SimpleTokenEstimator));
    }

    [Theory]
    [InlineData(-0.1)]  // Negative
    [InlineData(1.1)]   // > 1
    [InlineData(2.0)]   // Way over 1
    public void Constructor_InvalidOverlapPercentage_ThrowsArgumentException(double overlap)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new ChunkAccumulator(
            200, 400, 500, overlap, SimpleTokenEstimator));
    }

    [Fact]
    public void Constructor_NullTokenEstimator_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ChunkAccumulator(
            200, 400, 500, 0.1, tokenEstimator: null!));
    }

    [Fact]
    public void Constructor_WithInitialOverlap_IncludesOverlapInContent()
    {
        // Arrange
        var initialOverlap = "This is some initial overlap content.";

        // Act
        var accumulator = new ChunkAccumulator(
            200, 400, 500, 0.1, SimpleTokenEstimator, initialOverlap);

        // Assert
        Assert.True(accumulator.HasContent);
        Assert.True(accumulator.CurrentTokenCount > 0);
        Assert.Equal(SimpleTokenEstimator(initialOverlap), accumulator.CurrentTokenCount);
    }

    [Fact]
    public void TryAdd_WithinBounds_ReturnsTrue()
    {
        // Arrange
        var accumulator = new ChunkAccumulator(200, 400, 500, 0.1, SimpleTokenEstimator);
        var segment = new string('x', 100); // ~25 tokens

        // Act
        var result = accumulator.TryAdd(segment);

        // Assert
        Assert.True(result);
        Assert.True(accumulator.HasContent);
        Assert.True(accumulator.CurrentTokenCount > 0);
    }

    [Fact]
    public void TryAdd_ExceedsMaxWithExistingContent_ReturnsFalse()
    {
        // Arrange
        var accumulator = new ChunkAccumulator(200, 400, 500, 0.1, SimpleTokenEstimator);
        var firstSegment = new string('x', 1600); // ~400 tokens
        var largeSegment = new string('y', 800);  // ~200 tokens (would exceed 500)

        accumulator.TryAdd(firstSegment);

        // Act
        var result = accumulator.TryAdd(largeSegment);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void TryAdd_EmptyOrWhitespace_ReturnsTrue()
    {
        // Arrange
        var accumulator = new ChunkAccumulator(200, 400, 500, 0.1, SimpleTokenEstimator);

        // Act & Assert
        Assert.True(accumulator.TryAdd(""));
        Assert.True(accumulator.TryAdd("   "));
        Assert.True(accumulator.TryAdd("\n\n"));
        Assert.False(accumulator.HasContent);
    }

    [Fact]
    public void TryAdd_MultipleSegments_AccumulatesCorrectly()
    {
        // Arrange
        var accumulator = new ChunkAccumulator(200, 400, 500, 0.1, SimpleTokenEstimator);
        var segment1 = new string('x', 400); // ~100 tokens
        var segment2 = new string('y', 400); // ~100 tokens
        var segment3 = new string('z', 400); // ~100 tokens

        // Act
        var result1 = accumulator.TryAdd(segment1);
        var result2 = accumulator.TryAdd(segment2);
        var result3 = accumulator.TryAdd(segment3);

        // Assert
        Assert.True(result1);
        Assert.True(result2);
        Assert.True(result3);
        Assert.True(accumulator.HasContent);
        Assert.InRange(accumulator.CurrentTokenCount, 250, 400); // ~300 tokens + separators
    }

    [Fact]
    public void CanFit_SegmentFits_ReturnsTrue()
    {
        // Arrange
        var accumulator = new ChunkAccumulator(200, 400, 500, 0.1, SimpleTokenEstimator);
        accumulator.TryAdd(new string('x', 800)); // ~200 tokens
        var testSegment = new string('y', 400);   // ~100 tokens

        // Act
        var result = accumulator.CanFit(testSegment);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void CanFit_SegmentDoesNotFit_ReturnsFalse()
    {
        // Arrange
        var accumulator = new ChunkAccumulator(200, 400, 500, 0.1, SimpleTokenEstimator);
        accumulator.TryAdd(new string('x', 1600)); // ~400 tokens
        var testSegment = new string('y', 800);    // ~200 tokens (would exceed 500)

        // Act
        var result = accumulator.CanFit(testSegment);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Finalize_WithContent_ReturnsValidChunk()
    {
        // Arrange
        var accumulator = new ChunkAccumulator(200, 400, 500, 0.1, SimpleTokenEstimator);
        accumulator.TryAdd("This is a test paragraph with some content.");
        var metadata = CreateTestMetadata();

        // Act
        var chunk = accumulator.Finalize("test-doc", 0, "Test Title", 0, metadata);

        // Assert
        Assert.NotNull(chunk);
        Assert.Equal("test-doc_chunk_0", chunk.Id);
        Assert.Equal("test-doc", chunk.ParentDocumentId);
        Assert.Equal(0, chunk.ChunkIndex);
        Assert.Equal("Test Title", chunk.Title);
        Assert.False(string.IsNullOrWhiteSpace(chunk.Content));
        Assert.True(chunk.TokenCount > 0);
    }

    [Fact]
    public void Finalize_EmptyContent_ReturnsNull()
    {
        // Arrange
        var accumulator = new ChunkAccumulator(200, 400, 500, 0.1, SimpleTokenEstimator);
        var metadata = CreateTestMetadata();

        // Act
        var chunk = accumulator.Finalize("test-doc", 0, "Test Title", 0, metadata);

        // Assert
        Assert.Null(chunk);
    }

    [Fact]
    public void Finalize_WhitespaceOnly_ReturnsNull()
    {
        // Arrange
        var accumulator = new ChunkAccumulator(200, 400, 500, 0.1, SimpleTokenEstimator);
        accumulator.ForceAdd("   \n\n   ");
        var metadata = CreateTestMetadata();

        // Act
        var chunk = accumulator.Finalize("test-doc", 0, "Test Title", 0, metadata);

        // Assert
        Assert.Null(chunk);
    }

    [Fact]
    public void Finalize_PreparesOverlapBuffer()
    {
        // Arrange
        var accumulator = new ChunkAccumulator(200, 400, 500, 0.1, SimpleTokenEstimator);
        var segment1 = new string('x', 800);  // ~200 tokens
        var segment2 = new string('y', 800);  // ~200 tokens
        accumulator.TryAdd(segment1);
        accumulator.TryAdd(segment2);

        // Act
        var chunk = accumulator.Finalize("test-doc", 0, "Title", 0, CreateTestMetadata());
        var overlapBuffer = accumulator.GetOverlapBuffer();

        // Assert
        Assert.NotNull(chunk);
        Assert.False(string.IsNullOrWhiteSpace(overlapBuffer));
        // Overlap should be ~10% of target (400) = ~40 tokens worth of content
        var overlapTokens = SimpleTokenEstimator(overlapBuffer);
        Assert.InRange(overlapTokens, 30, 50); // Some tolerance
    }

    [Fact]
    public void Reset_WithoutOverlap_ClearsAllContent()
    {
        // Arrange
        var accumulator = new ChunkAccumulator(200, 400, 500, 0.1, SimpleTokenEstimator);
        accumulator.TryAdd("Some content here");
        accumulator.Finalize("test-doc", 0, "Title", 0, CreateTestMetadata());

        // Act
        accumulator.Reset(includeOverlap: false);

        // Assert
        Assert.False(accumulator.HasContent);
        Assert.Equal(0, accumulator.CurrentTokenCount);
    }

    [Fact]
    public void Reset_WithOverlap_IncludesOverlapBuffer()
    {
        // Arrange
        var accumulator = new ChunkAccumulator(200, 400, 500, 0.1, SimpleTokenEstimator);
        accumulator.TryAdd(new string('x', 1600)); // ~400 tokens
        accumulator.Finalize("test-doc", 0, "Title", 0, CreateTestMetadata());

        // Act
        accumulator.Reset(includeOverlap: true);

        // Assert
        Assert.True(accumulator.HasContent);
        Assert.True(accumulator.CurrentTokenCount > 0);
        // Should have ~40 tokens of overlap (10% of 400)
        Assert.InRange(accumulator.CurrentTokenCount, 30, 50);
    }

    [Fact]
    public void ForceAdd_AddsContentRegardlessOfSize()
    {
        // Arrange
        var accumulator = new ChunkAccumulator(200, 400, 500, 0.1, SimpleTokenEstimator);
        var hugeSegment = new string('x', 3000); // ~750 tokens (exceeds max)

        // Act
        accumulator.ForceAdd(hugeSegment);

        // Assert
        Assert.True(accumulator.HasContent);
        Assert.True(accumulator.CurrentTokenCount > 500); // Exceeds max
    }

    [Fact]
    public void HasReachedTarget_BelowTarget_ReturnsFalse()
    {
        // Arrange
        var accumulator = new ChunkAccumulator(200, 400, 500, 0.1, SimpleTokenEstimator);
        accumulator.TryAdd(new string('x', 400)); // ~100 tokens

        // Act & Assert
        Assert.False(accumulator.HasReachedTarget);
    }

    [Fact]
    public void HasReachedTarget_AtOrAboveTarget_ReturnsTrue()
    {
        // Arrange
        var accumulator = new ChunkAccumulator(200, 400, 500, 0.1, SimpleTokenEstimator);
        accumulator.TryAdd(new string('x', 1600)); // ~400 tokens

        // Act & Assert
        Assert.True(accumulator.HasReachedTarget);
    }

    [Fact]
    public void IsNearMax_BelowMax_ReturnsFalse()
    {
        // Arrange
        var accumulator = new ChunkAccumulator(200, 400, 500, 0.1, SimpleTokenEstimator);
        accumulator.TryAdd(new string('x', 1600)); // ~400 tokens

        // Act & Assert
        Assert.False(accumulator.IsNearMax);
    }

    [Fact]
    public void IsNearMax_AtOrAboveMax_ReturnsTrue()
    {
        // Arrange
        var accumulator = new ChunkAccumulator(200, 400, 500, 0.1, SimpleTokenEstimator);
        accumulator.TryAdd(new string('x', 2000)); // ~500 tokens

        // Act & Assert
        Assert.True(accumulator.IsNearMax);
    }

    [Fact]
    public void MultipleChunksWorkflow_ProducesConsistentOverlap()
    {
        // Arrange
        var metadata = CreateTestMetadata();
        var chunks = new List<DocumentChunk>();

        // Create first accumulator
        var accumulator = new ChunkAccumulator(200, 400, 500, 0.1, SimpleTokenEstimator);

        // First chunk
        accumulator.TryAdd(new string('a', 1600)); // ~400 tokens
        var chunk1 = accumulator.Finalize("test-doc", 0, "Title", 0, metadata);
        Assert.NotNull(chunk1);
        chunks.Add(chunk1);

        // Second chunk with overlap
        accumulator.Reset(includeOverlap: true);
        var overlapCount = accumulator.CurrentTokenCount;
        accumulator.TryAdd(new string('b', 1600)); // ~400 tokens
        var chunk2 = accumulator.Finalize("test-doc", 1, "Title", chunk1.EndPosition, metadata);
        Assert.NotNull(chunk2);
        chunks.Add(chunk2);

        // Assert overlap is approximately 10% of target (40 tokens)
        Assert.InRange(overlapCount, 30, 50);

        // Third chunk with overlap
        accumulator.Reset(includeOverlap: true);
        accumulator.TryAdd(new string('c', 1600)); // ~400 tokens
        var chunk3 = accumulator.Finalize("test-doc", 2, "Title", chunk2.EndPosition, metadata);
        Assert.NotNull(chunk3);
        chunks.Add(chunk3);

        // Verify all chunks
        Assert.Equal(3, chunks.Count);
        Assert.All(chunks, c => Assert.InRange(c.TokenCount, 200, 500));
    }
}
