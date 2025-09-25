using SemanticHub.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Add HTTP client for KernelMemoryService with service discovery
builder.Services.AddHttpClient<KernelMemoryClient>("kernelmemory", client =>
{
    // This will be resolved by service discovery in Aspire
    client.BaseAddress = new Uri("https+http://kernelmemory");
});

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Knowledge API endpoints that proxy to KernelMemoryService

// Upload text document endpoint
app.MapPost("/knowledge/upload/text", async (TextUploadRequest request, KernelMemoryClient memoryClient) =>
{
    var response = await memoryClient.UploadTextAsync(request);
    if (response.IsSuccessStatusCode)
    {
        return Results.Ok(await response.Content.ReadAsStringAsync());
    }
    var errorDetails = await response.Content.ReadAsStringAsync();
    return Results.Problem(
        detail: $"Upstream service returned status code {(int)response.StatusCode}: {errorDetails}",
        statusCode: (int)response.StatusCode
    );
});

// Ask question endpoint
app.MapPost("/knowledge/ask", async (AskRequest request, KernelMemoryClient memoryClient) =>
{
    var response = await memoryClient.AskQuestionAsync(request);
    if (response.IsSuccessStatusCode)
    {
        return Results.Ok(await response.Content.ReadAsStringAsync());
    }
    var errorDetails = await response.Content.ReadAsStringAsync();
    return Results.Problem(
        detail: $"Upstream service returned status code {(int)response.StatusCode}: {errorDetails}",
        statusCode: (int)response.StatusCode
    );
});

// Search knowledge endpoint
app.MapPost("/knowledge/search", async (SearchRequest request, KernelMemoryClient memoryClient) =>
{
    var response = await memoryClient.SearchAsync(request);
    if (response.IsSuccessStatusCode)
    {
        return Results.Ok(await response.Content.ReadAsStringAsync());
    }
    var errorDetails = await response.Content.ReadAsStringAsync();
    return Results.Problem(
        detail: $"Upstream service returned status code {(int)response.StatusCode}: {errorDetails}",
        statusCode: (int)response.StatusCode
    );
});

// Get document status endpoint
app.MapGet("/knowledge/documents/{documentId}/status", async (string documentId, KernelMemoryClient memoryClient) =>
{
    var response = await memoryClient.GetDocumentStatusAsync(documentId);
    if (response.IsSuccessStatusCode)
    {
        return Results.Ok(await response.Content.ReadAsStringAsync());
    }
    var errorDetails = await response.Content.ReadAsStringAsync();
    return Results.Problem(
        detail: $"Upstream service returned status code {(int)response.StatusCode}: {errorDetails}",
        statusCode: (int)response.StatusCode
    );
});

app.MapDefaultEndpoints();

app.Run();

// HTTP client for communicating with KernelMemoryService
public class KernelMemoryClient(HttpClient httpClient)
{
    public async Task<HttpResponseMessage> UploadTextAsync(TextUploadRequest request)
    {
        return await httpClient.PostAsJsonAsync("/documents/text", request);
    }

    public async Task<HttpResponseMessage> AskQuestionAsync(AskRequest request)
    {
        return await httpClient.PostAsJsonAsync("/ask", request);
    }

    public async Task<HttpResponseMessage> SearchAsync(SearchRequest request)
    {
        return await httpClient.PostAsJsonAsync("/search", request);
    }

    public async Task<HttpResponseMessage> GetDocumentStatusAsync(string documentId)
    {
        return await httpClient.GetAsync($"/documents/{documentId}/status");
    }
}

// Request/Response models that match KernelMemoryService
public record TextUploadRequest(string Text, string? DocumentId = null);
public record AskRequest(string Question);
public record SearchRequest(string Query, int? Limit = 10);
