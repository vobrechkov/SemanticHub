namespace SemanticHub.ServiceDefaults.Configuration;

/// <summary>
/// Configuration options for resilience policies (retry, circuit breaker, timeout).
/// Supports environment-specific behavior (e.g., disabled in Development, aggressive in Production).
/// </summary>
public sealed class ResilienceOptions
{
    /// <summary>
    /// Master switch to enable or disable all resilience policies.
    /// When false, no-op policies are used for immediate failures (useful for Development).
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// HTTP client resilience configuration (standard resilience handler).
    /// </summary>
    public HttpClientResilienceOptions HttpClient { get; init; } = new();

    /// <summary>
    /// Web scraping resilience configuration (Playwright scraper).
    /// </summary>
    public WebScrapingResilienceOptions WebScraping { get; init; } = new();
}

/// <summary>
/// Resilience options for HTTP clients using the standard resilience handler.
/// </summary>
public sealed class HttpClientResilienceOptions
{
    /// <summary>
    /// Retry policy configuration.
    /// </summary>
    public RetryOptions Retry { get; init; } = new();

    /// <summary>
    /// Circuit breaker policy configuration.
    /// </summary>
    public CircuitBreakerOptions CircuitBreaker { get; init; } = new();

    /// <summary>
    /// Timeout policy configuration.
    /// </summary>
    public TimeoutOptions Timeout { get; init; } = new();
}

/// <summary>
/// Retry policy configuration.
/// </summary>
public sealed class RetryOptions
{
    /// <summary>
    /// Maximum number of retry attempts.
    /// </summary>
    public int MaxAttempts { get; init; } = 3;

    /// <summary>
    /// Base delay in milliseconds between retry attempts.
    /// </summary>
    public int BaseDelayMs { get; init; } = 500;

    /// <summary>
    /// Whether to use exponential backoff for retry delays.
    /// If true, delay = BaseDelayMs * 2^attempt.
    /// If false, delay = BaseDelayMs (constant).
    /// </summary>
    public bool UseExponentialBackoff { get; init; } = true;

    /// <summary>
    /// Maximum delay in milliseconds for exponential backoff.
    /// Prevents excessive wait times on later retry attempts.
    /// </summary>
    public int MaxDelayMs { get; init; } = 30000;
}

/// <summary>
/// Circuit breaker policy configuration.
/// </summary>
public sealed class CircuitBreakerOptions
{
    /// <summary>
    /// Failure threshold (0.0-1.0) that triggers the circuit breaker.
    /// E.g., 0.5 means circuit opens when 50% of requests fail.
    /// </summary>
    public double FailureThreshold { get; init; } = 0.5;

    /// <summary>
    /// Duration in seconds to sample failures for circuit breaker calculation.
    /// </summary>
    public int SamplingDurationSeconds { get; init; } = 30;

    /// <summary>
    /// Minimum number of requests before circuit breaker can trip.
    /// Prevents opening circuit during low-traffic periods.
    /// </summary>
    public int MinimumThroughput { get; init; } = 10;

    /// <summary>
    /// Duration in seconds the circuit stays open before attempting recovery.
    /// </summary>
    public int BreakDurationSeconds { get; init; } = 30;
}

/// <summary>
/// Timeout policy configuration.
/// </summary>
public sealed class TimeoutOptions
{
    /// <summary>
    /// Total request timeout in seconds (includes retries).
    /// </summary>
    public int TotalRequestTimeoutSeconds { get; init; } = 30;
}

/// <summary>
/// Resilience options specifically for web scraping operations (Playwright).
/// </summary>
public sealed class WebScrapingResilienceOptions
{
    /// <summary>
    /// Maximum number of retry attempts for web scraping.
    /// </summary>
    public int MaxAttempts { get; init; } = 3;

    /// <summary>
    /// Base delay in milliseconds between retry attempts.
    /// </summary>
    public int BaseDelayMs { get; init; } = 250;

    /// <summary>
    /// Whether to use exponential backoff for retry delays.
    /// If true, delay = BaseDelayMs * 2^attempt.
    /// If false, delay = BaseDelayMs (constant).
    /// </summary>
    public bool UseExponentialBackoff { get; init; } = true;

    /// <summary>
    /// Maximum delay in milliseconds for exponential backoff.
    /// </summary>
    public int MaxDelayMs { get; init; } = 10000;
}
