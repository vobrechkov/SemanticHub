using System.Text;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using SemanticHub.IngestionService.Domain.OpenApi;
using SemanticHub.IngestionService.Domain.Ports;
using SemanticHub.IngestionService.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SemanticHub.IngestionService.Services.OpenApi;

/// <summary>
/// Converts OpenAPI endpoints into rich Markdown documents suitable for ingestion.
/// </summary>
public sealed class OpenApiMarkdownGenerator(ILogger<OpenApiMarkdownGenerator> logger) : IOpenApiMarkdownGenerator
{
    private readonly ISerializer _yamlSerializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public string Generate(OpenApiSpecificationDocument specification, OpenApiEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(endpoint);

        var sb = new StringBuilder();

        AppendFrontmatter(sb, specification, endpoint);
        AppendHeading(sb, endpoint);
        AppendSummarySection(sb, endpoint);
        AppendDescriptionSection(sb, endpoint);
        AppendServersSection(sb, endpoint);
        AppendParametersSection(sb, endpoint);
        AppendRequestBodySection(sb, endpoint);
        AppendResponsesSection(sb, endpoint);
        AppendSecuritySection(sb, endpoint);

        var markdown = sb.ToString();
        logger.LogDebug(
            "Generated Markdown for {Method} {Path} ({Length} characters)",
            endpoint.Method,
            endpoint.Path,
            markdown.Length);

        return markdown;
    }

    private void AppendFrontmatter(
        StringBuilder sb,
        OpenApiSpecificationDocument specification,
        OpenApiEndpoint endpoint)
    {
        var frontmatter = new Dictionary<string, object>
        {
            ["title"] = $"{endpoint.Method} {endpoint.Path}",
            ["operationId"] = endpoint.OperationId ?? endpoint.Id,
            ["method"] = endpoint.Method,
            ["path"] = endpoint.Path,
            ["sourceType"] = "openapi",
            ["tags"] = endpoint.Tags,
            ["version"] = endpoint.Version ?? specification.Version,
            ["source"] = endpoint.SourceSpec ?? specification.Source
        };

        if (!string.IsNullOrWhiteSpace(specification.Title))
        {
            frontmatter["specTitle"] = specification.Title;
        }

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
            var type = parameter.Schema?.Type.ToString() ?? "string";
            var description = parameter.Description ?? string.Empty;
            var location = parameter.In?.ToString() ?? "unknown";
            sb.AppendLine($"| `{parameter.Name}` | {location} | {type} | {required} | {description} |");
        }

        sb.AppendLine();
    }

    private static void AppendRequestBodySection(StringBuilder sb, OpenApiEndpoint endpoint)
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

        if (endpoint.RequestBody.Content is { Count: > 0 } bodyContent)
        {
            foreach (var content in bodyContent)
            {
                sb.AppendLine($"**Content-Type:** `{content.Key}`");
                sb.AppendLine();

                AppendSchemaBlock(sb, content.Value.Schema, "###");
                AppendExampleBlock(sb, content.Value.Example, "###");
            }
        }
    }

    private static void AppendResponsesSection(StringBuilder sb, OpenApiEndpoint endpoint)
    {
        if (!endpoint.Responses.Any())
        {
            return;
        }

        sb.AppendLine("## Responses");
        sb.AppendLine();

        foreach (var response in endpoint.Responses.OrderBy(r => r.Key, StringComparer.Ordinal))
        {
            sb.AppendLine($"### {response.Key} - {response.Value.Description ?? "Response"}");
            sb.AppendLine();

            if (response.Value.Content is { Count: > 0 } responseContent)
            {
                foreach (var content in responseContent)
                {
                    sb.AppendLine($"**Content-Type:** `{content.Key}`");
                    sb.AppendLine();

                    AppendSchemaBlock(sb, content.Value.Schema, "####");
                    AppendExampleBlock(sb, content.Value.Example, "####");
                }
            }
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
        foreach (var requirement in endpoint.Security.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"- {requirement}");
        }

        sb.AppendLine();
    }

    private static void AppendSchemaBlock(StringBuilder sb, IOpenApiSchema? schema, string headingPrefix)
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

    private static void AppendExampleBlock(StringBuilder sb, JsonNode? example, string headingPrefix)
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

    private static string SerializeSchema(IOpenApiSchema schema)
    {
        try
        {
            using var stringWriter = new StringWriter();
            var writer = new OpenApiJsonWriter(stringWriter);
            schema.SerializeAsV3(writer);
            return stringWriter.ToString();
        }
        catch (Exception)
        {
            return "{}";
        }
    }

    private static string SerializeExample(JsonNode example)
    {
        try
        {
            return example.ToJsonString(new() { WriteIndented = true });
        }
        catch (Exception)
        {
            return "{}";
        }
    }
}
