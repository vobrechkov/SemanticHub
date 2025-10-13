using Microsoft.Extensions.Logging;
using Moq;
using SemanticHub.IngestionService.Tools;

namespace SemanticHub.Tests.Tools;

/// <summary>
/// Integration tests for OpenApiIngestionTool to verify correct parsing of real-world OpenAPI specifications
/// </summary>
public class OpenApiIngestionToolTests
{
    private readonly OpenApiIngestionTool _tool;
    private readonly Mock<ILogger<OpenApiIngestionTool>> _mockLogger;

    public OpenApiIngestionToolTests()
    {
        _mockLogger = new Mock<ILogger<OpenApiIngestionTool>>();
        _tool = new OpenApiIngestionTool(_mockLogger.Object);
    }

    [Fact]
    public async Task ParseOpenApiSpec_WealthCareApi_ParsesSuccessfully()
    {
        // Arrange
        var specPath = Path.Combine(
            GetRepositoryRoot(),
            "docs",
            "wealthcare-participant-integration-rest-api-29.0.yaml"
        );

        Assert.True(File.Exists(specPath), $"OpenAPI spec file not found at: {specPath}");

        // Act
        var endpoints = await _tool.ParseOpenApiSpecAsync(specPath, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(endpoints);
        Assert.NotEmpty(endpoints);

        // Verify we got a reasonable number of endpoints (WealthCare has ~200+ endpoints)
        Assert.True(endpoints.Count > 100, $"Expected >100 endpoints, got {endpoints.Count}");

        // Log summary
        Console.WriteLine($"✅ Successfully parsed {endpoints.Count} endpoints from WealthCare OpenAPI spec");
    }

    [Fact]
    public async Task ParseOpenApiSpec_WealthCareApi_HealthCheckEndpoint_HasCorrectStructure()
    {
        // Arrange
        var specPath = Path.Combine(
            GetRepositoryRoot(),
            "docs",
            "wealthcare-participant-integration-rest-api-29.0.yaml"
        );

        // Act
        var endpoints = await _tool.ParseOpenApiSpecAsync(specPath, TestContext.Current.CancellationToken);
        var healthCheckEndpoint = endpoints.FirstOrDefault(e => e.Path == "/healthz" && e.Method == "GET");

        // Assert
        Assert.NotNull(healthCheckEndpoint);
        Assert.Equal("HealthCheck", healthCheckEndpoint.OperationId);
        Assert.Equal("Health Check", healthCheckEndpoint.Summary);
        Assert.Contains("HealthCheck", healthCheckEndpoint.Tags);
        Assert.NotEmpty(healthCheckEndpoint.Servers);
        Assert.Equal("29.0", healthCheckEndpoint.Version);

        // Verify responses exist
        Assert.NotEmpty(healthCheckEndpoint.Responses);
        Assert.Contains("200", healthCheckEndpoint.Responses.Keys);

        Console.WriteLine($"✅ /healthz endpoint structure validated");
    }

    [Fact]
    public async Task ParseOpenApiSpec_WealthCareApi_PathParametersResolved()
    {
        // Arrange
        var specPath = Path.Combine(
            GetRepositoryRoot(),
            "docs",
            "wealthcare-participant-integration-rest-api-29.0.yaml"
        );

        // Act
        var endpoints = await _tool.ParseOpenApiSpecAsync(specPath, TestContext.Current.CancellationToken);
        var challengeEndpoint = endpoints.FirstOrDefault(e => e.Path == "/challenge/{user}" && e.Method == "POST");

        // Assert
        Assert.NotNull(challengeEndpoint);

        // Verify path parameter is resolved (from $ref)
        Assert.NotEmpty(challengeEndpoint.Parameters);
        var userParam = challengeEndpoint.Parameters.FirstOrDefault(p => p.Name == "user");
        Assert.NotNull(userParam);
        Assert.Equal(Microsoft.OpenApi.Models.ParameterLocation.Path, userParam.In);
        Assert.True(userParam.Required);

        Console.WriteLine($"✅ Path parameters resolved correctly (found {challengeEndpoint.Parameters.Count} parameters)");
    }

    [Fact]
    public async Task ParseOpenApiSpec_WealthCareApi_ComplexEndpointWithMultipleParams()
    {
        // Arrange
        var specPath = Path.Combine(
            GetRepositoryRoot(),
            "docs",
            "wealthcare-participant-integration-rest-api-29.0.yaml"
        );

        // Act
        var endpoints = await _tool.ParseOpenApiSpecAsync(specPath, TestContext.Current.CancellationToken);
        var cardEndpoint = endpoints.FirstOrDefault(e =>
            e.Path == "/services/participant/cards/{tpaId}/{employerId}/{participantId}" &&
            e.Method == "PUT");

        // Assert
        Assert.NotNull(cardEndpoint);
        Assert.Equal("ParticipantCard_UpdateCardDetails", cardEndpoint.OperationId);

        // Verify multiple path parameters resolved
        Assert.True(cardEndpoint.Parameters.Count >= 3, $"Expected ≥3 parameters, got {cardEndpoint.Parameters.Count}");
        Assert.Contains(cardEndpoint.Parameters, p => p.Name == "tpaId");
        Assert.Contains(cardEndpoint.Parameters, p => p.Name == "employerId");
        Assert.Contains(cardEndpoint.Parameters, p => p.Name == "participantId");

        // Verify request body exists
        Assert.NotNull(cardEndpoint.RequestBody);
        Assert.NotEmpty(cardEndpoint.RequestBody.Content);

        // Verify responses exist
        Assert.NotEmpty(cardEndpoint.Responses);

        Console.WriteLine($"✅ Complex endpoint with {cardEndpoint.Parameters.Count} parameters validated");
    }

    [Fact]
    public async Task ConvertEndpointToMarkdown_WealthCareApi_ProducesValidMarkdown()
    {
        // Arrange
        var specPath = Path.Combine(
            GetRepositoryRoot(),
            "docs",
            "wealthcare-participant-integration-rest-api-29.0.yaml"
        );

        // Act
        var endpoints = await _tool.ParseOpenApiSpecAsync(specPath, TestContext.Current.CancellationToken);
        var healthCheckEndpoint = endpoints.First(e => e.Path == "/healthz");
        var markdown = _tool.ConvertEndpointToMarkdown(healthCheckEndpoint);

        // Assert
        Assert.NotEmpty(markdown);

        // Verify YAML frontmatter
        Assert.StartsWith("---", markdown);
        Assert.Contains("title:", markdown);
        Assert.Contains("operationId:", markdown);
        Assert.Contains("method:", markdown);
        Assert.Contains("sourceType: openapi", markdown);

        // Verify Markdown sections
        Assert.Contains("# GET /healthz", markdown);
        Assert.Contains("## Description", markdown);
        Assert.Contains("## Servers", markdown);
        Assert.Contains("## Responses", markdown);

        Console.WriteLine($"✅ Markdown document generated successfully ({markdown.Length} chars)");
        Console.WriteLine("\n--- Sample Output (first 500 chars) ---");
        Console.WriteLine(markdown.Substring(0, Math.Min(500, markdown.Length)));
    }

    [Fact]
    public async Task ConvertEndpointToMarkdown_WealthCareApi_AllEndpoints_NoExceptions()
    {
        // Arrange
        var specPath = Path.Combine(
            GetRepositoryRoot(),
            "docs",
            "wealthcare-participant-integration-rest-api-29.0.yaml"
        );

        // Act
        var endpoints = await _tool.ParseOpenApiSpecAsync(specPath, TestContext.Current.CancellationToken);
        var markdownDocs = _tool.ConvertEndpointsToMarkdown(endpoints);

        // Assert
        Assert.Equal(endpoints.Count, markdownDocs.Count);

        // Verify all documents have content
        Assert.All(markdownDocs, md => Assert.NotEmpty(md));

        // Verify all documents have proper structure
        Assert.All(markdownDocs, md =>
        {
            Assert.StartsWith("---", md);
            Assert.Contains("sourceType: openapi", md);
        });

        Console.WriteLine($"✅ Successfully converted all {markdownDocs.Count} endpoints to Markdown");
    }

    [Fact]
    public async Task ParseOpenApiSpec_WealthCareApi_RequestBodySchemasSerialized()
    {
        // Arrange
        var specPath = Path.Combine(
            GetRepositoryRoot(),
            "docs",
            "wealthcare-participant-integration-rest-api-29.0.yaml"
        );

        // Act
        var endpoints = await _tool.ParseOpenApiSpecAsync(specPath, TestContext.Current.CancellationToken);
        var endpointWithBody = endpoints.FirstOrDefault(e => e.RequestBody != null);

        Assert.NotNull(endpointWithBody);

        var markdown = _tool.ConvertEndpointToMarkdown(endpointWithBody);

        // Assert
        Assert.Contains("## Request Body", markdown);
        Assert.Contains("### Schema", markdown);
        Assert.Contains("```json", markdown);

        Console.WriteLine($"✅ Request body schema serialized correctly for {endpointWithBody.Method} {endpointWithBody.Path}");
    }

    [Fact]
    public async Task ParseOpenApiSpec_WealthCareApi_NoSecuritySchemesHandledGracefully()
    {
        // Arrange
        var specPath = Path.Combine(
            GetRepositoryRoot(),
            "docs",
            "wealthcare-participant-integration-rest-api-29.0.yaml"
        );

        // Act
        var endpoints = await _tool.ParseOpenApiSpecAsync(specPath, TestContext.Current.CancellationToken);

        // Assert - WealthCare spec has no security schemes, should handle gracefully
        Assert.All(endpoints, endpoint =>
        {
            Assert.NotNull(endpoint.Security);
            // Security list may be empty, which is valid
        });

        Console.WriteLine($"✅ Handled missing security schemes gracefully for all {endpoints.Count} endpoints");
    }

    /// <summary>
    /// Get the repository root directory by walking up from the test assembly location
    /// </summary>
    private static string GetRepositoryRoot()
    {
        var assemblyLocation = typeof(OpenApiIngestionToolTests).Assembly.Location;
        var directory = Path.GetDirectoryName(assemblyLocation);

        // Walk up until we find the directory containing 'docs' folder
        while (directory != null && !Directory.Exists(Path.Combine(directory, "docs")))
        {
            directory = Path.GetDirectoryName(directory);
        }

        if (directory == null)
        {
            throw new InvalidOperationException("Could not find repository root (looking for 'docs' folder)");
        }

        return directory;
    }
}
