using Scalar.AspNetCore;
using SemanticHub.IngestionService.Endpoints;
using SemanticHub.IngestionService.Extensions;
using SemanticHub.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddIngestionServices();

builder.Services.AddOpenApi("v1", options =>
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "SemanticHub Ingestion Service";
        document.Info.Description = "Accepts documents and indexes them into Azure AI Search for Retrieval Augmented Generation.";
        document.Info.Version = "v1";
        return Task.CompletedTask;
    }));

builder.Services.AddIngestionEndpoints();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi("/openapi/v1.json");
    app.MapScalarApiReference("/", options => options
        .WithTitle("SemanticHub Ingestion Service")
        .WithOpenApiRoutePattern("/openapi/v1.json"));
}

app.UseHttpsRedirection();

app.MapRegisteredIngestionEndpoints();
app.MapDefaultEndpoints();

app.Run();
