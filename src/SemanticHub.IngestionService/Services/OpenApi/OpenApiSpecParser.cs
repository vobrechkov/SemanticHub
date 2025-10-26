using System.Diagnostics;
using System.Text;
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

    public Task<OpenApiSpecificationDocument> ParseAsync(
        OpenApiSpecDocument specDocument,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specDocument);

        using var activity = IngestionTelemetry.ActivitySource.StartActivity("OpenApiSpecParser.Parse");
        activity?.SetTag("ingestion.openapi.specSource", specDocument.Source);

        try
        {
            var reader = CreateReader();
            var document = reader.Read(specDocument.Content, out var diagnostic);

            LogDiagnostics(diagnostic);

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

            return Task.FromResult(specification);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogError(ex, "Failed to parse OpenAPI specification from {Source}", specDocument.Source);
            throw;
        }
    }

    private static OpenApiStringReader CreateReader() =>
        new(new OpenApiReaderSettings
        {
            ReferenceResolution = ReferenceResolutionSetting.ResolveLocalReferences
        });

    private void LogDiagnostics(OpenApiDiagnostic diagnostic)
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
        var servers = document.Servers?.Select(s => s.Url).ToList() ?? [];

        var endpoints = new List<OpenApiEndpoint>();
        foreach (var path in document.Paths)
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
            Tags = operation.Value.Tags?.Select(t => t.Name).Where(t => !string.IsNullOrWhiteSpace(t)).ToList() ?? [],
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
}
