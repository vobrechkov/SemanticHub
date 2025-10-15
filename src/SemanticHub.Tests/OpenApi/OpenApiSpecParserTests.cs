using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SemanticHub.IngestionService.Domain.OpenApi;
using SemanticHub.IngestionService.Services.OpenApi;

namespace SemanticHub.Tests.OpenApi;

/// <summary>
/// Integration tests covering the OpenAPI parser and markdown generator against a real-world specification.
/// </summary>
public class OpenApiSpecParserTests
{
    private readonly OpenApiSpecParser _parser;
    private readonly OpenApiMarkdownGenerator _generator;
    private readonly Mock<ILogger<OpenApiSpecParser>> _parserLogger = new();
    private readonly Mock<ILogger<OpenApiMarkdownGenerator>> _generatorLogger = new();

    public OpenApiSpecParserTests()
    {
        _parser = new OpenApiSpecParser(_parserLogger.Object);
        _generator = new OpenApiMarkdownGenerator(_generatorLogger.Object);
    }

    [Fact]
    public async Task ParseOpenApiSpec_WealthCareApi_ParsesSuccessfully()
    {
        var specification = await LoadSpecificationAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(specification);
        Assert.NotEmpty(specification.Endpoints);
        Assert.True(specification.Endpoints.Count > 100, $"Expected >100 endpoints, got {specification.Endpoints.Count}");
    }

    [Fact]
    public async Task ParseOpenApiSpec_WealthCareApi_HealthCheckEndpoint_HasCorrectStructure()
    {
        var specification = await LoadSpecificationAsync(TestContext.Current.CancellationToken);
        var healthCheckEndpoint = specification.Endpoints.FirstOrDefault(e => e.Path == "/healthz" && e.Method == "GET");

        Assert.NotNull(healthCheckEndpoint);
        Assert.Equal("HealthCheck", healthCheckEndpoint.OperationId);
        Assert.Equal("Health Check", healthCheckEndpoint.Summary);
        Assert.Contains("HealthCheck", healthCheckEndpoint.Tags);
        Assert.NotEmpty(healthCheckEndpoint.Servers);
        Assert.Equal("29.0", healthCheckEndpoint.Version);
        Assert.NotEmpty(healthCheckEndpoint.Responses);
        Assert.Contains("200", healthCheckEndpoint.Responses.Keys);
    }

    [Fact]
    public async Task ParseOpenApiSpec_WealthCareApi_PathParametersResolved()
    {
        var specification = await LoadSpecificationAsync(TestContext.Current.CancellationToken);
        var challengeEndpoint = specification.Endpoints.FirstOrDefault(e => e.Path == "/challenge/{user}" && e.Method == "POST");

        Assert.NotNull(challengeEndpoint);
        Assert.NotEmpty(challengeEndpoint.Parameters);
        var userParam = challengeEndpoint.Parameters.FirstOrDefault(p => p.Name == "user");
        Assert.NotNull(userParam);
        Assert.Equal(Microsoft.OpenApi.Models.ParameterLocation.Path, userParam.In);
        Assert.True(userParam.Required);
    }

    [Fact]
    public async Task ParseOpenApiSpec_WealthCareApi_ComplexEndpointWithMultipleParams()
    {
        var specification = await LoadSpecificationAsync(TestContext.Current.CancellationToken);
        var cardEndpoint = specification.Endpoints.FirstOrDefault(e =>
            e.Path == "/services/participant/cards/{tpaId}/{employerId}/{participantId}" &&
            e.Method == "PUT");

        Assert.NotNull(cardEndpoint);
        Assert.Equal("ParticipantCard_UpdateCardDetails", cardEndpoint.OperationId);
        Assert.True(cardEndpoint.Parameters.Count >= 3, $"Expected ≥3 parameters, got {cardEndpoint.Parameters.Count}");
        Assert.Contains(cardEndpoint.Parameters, p => p.Name == "tpaId");
        Assert.Contains(cardEndpoint.Parameters, p => p.Name == "employerId");
        Assert.Contains(cardEndpoint.Parameters, p => p.Name == "participantId");
        Assert.NotNull(cardEndpoint.RequestBody);
        Assert.NotEmpty(cardEndpoint.RequestBody!.Content);
        Assert.NotEmpty(cardEndpoint.Responses);
    }

    [Fact]
    public async Task GenerateMarkdown_WealthCareApi_HealthCheckEndpoint()
    {
        var specification = await LoadSpecificationAsync(TestContext.Current.CancellationToken);
        var healthCheckEndpoint = specification.Endpoints.First(e => e.Path == "/healthz");

        var markdown = _generator.Generate(specification, healthCheckEndpoint);

        Assert.NotEmpty(markdown);
        Assert.StartsWith("---", markdown);
        Assert.Contains("title:", markdown);
        Assert.Contains("operationId:", markdown);
        Assert.Contains("sourceType: openapi", markdown);
        Assert.Contains("# GET /healthz", markdown);
        Assert.Contains("## Description", markdown);
        Assert.Contains("## Responses", markdown);
    }

    [Fact]
    public async Task GenerateMarkdown_WealthCareApi_AllEndpoints_NoExceptions()
    {
        var specification = await LoadSpecificationAsync(TestContext.Current.CancellationToken);
        var markdownDocs = specification.Endpoints
            .Select(endpoint => _generator.Generate(specification, endpoint))
            .ToList();

        Assert.Equal(specification.Endpoints.Count, markdownDocs.Count);
        Assert.All(markdownDocs, md =>
        {
            Assert.NotEmpty(md);
            Assert.StartsWith("---", md);
            Assert.Contains("sourceType: openapi", md);
        });
    }

    [Fact]
    public async Task ParseOpenApiSpec_WealthCareApi_RequestBodySchemasSerialized()
    {
        var specification = await LoadSpecificationAsync(TestContext.Current.CancellationToken);
        var endpointWithBody = specification.Endpoints.FirstOrDefault(e => e.RequestBody != null);

        Assert.NotNull(endpointWithBody);

        var markdown = _generator.Generate(specification, endpointWithBody!);

        Assert.Contains("## Request Body", markdown);
        Assert.Contains("### Schema", markdown);
        Assert.Contains("```json", markdown);
    }

    [Fact]
    public async Task ParseOpenApiSpec_WealthCareApi_NoSecuritySchemesHandledGracefully()
    {
        var specification = await LoadSpecificationAsync(TestContext.Current.CancellationToken);

        Assert.All(specification.Endpoints, endpoint =>
        {
            Assert.NotNull(endpoint.Security);
        });
    }

    private async Task<OpenApiSpecificationDocument> LoadSpecificationAsync(CancellationToken cancellationToken)
    {
        var specPath = Path.Combine(GetRepositoryRoot(), "docs", "wealthcare-participant-integration-rest-api-29.0.yaml");
        Assert.True(File.Exists(specPath), $"OpenAPI spec file not found at: {specPath}");

        var content = await File.ReadAllTextAsync(specPath, cancellationToken);
        var specDocument = new OpenApiSpecDocument(specPath, content, new Uri(Path.GetFullPath(specPath)));

        return await _parser.ParseAsync(specDocument, cancellationToken);
    }

    private static string GetRepositoryRoot()
    {
        var directory = Path.GetDirectoryName(typeof(OpenApiSpecParserTests).Assembly.Location);

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
