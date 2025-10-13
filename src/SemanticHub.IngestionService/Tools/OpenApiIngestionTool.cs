using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using Microsoft.OpenApi.Writers;
using SemanticHub.IngestionService.Models;
using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

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
        logger.LogInformation("Parsing OpenAPI spec from: {Source}", specSource);

        OpenApiDocument openApiDoc;

        try
        {
            // Determine if source is URL or file path
            if (Uri.TryCreate(specSource, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                // Load from URL
                using var httpClient = new HttpClient();
                var stream = await httpClient.GetStreamAsync(uri, cancellationToken);
                var reader = new OpenApiStreamReader();
                openApiDoc = reader.Read(stream, out var diagnostic);

                if (diagnostic.Errors.Count > 0)
                {
                    logger.LogWarning("OpenAPI parsing errors: {Errors}",
                        string.Join(", ", diagnostic.Errors.Select(e => e.Message)));
                }
            }
            else
            {
                // Load from file
                await using var stream = File.OpenRead(specSource);
                var reader = new OpenApiStreamReader();
                openApiDoc = reader.Read(stream, out var diagnostic);

                if (diagnostic.Errors.Count > 0)
                {
                    logger.LogWarning("OpenAPI parsing errors: {Errors}",
                        string.Join(", ", diagnostic.Errors.Select(e => e.Message)));
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse OpenAPI spec from: {Source}", specSource);
            throw;
        }

        // Extract endpoints
        var endpoints = ExtractEndpoints(openApiDoc, specSource);

        logger.LogInformation("Successfully parsed {Count} endpoints from OpenAPI spec", endpoints.Count);
        return endpoints;
    }

    /// <summary>
    /// Extract individual endpoints from OpenAPI document
    /// </summary>
    private static List<OpenApiEndpoint> ExtractEndpoints(OpenApiDocument doc, string source)
    {
        var endpoints = new List<OpenApiEndpoint>();
        var version = doc.Info?.Version ?? "1.0";
        var servers = doc.Servers?.Select(s => s.Url).ToList() ?? [];

        foreach (var path in doc.Paths)
        {
            foreach (var operation in path.Value.Operations)
            {
                var method = operation.Key.ToString().ToUpper();
                var op = operation.Value;

                // Merge path-level and operation-level parameters
                var allParameters = new List<OpenApiParameter>();

                // Add path-level parameters first
                if (path.Value.Parameters != null)
                {
                    allParameters.AddRange(path.Value.Parameters);
                }

                // Add operation-level parameters (these override path-level if same name)
                if (op.Parameters != null)
                {
                    foreach (var opParam in op.Parameters)
                    {
                        // Remove any path-level param with same name and location
                        allParameters.RemoveAll(p =>
                            p.Name == opParam.Name &&
                            p.In == opParam.In);
                        allParameters.Add(opParam);
                    }
                }

                var endpoint = new OpenApiEndpoint
                {
                    Id = $"{method}_{path.Key}".Replace("/", "_").Replace("{", "").Replace("}", ""),
                    Method = method,
                    Path = path.Key,
                    OperationId = op.OperationId,
                    Summary = op.Summary,
                    Description = op.Description,
                    Tags = op.Tags?.Select(t => t.Name).ToList() ?? [],
                    Version = version,
                    SourceSpec = source,
                    Servers = servers,
                    // Use merged parameters
                    Parameters = allParameters,
                    RequestBody = op.RequestBody,
                    Responses = op.Responses ?? new OpenApiResponses()
                };

                // Extract security requirements
                if (op.Security != null)
                {
                    foreach (var security in op.Security)
                    {
                        var securityNames = security.Keys
                            .Select(k => k.Reference?.Id ?? k.ToString())
                            .Where(s => !string.IsNullOrEmpty(s))
                            .Cast<string>();
                        endpoint.Security.AddRange(securityNames);
                    }
                }

                endpoints.Add(endpoint);
            }
        }

        return endpoints;
    }

    /// <summary>
    /// Convert endpoint to Markdown with YAML frontmatter
    /// </summary>
    public string ConvertEndpointToMarkdown(OpenApiEndpoint endpoint)
    {
        var sb = new StringBuilder();

        // Build YAML frontmatter
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

        // Build Markdown content
        sb.AppendLine($"# {endpoint.Method} {endpoint.Path}");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(endpoint.Summary))
        {
            sb.AppendLine($"**Summary:** {endpoint.Summary}");
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(endpoint.Description))
        {
            sb.AppendLine("## Description");
            sb.AppendLine();
            sb.AppendLine(endpoint.Description);
            sb.AppendLine();
        }

        // Servers
        if (endpoint.Servers.Any())
        {
            sb.AppendLine("## Servers");
            sb.AppendLine();
            foreach (var server in endpoint.Servers)
            {
                sb.AppendLine($"- `{server}`");
            }
            sb.AppendLine();
        }

        // Parameters
        if (endpoint.Parameters.Any())
        {
            sb.AppendLine("## Parameters");
            sb.AppendLine();
            sb.AppendLine("| Name | In  | Type | Required | Description |");
            sb.AppendLine("|------|-----|------|----------|-------------|");

            foreach (var param in endpoint.Parameters)
            {
                var req = param.Required ? "✓" : "";
                var type = param.Schema?.Type ?? "string";
                var desc = param.Description ?? "";
                var inLocation = param.In?.ToString() ?? "unknown";
                sb.AppendLine($"| `{param.Name}` | {inLocation} | {type} | {req} | {desc} |");
            }
            sb.AppendLine();
        }

        // Request Body
        if (endpoint.RequestBody != null)
        {
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

            // Get first content type
            var content = endpoint.RequestBody.Content.FirstOrDefault();
            if (content.Value != null)
            {
                sb.AppendLine($"**Content-Type:** `{content.Key}`");
                sb.AppendLine();

                if (content.Value.Schema != null)
                {
                    sb.AppendLine("### Schema");
                    sb.AppendLine();
                    sb.AppendLine("```json");
                    sb.AppendLine(SerializeSchema(content.Value.Schema));
                    sb.AppendLine("```");
                    sb.AppendLine();
                }

                if (content.Value.Example != null)
                {
                    sb.AppendLine("### Example");
                    sb.AppendLine();
                    sb.AppendLine("```json");
                    sb.AppendLine(SerializeExample(content.Value.Example));
                    sb.AppendLine("```");
                    sb.AppendLine();
                }
            }
        }

        // Responses
        if (endpoint.Responses.Any())
        {
            sb.AppendLine("## Responses");
            sb.AppendLine();

            foreach (var response in endpoint.Responses.OrderBy(r => r.Key))
            {
                sb.AppendLine($"### {response.Key} - {response.Value.Description ?? "Response"}");
                sb.AppendLine();

                var content = response.Value.Content.FirstOrDefault();
                if (content.Value != null)
                {
                    sb.AppendLine($"**Content-Type:** `{content.Key}`");
                    sb.AppendLine();

                    if (content.Value.Schema != null)
                    {
                        sb.AppendLine("#### Schema");
                        sb.AppendLine();
                        sb.AppendLine("```json");
                        sb.AppendLine(SerializeSchema(content.Value.Schema));
                        sb.AppendLine("```");
                        sb.AppendLine();
                    }

                    if (content.Value.Example != null)
                    {
                        sb.AppendLine("#### Example");
                        sb.AppendLine();
                        sb.AppendLine("```json");
                        sb.AppendLine(SerializeExample(content.Value.Example));
                        sb.AppendLine("```");
                        sb.AppendLine();
                    }
                }
            }
        }

        // Security
        if (endpoint.Security.Any())
        {
            sb.AppendLine("## Security");
            sb.AppendLine();
            foreach (var sec in endpoint.Security.Distinct())
            {
                sb.AppendLine($"- {sec}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Convert all endpoints to Markdown documents
    /// </summary>
    public List<string> ConvertEndpointsToMarkdown(List<OpenApiEndpoint> endpoints)
    {
        logger.LogInformation("Converting {Count} endpoints to Markdown", endpoints.Count);

        var markdownDocs = new List<string>();
        foreach (var endpoint in endpoints)
        {
            try
            {
                var markdown = ConvertEndpointToMarkdown(endpoint);
                markdownDocs.Add(markdown);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to convert endpoint to Markdown: {Method} {Path}",
                    endpoint.Method, endpoint.Path);
            }
        }

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
