using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Polly;
using SemanticHub.ServiceDefaults.Configuration;

namespace SemanticHub.Tests;

public class WebTests
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(120); // Increased from 30s to allow for Azure provisioning

    [Fact(Skip = "Integration test requires Azure OpenAI provisioning - run manually with proper Azure configuration")]
    public async Task GetWebResourceRootReturnsOkStatusCode()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.SemanticHub_AppHost>(cancellationToken);
        appHost.Services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Debug);
            // Override the logging filters from the app's configuration
            logging.AddFilter(appHost.Environment.ApplicationName, LogLevel.Debug);
            logging.AddFilter("Aspire.", LogLevel.Debug);
            // To output logs to the xUnit.net ITestOutputHelper, consider adding a package from https://www.nuget.org/packages?q=xunit+logging
        });
        // Configure resilience from configuration (defaults to disabled in test environment)
        var resilienceConfig = appHost.Configuration.GetSection("Resilience");
        var resilienceOptions = resilienceConfig.Get<ResilienceOptions>() ?? new ResilienceOptions { Enabled = false };

        appHost.Services.ConfigureHttpClientDefaults(clientBuilder =>
        {
            if (resilienceOptions.Enabled)
            {
                clientBuilder.AddStandardResilienceHandler(options =>
                {
                    options.Retry.MaxRetryAttempts = resilienceOptions.HttpClient.Retry.MaxAttempts;
                    options.Retry.UseJitter = true;

                    if (resilienceOptions.HttpClient.Retry.UseExponentialBackoff)
                    {
                        options.Retry.Delay = TimeSpan.FromMilliseconds(resilienceOptions.HttpClient.Retry.BaseDelayMs);
                        options.Retry.MaxDelay = TimeSpan.FromMilliseconds(resilienceOptions.HttpClient.Retry.MaxDelayMs);
                        options.Retry.BackoffType = DelayBackoffType.Exponential;
                    }
                    else
                    {
                        options.Retry.Delay = TimeSpan.FromMilliseconds(resilienceOptions.HttpClient.Retry.BaseDelayMs);
                        options.Retry.BackoffType = DelayBackoffType.Constant;
                    }
                });
            }
            else
            {
                // Resilience disabled - use no-op handler for immediate failures
                clientBuilder.AddResilienceHandler("no-op-resilience", (_, _) => { });
            }
        });

        await using var app = await appHost.BuildAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
        await app.StartAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);

        // Act
        var httpClient = app.CreateHttpClient("webapp");
        await app.ResourceNotifications.WaitForResourceHealthyAsync("webapp", cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
        var response = await httpClient.GetAsync("/", cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
