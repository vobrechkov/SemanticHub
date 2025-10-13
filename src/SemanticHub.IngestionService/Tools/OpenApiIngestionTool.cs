using System.Diagnostics;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using Microsoft.OpenApi.Writers;
using SemanticHub.IngestionService.Models;
using System.Text;
using System.IO;
using System.Net.Http;
using System.Collections.Generic;
using System.Linq;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using SemanticHub.IngestionService.Diagnostics;

namespace SemanticHub.IngestionService.Tools;

/// <summary>
/// Tool for parsing OpenAPI specifications and converting endpoints to Markdown documents
/// </summary>
public class OpenApiIngestionTool(ILogger<OpenApiIngestionTool> logger)
{
    private readonly ISerializer _yamlSerializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    /// <summary>
    /// Parse OpenAPI specification from URL or file path
    /// </summary>
    public async Task<List<OpenApiEndpoint>> ParseOpenApiSpecAsync(
        string specSource,
        CancellationToken cancellationToken = default)
    {
        using var activity = IngestionTelemetry.ActivitySource.StartActivity("ParseOpenApiSpec");
        activity?.SetTag("ingestion.openapi.specSource", specSource);

        logger.LogInformation("Parsing OpenAPI spec from: {Source}", specSource);

        try
        {
            var openApiDoc = await LoadOpenApiDocumentAsync(specSource, cancellationToken);
            var endpoints = ExtractEndpoints(openApiDoc, specSource);

            activity?.SetTag("ingestion.openapi.endpointCount", endpoints.Count);
            activity?.SetStatus(ActivityStatusCode.Ok);

            logger.LogInformation("Successfully parsed {Count} endpoints from OpenAPI spec", endpoints.Count);
            return endpoints;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogError(ex, "Failed to parse OpenAPI spec from: {Source}", specSource);
            throw;
        }
    }

    private async Task<OpenApiDocument> LoadOpenApiDocumentAsync(
        string specSource,
        CancellationToken cancellationToken)
    {
        if (TryCreateHttpUri(specSource, out var uri) && uri != null)
        {
            using var httpClient = new HttpClient();
            await using var stream = await httpClient.GetStreamAsync(uri, cancellationToken);
            return ReadOpenApiDocument(stream);
        }

        await using var fileStream = File.OpenRead(specSource);
        return ReadOpenApiDocument(fileStream);
    }

    private static bool TryCreateHttpUri(string specSource, out Uri? uri)
    {
        if (Uri.TryCreate(specSource, UriKind.Absolute, out var created) &&
            (created.Scheme == Uri.UriSchemeHttp || created.Scheme == Uri.UriSchemeHttps))
        {
            uri = created;
            return true;
        }

        uri = null;
        return false;
    }

    private OpenApiDocument ReadOpenApiDocument(Stream stream)
    {
        var reader = new OpenApiStreamReader();
        var document = reader.Read(stream, out var diagnostic);
        LogParsingDiagnostics(diagnostic);
        return document;
    }

    private void LogParsingDiagnostics(OpenApiDiagnostic diagnostic)
    {
        if (diagnostic.Errors.Count == 0)
        {
            return;
        }

        logger.LogWarning(
            "OpenAPI parsing issues: {Errors}",
            string.Join(", ", diagnostic.Errors.Select(e => e.Message)));
    }

    /// <summary>
    /// Extract individual endpoints from OpenAPI document
    /// </summary>
    private static List<OpenApiEndpoint> ExtractEndpoints(OpenApiDocument doc, string source)
    {
        var version = doc.Info?.Version ?? "1.0";
        var servers = doc.Servers?.Select(s => s.Url).ToList() ?? [];

        var endpoints = new List<OpenApiEndpoint>();
        foreach (var path in doc.Paths)
        {
            endpoints.AddRange(CreateEndpointsForPath(source, version, servers, path.Key, path.Value));
        }

        return endpoints;
    }

    private static IEnumerable<OpenApiEndpoint> CreateEndpointsForPath(
        string source,
        string version,
        List<string> servers,
        string pathKey,
        OpenApiPathItem pathItem)
    {
        foreach (var operation in pathItem.Operations)
        {
            yield return CreateEndpoint(source, version, servers, pathKey, pathItem, operation);
        }
    }

    private static OpenApiEndpoint CreateEndpoint(
        string source,
        string version,
        List<string> servers,
        string pathKey,
        OpenApiPathItem pathItem,
        KeyValuePair<OperationType, OpenApiOperation> operation)
    {
        var method = operation.Key.ToString().ToUpperInvariant();
        var mergedParameters = MergeParameters(pathItem.Parameters, operation.Value.Parameters);

        var endpoint = new OpenApiEndpoint
        {
            Id = BuildEndpointId(method, pathKey),
            Method = method,
            Path = pathKey,
            OperationId = operation.Value.OperationId,
            Summary = operation.Value.Summary,
            Description = operation.Value.Description,
            Tags = operation.Value.Tags?.Select(t => t.Name).ToList() ?? [],
            Version = version,
            SourceSpec = source,
            Servers = new List<string>(servers),
            Parameters = mergedParameters,
            RequestBody = operation.Value.RequestBody,
            Responses = operation.Value.Responses ?? []
        };

        AddSecurityRequirements(operation.Value, endpoint);
        return endpoint;
    }

