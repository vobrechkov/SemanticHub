using System.Text;
using SemanticHub.IngestionService.Models;

namespace SemanticHub.IngestionService.Services;

/// <summary>
/// Accumulates content for building chunks with guaranteed size constraints and overlap management.
/// Ensures chunks are between min/max bounds and manages overlap buffer for context continuity.
/// </summary>
public class ChunkAccumulator
{
    private readonly int _minTokenCount;
    private readonly int _targetTokenCount;
    private readonly int _maxTokenCount;
    private readonly double _overlapPercentage;
    private readonly Func<string, int> _tokenEstimator;

    private readonly StringBuilder _currentContent;
    private readonly List<string> _contentSegments;
    private string _overlapBuffer;

    /// <summary>
    /// Gets the current token count of accumulated content
    /// </summary>
    public int CurrentTokenCount { get; private set; }

    /// <summary>
    /// Gets whether the accumulator has any content
    /// </summary>
    public bool HasContent => _currentContent.Length > 0;

    /// <summary>
    /// Gets whether the accumulator has reached the target size
    /// </summary>
    public bool HasReachedTarget => CurrentTokenCount >= _targetTokenCount;

    /// <summary>
    /// Gets whether the accumulator is at or near the max limit
    /// </summary>
    public bool IsNearMax => CurrentTokenCount >= _maxTokenCount;

    public ChunkAccumulator(
        int minTokenCount,
        int targetTokenCount,
        int maxTokenCount,
        double overlapPercentage,
        Func<string, int> tokenEstimator,
        string? initialOverlap = null)
    {
        if (minTokenCount <= 0)
            throw new ArgumentException("Min token count must be positive", nameof(minTokenCount));
        if (targetTokenCount <= minTokenCount)
            throw new ArgumentException("Target must be greater than min", nameof(targetTokenCount));
        if (maxTokenCount <= targetTokenCount)
            throw new ArgumentException("Max must be greater than target", nameof(maxTokenCount));
        if (overlapPercentage < 0 || overlapPercentage > 1)
            throw new ArgumentException("Overlap percentage must be between 0 and 1", nameof(overlapPercentage));

        _minTokenCount = minTokenCount;
        _targetTokenCount = targetTokenCount;
        _maxTokenCount = maxTokenCount;
        _overlapPercentage = overlapPercentage;
        _tokenEstimator = tokenEstimator ?? throw new ArgumentNullException(nameof(tokenEstimator));

        _currentContent = new StringBuilder();
        _contentSegments = new List<string>();
        _overlapBuffer = initialOverlap ?? string.Empty;

        // Add initial overlap if provided
        if (!string.IsNullOrEmpty(initialOverlap))
        {
            _currentContent.Append(initialOverlap);
            _contentSegments.Add(initialOverlap);
            CurrentTokenCount = _tokenEstimator(initialOverlap);
        }
    }

    /// <summary>
    /// Attempts to add a content segment to the current chunk.
    /// Returns true if added successfully, false if it would exceed max limit.
    /// </summary>
    public bool TryAdd(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
            return true; // Skip empty segments silently

        var segmentTokens = _tokenEstimator(segment);
        var potentialTotal = CurrentTokenCount + segmentTokens;

        // If adding this would exceed hard max, don't add it
        if (potentialTotal > _maxTokenCount && HasContent)
        {
            return false;
        }

        // If we're starting fresh and the segment alone exceeds max, we need to force-add it
        // (caller should handle splitting large segments before calling TryAdd)
        if (!HasContent && segmentTokens > _maxTokenCount)
        {
            return false;
        }

        // Add the segment
        if (_currentContent.Length > 0)
        {
            _currentContent.Append("\n\n");
        }
        _currentContent.Append(segment);
        _contentSegments.Add(segment);
        CurrentTokenCount = _tokenEstimator(_currentContent.ToString());

        return true;
    }

    /// <summary>
    /// Checks if a segment can fit without exceeding the max limit
    /// </summary>
    public bool CanFit(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
            return true;

        var segmentTokens = _tokenEstimator(segment);
        var potentialTotal = CurrentTokenCount + segmentTokens;

        return potentialTotal <= _maxTokenCount;
    }

