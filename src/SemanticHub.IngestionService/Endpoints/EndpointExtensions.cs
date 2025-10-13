using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace SemanticHub.IngestionService.Endpoints;

/// <summary>
/// Extensions for registering and mapping endpoint definitions.
/// </summary>
public static class EndpointExtensions
{
    public static IServiceCollection AddIngestionEndpoints(this IServiceCollection services)
    {
        services.AddSingleton<IEndpoint, IngestionEndpoints>();
        return services;
    }

    public static WebApplication MapRegisteredIngestionEndpoints(this WebApplication app)
    {
        var endpointDefinitions = app.Services.GetServices<IEndpoint>();
        foreach (var endpoint in endpointDefinitions)
        {
            endpoint.MapEndpoint(app);
        }

        return app;
    }
}