    private static string BuildEndpointId(string method, string pathKey)
    {
        return $"{method}_{pathKey}"
            .Replace("/", "_", StringComparison.Ordinal)
            .Replace("{", string.Empty, StringComparison.Ordinal)
            .Replace("}", string.Empty, StringComparison.Ordinal);
    }

    private static List<OpenApiParameter> MergeParameters(
        IList<OpenApiParameter>? pathParameters,
        IList<OpenApiParameter>? operationParameters)
    {
        var merged = new List<OpenApiParameter>();

        if (pathParameters != null)
        {
            merged.AddRange(pathParameters);
        }

        if (operationParameters == null)
        {
            return merged;
        }

        foreach (var parameter in operationParameters)
        {
            merged.RemoveAll(p => p.Name == parameter.Name && p.In == parameter.In);
            merged.Add(parameter);
        }

        return merged;
    }

    private static void AddSecurityRequirements(OpenApiOperation operation, OpenApiEndpoint endpoint)
    {
        if (operation.Security == null)
        {
            return;
        }

        foreach (var security in operation.Security)
        {
            foreach (var requirement in security.Keys)
            {
                var name = requirement.Reference?.Id ?? requirement.ToString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    endpoint.Security.Add(name);
                }
            }
        }
    }

    /// <summary>
    /// Convert endpoint to Markdown with YAML frontmatter
    /// </summary>
    public string ConvertEndpointToMarkdown(OpenApiEndpoint endpoint)
    {
        var sb = new StringBuilder();

        AppendFrontmatter(sb, endpoint);
        AppendHeading(sb, endpoint);
        AppendSummarySection(sb, endpoint);
        AppendDescriptionSection(sb, endpoint);
        AppendServersSection(sb, endpoint);
        AppendParametersSection(sb, endpoint);
        AppendRequestBodySection(sb, endpoint);
        AppendResponsesSection(sb, endpoint);
        AppendSecuritySection(sb, endpoint);

        return sb.ToString();
    }

    private void AppendFrontmatter(StringBuilder sb, OpenApiEndpoint endpoint)
    {
        var frontmatter = new Dictionary<string, object>
        {
            ["title"] = $"{endpoint.Method} {endpoint.Path}",
            ["operationId"] = endpoint.OperationId ?? endpoint.Id,
            ["method"] = endpoint.Method,
            ["path"] = endpoint.Path,
            ["sourceType"] = "openapi",
            ["tags"] = endpoint.Tags,
            ["version"] = endpoint.Version ?? "unknown",
            ["source"] = endpoint.SourceSpec ?? "unknown"
        };

        if (endpoint.Security.Any())
        {
            frontmatter["security"] = endpoint.Security;
        }

        sb.AppendLine("---");
        sb.AppendLine(_yamlSerializer.Serialize(frontmatter).TrimEnd());
        sb.AppendLine("---");
        sb.AppendLine();
    }

    private static void AppendHeading(StringBuilder sb, OpenApiEndpoint endpoint)
    {
        sb.AppendLine($"# {endpoint.Method} {endpoint.Path}");
        sb.AppendLine();
    }

    private static void AppendSummarySection(StringBuilder sb, OpenApiEndpoint endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint.Summary))
        {
            return;
        }

        sb.AppendLine($"**Summary:** {endpoint.Summary}");
        sb.AppendLine();
    }

    private static void AppendDescriptionSection(StringBuilder sb, OpenApiEndpoint endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint.Description))
        {
            return;
        }

        sb.AppendLine("## Description");
        sb.AppendLine();
        sb.AppendLine(endpoint.Description);
        sb.AppendLine();
    }

    private static void AppendServersSection(StringBuilder sb, OpenApiEndpoint endpoint)
    {
        if (!endpoint.Servers.Any())
        {
            return;
        }

        sb.AppendLine("## Servers");
        sb.AppendLine();
        foreach (var server in endpoint.Servers)
        {
            sb.AppendLine($"- `{server}`");
        }
        sb.AppendLine();
    }

    private static void AppendParametersSection(StringBuilder sb, OpenApiEndpoint endpoint)
    {
        if (!endpoint.Parameters.Any())
        {
            return;
        }

        sb.AppendLine("## Parameters");
        sb.AppendLine();
        sb.AppendLine("| Name | In  | Type | Required | Description |");
        sb.AppendLine("|------|-----|------|----------|-------------|");

        foreach (var parameter in endpoint.Parameters)
        {
            var required = parameter.Required ? "✓" : string.Empty;
            var type = parameter.Schema?.Type ?? "string";
            var description = parameter.Description ?? string.Empty;
            var location = parameter.In?.ToString() ?? "unknown";
            sb.AppendLine($"| `{parameter.Name}` | {location} | {type} | {required} | {description} |");
        }

        sb.AppendLine();
    }

    private void AppendRequestBodySection(StringBuilder sb, OpenApiEndpoint endpoint)
    {
        if (endpoint.RequestBody == null)
        {
            return;
        }

        sb.AppendLine("## Request Body");
        sb.AppendLine();

        if (endpoint.RequestBody.Required)
        {
            sb.AppendLine("**Required:** Yes");
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(endpoint.RequestBody.Description))
        {
            sb.AppendLine(endpoint.RequestBody.Description);
            sb.AppendLine();
        }

        var content = endpoint.RequestBody.Content.FirstOrDefault();
        if (content.Value == null)
        {
            return;
        }

        sb.AppendLine($"**Content-Type:** `{content.Key}`");
        sb.AppendLine();

        AppendSchemaBlock(sb, content.Value.Schema, "###");
        AppendExampleBlock(sb, content.Value.Example, "###");
    }

    private void AppendResponsesSection(StringBuilder sb, OpenApiEndpoint endpoint)
    {
        if (!endpoint.Responses.Any())
        {
            return;
        }

        sb.AppendLine("## Responses");
        sb.AppendLine();

        foreach (var response in endpoint.Responses.OrderBy(r => r.Key))
        {
            sb.AppendLine($"### {response.Key} - {response.Value.Description ?? "Response"}");
            sb.AppendLine();

            var content = response.Value.Content.FirstOrDefault();
            if (content.Value == null)
            {
                continue;
            }

            sb.AppendLine($"**Content-Type:** `{content.Key}`");
            sb.AppendLine();

            AppendSchemaBlock(sb, content.Value.Schema, "####");
            AppendExampleBlock(sb, content.Value.Example, "####");
        }
    }

    private static void AppendSecuritySection(StringBuilder sb, OpenApiEndpoint endpoint)
    {
        if (!endpoint.Security.Any())
        {
            return;
        }

        sb.AppendLine("## Security");
        sb.AppendLine();
        foreach (var requirement in endpoint.Security.Distinct())
        {
            sb.AppendLine($"- {requirement}");
        }
        sb.AppendLine();
    }

    private void AppendSchemaBlock(StringBuilder sb, OpenApiSchema? schema, string headingPrefix)
    {
        if (schema == null)
        {
            return;
        }

        sb.AppendLine($"{headingPrefix} Schema");
        sb.AppendLine();
        sb.AppendLine("```json");
        sb.AppendLine(SerializeSchema(schema));
        sb.AppendLine("```");
        sb.AppendLine();
    }

    private void AppendExampleBlock(StringBuilder sb, IOpenApiAny? example, string headingPrefix)
    {
        if (example == null)
        {
            return;
        }

        sb.AppendLine($"{headingPrefix} Example");
        sb.AppendLine();
        sb.AppendLine("```json");
        sb.AppendLine(SerializeExample(example));
        sb.AppendLine("```");
        sb.AppendLine();
    }

    /// <summary>
    /// Convert all endpoints to Markdown documents
    /// </summary>
    public List<string> ConvertEndpointsToMarkdown(List<OpenApiEndpoint> endpoints)
    {
        using var activity = IngestionTelemetry.ActivitySource.StartActivity("ConvertOpenApiEndpoints");
        activity?.SetTag("ingestion.openapi.endpointCount", endpoints.Count);

        logger.LogInformation("Converting {Count} endpoints to Markdown", endpoints.Count);

        var markdownDocs = new List<string>();
        foreach (var endpoint in endpoints)
        {
            try
            {
                var markdown = ConvertEndpointToMarkdown(endpoint);
                markdownDocs.Add(markdown);
                activity?.AddEvent(new ActivityEvent("EndpointConverted", tags: new ActivityTagsCollection
                {
                    { "method", endpoint.Method },
                    { "path", endpoint.Path }
                }));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to convert endpoint to Markdown: {Method} {Path}",
                    endpoint.Method, endpoint.Path);
            }
        }

        activity?.SetStatus(ActivityStatusCode.Ok);
        return markdownDocs;
    }

    /// <summary>
    /// Serialize OpenAPI schema to JSON string using built-in OpenAPI writer
    /// </summary>
    private string SerializeSchema(OpenApiSchema schema)
    {
        try
        {
            using var stringWriter = new StringWriter();
            var writer = new OpenApiJsonWriter(stringWriter);
            schema.SerializeAsV3(writer);
            return stringWriter.ToString();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to serialize schema");
            return "{}";
        }
    }

    /// <summary>
    /// Serialize example to JSON string
    /// </summary>
    private string SerializeExample(IOpenApiAny example)
    {
        try
        {
            using var stringWriter = new StringWriter();
            var writer = new OpenApiJsonWriter(stringWriter);
            example.Write(writer, Microsoft.OpenApi.OpenApiSpecVersion.OpenApi3_0);
            return stringWriter.ToString();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to serialize example");
            return "{}";
        }
    }
}