    /// <summary>
    /// Finalizes the current chunk and returns it, preparing overlap buffer for the next chunk.
    /// Returns null if the chunk doesn't meet minimum requirements.
    /// </summary>
    public DocumentChunk? Finalize(
        string documentId,
        int chunkIndex,
        string? title,
        int startPosition,
        DocumentMetadata metadata)
    {
        if (!HasContent)
            return null;

        var content = _currentContent.ToString().Trim();

        // Skip if content is empty after trimming
        if (string.IsNullOrWhiteSpace(content))
            return null;

        var tokenCount = _tokenEstimator(content);

        // Create the chunk
        var chunk = new DocumentChunk
        {
            Id = $"{documentId}_chunk_{chunkIndex}",
            ParentDocumentId = documentId,
            ChunkIndex = chunkIndex,
            Title = title,
            Content = content,
            Metadata = metadata,
            TokenCount = tokenCount,
            StartPosition = startPosition,
            EndPosition = startPosition + content.Length
        };

        // Prepare overlap buffer for next chunk
        PrepareOverlapBuffer();

        return chunk;
    }

    /// <summary>
    /// Prepares the overlap buffer from the final content (takes last N% of content)
    /// </summary>
    private void PrepareOverlapBuffer()
    {
        if (!HasContent)
        {
            _overlapBuffer = string.Empty;
            return;
        }

        // Calculate target overlap in tokens
        var overlapTokenTarget = (int)(CurrentTokenCount * _overlapPercentage);
        
        if (overlapTokenTarget <= 0)
        {
            _overlapBuffer = string.Empty;
            return;
        }

        // If entire content is smaller than overlap target, use all of it
        if (CurrentTokenCount <= overlapTokenTarget)
        {
            _overlapBuffer = _currentContent.ToString().Trim();
            return;
        }

        // Accumulate segments from the end until we reach the token target
        var overlapSegments = new List<string>();
        var accumulatedTokens = 0;

        // Walk backwards through segments
        for (int i = _contentSegments.Count - 1; i >= 0; i--)
        {
            var segment = _contentSegments[i];
            var segmentTokens = _tokenEstimator(segment);

            // Check if adding this segment would exceed our overlap budget
            if (accumulatedTokens + segmentTokens > overlapTokenTarget && overlapSegments.Count > 0)
            {
                // We've accumulated enough, stop here
                break;
            }

            overlapSegments.Insert(0, segment);
            accumulatedTokens += segmentTokens;

            // If we've reached or exceeded the target, we're done
            if (accumulatedTokens >= overlapTokenTarget)
            {
                break;
            }
        }

        // Join segments with the same separator used in TryAdd
        _overlapBuffer = string.Join("\n\n", overlapSegments).Trim();
    }

    /// <summary>
    /// Gets the overlap buffer for the next chunk
    /// </summary>
    public string GetOverlapBuffer() => _overlapBuffer;

    /// <summary>
    /// Resets the accumulator for a new chunk, optionally starting with overlap from previous chunk
    /// </summary>
    public void Reset(bool includeOverlap = true)
    {
        _currentContent.Clear();
        _contentSegments.Clear();
        CurrentTokenCount = 0;

        if (includeOverlap && !string.IsNullOrEmpty(_overlapBuffer))
        {
            _currentContent.Append(_overlapBuffer);
            _contentSegments.Add(_overlapBuffer);
            CurrentTokenCount = _tokenEstimator(_overlapBuffer);
        }
    }

    /// <summary>
    /// Forces the current content to be added even if it exceeds target (used for must-include segments)
    /// </summary>
    public void ForceAdd(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
            return;

        if (_currentContent.Length > 0)
        {
            _currentContent.Append("\n\n");
        }
        _currentContent.Append(segment);
        _contentSegments.Add(segment);
        CurrentTokenCount = _tokenEstimator(_currentContent.ToString());
    }
}
