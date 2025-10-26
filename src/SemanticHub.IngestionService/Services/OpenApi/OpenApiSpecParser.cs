using System.Diagnostics;
using System.Text;
using Microsoft.OpenApi;
using SemanticHub.IngestionService.Diagnostics;
using SemanticHub.IngestionService.Domain.OpenApi;
using SemanticHub.IngestionService.Domain.Ports;
using SemanticHub.IngestionService.Models;

namespace SemanticHub.IngestionService.Services.OpenApi;

/// <summary>
/// Parses OpenAPI specifications into normalized endpoint representations.
/// </summary>
public sealed class OpenApiSpecParser(ILogger<OpenApiSpecParser> logger) : IOpenApiSpecParser
{
    public bool LooksLikeSpecification(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        return content.Contains("openapi", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("swagger", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<OpenApiSpecificationDocument> ParseAsync(
        OpenApiSpecDocument specDocument,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specDocument);

        using var activity = IngestionTelemetry.ActivitySource.StartActivity("OpenApiSpecParser.Parse");
        activity?.SetTag("ingestion.openapi.specSource", specDocument.Source);

        try
        {
            // In OpenAPI.NET v2, use OpenApiDocument.Parse() for string content
            // The Parse method returns a ReadResult which can be deconstructed
            var (document, diagnostic) = OpenApiDocument.Parse(specDocument.Content, format: OpenApiConstants.Json);
            
            if (document == null)
            {
                throw new InvalidOperationException($"Failed to parse OpenAPI document from {specDocument.Source}");
            }

            if (diagnostic != null)
            {
                LogDiagnostics(diagnostic);
            }

            var endpoints = ExtractEndpoints(document, specDocument.Source);
            activity?.SetTag("ingestion.openapi.endpointCount", endpoints.Count);
            activity?.SetStatus(ActivityStatusCode.Ok);

            logger.LogInformation(
                "Parsed OpenAPI specification '{Title}' (v{Version}) with {EndpointCount} endpoints.",
                document.Info?.Title ?? specDocument.Source,
                document.Info?.Version ?? "1.0",
                endpoints.Count);

            var specification = new OpenApiSpecificationDocument(
                document.Info?.Title ?? specDocument.Source,
                document.Info?.Version ?? "1.0",
                specDocument.Source,
                endpoints);

            return await Task.FromResult(specification);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogError(ex, "Failed to parse OpenAPI specification from {Source}", specDocument.Source);
            throw;
        }
    }

    private void LogDiagnostics(Microsoft.OpenApi.Reader.OpenApiDiagnostic diagnostic)
    {
        if (diagnostic.Errors.Count > 0)
        {
            logger.LogWarning(
                "OpenAPI parsing issues: {Errors}",
                string.Join(", ", diagnostic.Errors.Select(e => e.Message)));
        }

        if (diagnostic.Warnings.Count > 0)
        {
            logger.LogDebug(
                "OpenAPI parsing warnings: {Warnings}",
                string.Join(", ", diagnostic.Warnings.Select(w => w.Message)));
        }
    }

    private static List<OpenApiEndpoint> ExtractEndpoints(OpenApiDocument document, string source)
    {
        var version = document.Info?.Version ?? "1.0";
        var servers = document.Servers?.Select(s => s.Url).Where(url => url != null).ToList() ?? [];

        var endpoints = new List<OpenApiEndpoint>();
        foreach (var path in document.Paths)
        {
            endpoints.AddRange(CreateEndpointsForPath(source, version, servers!, path.Key, path.Value));
        }

        return endpoints;
    }

    private static IEnumerable<OpenApiEndpoint> CreateEndpointsForPath(
        string source,
        string version,
        List<string> servers,
        string pathKey,
        IOpenApiPathItem pathItem)
    {
        // Cast IOpenApiPathItem to OpenApiPathItem to access Operations
        if (pathItem is not OpenApiPathItem concretePathItem || concretePathItem.Operations == null)
        {
            yield break;
        }

        foreach (var operation in concretePathItem.Operations)
        {
            yield return CreateEndpoint(source, version, servers, pathKey, concretePathItem, operation);
        }
    }

    private static OpenApiEndpoint CreateEndpoint(
        string source,
        string version,
        List<string> servers,
        string pathKey,
        OpenApiPathItem pathItem,
        KeyValuePair<HttpMethod, OpenApiOperation> operation)
    {
        // In v2, HttpMethod is used instead of OperationType enum
        var method = operation.Key.Method; // Get the string representation
        var mergedParameters = MergeParameters(pathItem.Parameters, operation.Value.Parameters);

        var endpoint = new OpenApiEndpoint
        {
            Id = BuildEndpointId(method, pathKey),
            Method = method,
            Path = pathKey,
            OperationId = operation.Value.OperationId,
            Summary = operation.Value.Summary,
            Description = operation.Value.Description,
            Tags = operation.Value.Tags?
                .Select(t => t.Name)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Cast<string>() // Ensure non-nullable strings
                .ToList() ?? [],
            Version = version,
            SourceSpec = source,
            Servers = new List<string>(servers),
            Parameters = mergedParameters,
            // In v2, RequestBody is IOpenApiRequestBody, cast to OpenApiRequestBody
            RequestBody = operation.Value.RequestBody as OpenApiRequestBody,
            Responses = operation.Value.Responses ?? []
        };

        AddSecurityRequirements(operation.Value, endpoint);
        return endpoint;
    }

    private static string BuildEndpointId(string method, string pathKey)
    {
        var normalized = $"{method}_{pathKey}";
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            builder.Append(character switch
            {
                '/' => '_',
                '{' => '_',
                '}' => '_',
                '-' => '-',
                _ => character
            });
        }

        return builder.ToString().Trim('_');
    }

    private static List<OpenApiParameter> MergeParameters(
        IList<IOpenApiParameter>? pathParameters,
        IList<IOpenApiParameter>? operationParameters)
    {
        var merged = new List<OpenApiParameter>();

        if (pathParameters != null)
        {
            // Cast IOpenApiParameter to OpenApiParameter
            foreach (var param in pathParameters)
            {
                if (param is OpenApiParameter concreteParam)
                {
                    merged.Add(concreteParam);
                }
            }
        }

        if (operationParameters == null)
        {
            return merged;
        }

        foreach (var parameter in operationParameters)
        {
            if (parameter is not OpenApiParameter concreteParameter)
            {
                continue;
            }
            
            merged.RemoveAll(p => p.Name == concreteParameter.Name && p.In == concreteParameter.In);
            merged.Add(concreteParameter);
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
}
