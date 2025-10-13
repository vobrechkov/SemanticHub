using Microsoft.Extensions.Logging;
using Moq;
using SemanticHub.Api.Configuration;
using SemanticHub.Api.Memory;
using SemanticHub.Api.Tools;

namespace SemanticHub.Tests.Tools;

/// <summary>
/// Unit tests for KnowledgeBaseTools.
/// </summary>
public class KnowledgeBaseToolsTests
{
    private readonly AgentFrameworkOptions _options;
    private readonly Mock<IAzureSearchKnowledgeStore> _mockKnowledgeStore;
    private readonly Mock<ILogger<KnowledgeBaseTools>> _mockLogger;

    public KnowledgeBaseToolsTests()
    {
        _options = new AgentFrameworkOptions
        {
            Memory =
            {
                AzureSearch =
                {
                    MaxResults = 5,
                    MinRelevance = 0.6
                }
            }
        };

        _mockKnowledgeStore = new Mock<IAzureSearchKnowledgeStore>();
        _mockLogger = new Mock<ILogger<KnowledgeBaseTools>>();
    }

    [Fact]
    public async Task SearchKnowledgeBase_ValidQuery_ReturnsFormattedResults()
    {
        // Arrange
        var records = new List<KnowledgeRecord>
        {
            new(
                new KnowledgeDocument("doc-1", "Test result 1", "Summary 1"),
                "Content for result 1",
                0.95,
                0.95,
                new Dictionary<string, object?>()),
            new(
                new KnowledgeDocument("doc-2", "Test result 2", "Summary 2"),
                "Content for result 2",
                0.85,
                0.85,
                new Dictionary<string, object?>())
        };

        _mockKnowledgeStore
            .Setup(store => store.SearchAsync("test query", 5, 0.6, It.IsAny<CancellationToken>()))
            .ReturnsAsync(records);

        var tools = new KnowledgeBaseTools(_mockLogger.Object, _mockKnowledgeStore.Object, _options);

        // Act
        var result = await tools.SearchKnowledgeBase("test query", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("Found 2 relevant result(s)", result);
        Assert.Contains("Test result 1", result);
        Assert.Contains("Test result 2", result);
        Assert.Contains("doc-1", result);
        Assert.Contains("doc-2", result);
    }

    [Fact]
    public async Task SearchKnowledgeBase_NoResults_ReturnsFriendlyMessage()
    {
        // Arrange
        _mockKnowledgeStore
            .Setup(store => store.SearchAsync("missing", 5, 0.6, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeRecord>());

        var tools = new KnowledgeBaseTools(_mockLogger.Object, _mockKnowledgeStore.Object, _options);

        // Act
        var result = await tools.SearchKnowledgeBase("missing", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("No relevant information found", result);
    }

    [Fact]
    public async Task GetDocumentStatus_WhenDocumentFound_ReturnsIndexedMessage()
    {
        // Arrange
        var document = new KnowledgeDocument("doc-123", "Test Document", "Summary");
        _mockKnowledgeStore
            .Setup(store => store.TryGetDocumentAsync("doc-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);

        var tools = new KnowledgeBaseTools(_mockLogger.Object, _mockKnowledgeStore.Object, _options);

        // Act
        var result = await tools.GetDocumentStatus("doc-123", TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("Document doc-123 status: indexed.", result);
        Assert.Contains("Test Document", result);
    }

    [Fact]
    public async Task GetDocumentStatus_WhenMissing_ReturnsNotFoundMessage()
    {
        // Arrange
        _mockKnowledgeStore
            .Setup(store => store.TryGetDocumentAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((KnowledgeDocument?)null);

        var tools = new KnowledgeBaseTools(_mockLogger.Object, _mockKnowledgeStore.Object, _options);

        // Act
        var result = await tools.GetDocumentStatus("missing", TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("not found in index", result);
    }

    [Fact]
    public async Task ListDocuments_ReturnsFormattedList()
    {
        // Arrange
        var documents = new List<KnowledgeDocument>
        {
            new("doc-1", "Document 1", "Summary 1"),
            new("doc-2", "Document 2", null)
        };

        _mockKnowledgeStore
            .Setup(store => store.ListDocumentsAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(documents);

        var tools = new KnowledgeBaseTools(_mockLogger.Object, _mockKnowledgeStore.Object, _options);

        // Act
        var result = await tools.ListDocuments(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("Found 2 document(s)", result);
        Assert.Contains("doc-1: Document 1", result);
        Assert.Contains("doc-2: Document 2", result);
    }
}
