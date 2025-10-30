using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Polly;
using SemanticHub.ServiceDefaults.Configuration;

namespace SemanticHub.ServiceDefaults;

// Adds common .NET Aspire services: service discovery, resilience, health checks, and OpenTelemetry.
// This project should be referenced by each service project in your solution.
// To learn more about using this project, see https://aka.ms/dotnet/aspire/service-defaults
public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        // Configure resilience options from configuration
        builder.Services.Configure<ResilienceOptions>(
            builder.Configuration.GetSection("Resilience"));

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Add resilience with configuration support
            http.AddConfigurableResilience(builder.Configuration);

            // Turn on service discovery by default
            http.AddServiceDiscovery();
        });

        // Uncomment the following to restrict the allowed schemes for service discovery.
        // builder.Services.Configure<ServiceDiscoveryOptions>(options =>
        // {
        //     options.AllowedSchemes = ["https"];
        // });

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation(tracing =>
                        // Exclude health check requests from tracing
                        tracing.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                            && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath)
                    )
                    // Uncomment the following line to enable gRPC instrumentation (requires the OpenTelemetry.Instrumentation.GrpcNetClient package)
                    //.AddGrpcClientInstrumentation()
                    .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        // Uncomment the following lines to enable the Azure Monitor exporter (requires the Azure.Monitor.OpenTelemetry.AspNetCore package)
        //if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        //{
        //    builder.Services.AddOpenTelemetry()
        //       .UseAzureMonitor();
        //}

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            // Add a default liveness check to ensure app is responsive
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Adding health checks endpoints to applications in non-development environments has security implications.
        // See https://aka.ms/dotnet/aspire/healthchecks for details before enabling these endpoints in non-development environments.
        if (app.Environment.IsDevelopment())
        {
            // All health checks must pass for app to be considered ready to accept traffic after starting
            app.MapHealthChecks(HealthEndpointPath);

            // Only health checks tagged with the "live" tag must pass for app to be considered alive
            app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("live")
            });
        }

        return app;
    }

    /// <summary>
    /// Adds configurable resilience handlers to HTTP clients based on ResilienceOptions configuration.
    /// When resilience is disabled, uses no-op policies for immediate failures.
    /// </summary>
    private static IHttpClientBuilder AddConfigurableResilience(
        this IHttpClientBuilder builder,
        IConfiguration configuration)
    {
        var resilienceSection = configuration.GetSection("Resilience");
        var resilienceOptions = resilienceSection.Get<ResilienceOptions>() ?? new ResilienceOptions();

        if (!resilienceOptions.Enabled)
        {
            // Resilience is disabled - use no-op handler that fails immediately
            builder.AddResilienceHandler("no-op-resilience", (resiliencePipelineBuilder, context) =>
            {
                // Empty pipeline - no retry, no circuit breaker, no timeout beyond HttpClient defaults
                // This allows immediate failures for Development environments
            });

            return builder;
        }

        // Resilience is enabled - configure standard resilience handler with custom options
        builder.AddStandardResilienceHandler(options =>
        {
            // Configure retry policy
            options.Retry.MaxRetryAttempts = resilienceOptions.HttpClient.Retry.MaxAttempts;
            options.Retry.UseJitter = true;

            if (resilienceOptions.HttpClient.Retry.UseExponentialBackoff)
            {
                options.Retry.Delay = TimeSpan.FromMilliseconds(resilienceOptions.HttpClient.Retry.BaseDelayMs);
                options.Retry.MaxDelay = TimeSpan.FromMilliseconds(resilienceOptions.HttpClient.Retry.MaxDelayMs);
                options.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
            }
            else
            {
                options.Retry.Delay = TimeSpan.FromMilliseconds(resilienceOptions.HttpClient.Retry.BaseDelayMs);
                options.Retry.BackoffType = Polly.DelayBackoffType.Constant;
            }

            // Configure circuit breaker policy
            options.CircuitBreaker.FailureRatio = resilienceOptions.HttpClient.CircuitBreaker.FailureThreshold;
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(resilienceOptions.HttpClient.CircuitBreaker.SamplingDurationSeconds);
            options.CircuitBreaker.MinimumThroughput = resilienceOptions.HttpClient.CircuitBreaker.MinimumThroughput;
            options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(resilienceOptions.HttpClient.CircuitBreaker.BreakDurationSeconds);

            // Configure total request timeout
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(resilienceOptions.HttpClient.Timeout.TotalRequestTimeoutSeconds);
        });

        return builder;
    }
}
