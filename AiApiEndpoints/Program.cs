using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using AiApiEndpoints.DbContext;
using AiApiEndpoints.Functions;
using AiApiEndpoints.Models;
using AiApiEndpoints.Services;

var builder = WebApplication.CreateBuilder(args);

const string OllamaEndpoint = "http://localhost:11434";

builder.Services.AddHttpClient("ollama", client =>
{
    client.BaseAddress = new Uri(OllamaEndpoint);
    client.Timeout = Timeout.InfiniteTimeSpan;
    client.DefaultRequestHeaders.Accept.Clear();
    client.DefaultRequestHeaders.Accept.Add(
        new MediaTypeWithQualityHeaderValue("application/x-ndjson"));
});

builder.Services.AddScoped<KnowledgeService>();
builder.Services.AddScoped<ITrainingDataService, TrainingDataService>();
builder.Services.AddScoped<IPromptService, PromptService>();
builder.Services.AddScoped<IOllamaService, OllamaService>();

var app = builder.Build();

app.MapPost("/api/chat", async (
    ChatRequestDto request,
    IOllamaService ollamaService,
    HttpContext ctx
) =>
{
    await ollamaService.EnrichAndForwardChatAsync(
        request,
        ctx,
        ctx.RequestAborted
    );
});


app.Run();