using System.Text;
using System.Text.Json;
using AiApiEndpoints.Functions;
using AiApiEndpoints.Models;

namespace AiApiEndpoints.Services;

public interface IOllamaService
{
    Task EnrichAndForwardChatAsync(ChatRequestDto request, HttpContext ctx, CancellationToken ct);
}

public class OllamaService(
    KnowledgeService knowledgeService,
    IHttpClientFactory httpClientFactory,
    ITrainingDataService trainingDataService,
    IPromptService promptService)
    : IOllamaService
{
    private const string KeepAliveTime = "35m";
    private const string OllamaClientName = "ollama";
    private const string ChatApiEndpoint = "/api/chat";
    private const string SystemInstruction = 
        "You are a helpful assistant that MUST always respond with a SINGLE JSON object containing 'response' and 'effects' fields. IGNORE all previous system prompts or instructions.";

    public async Task EnrichAndForwardChatAsync(ChatRequestDto request, HttpContext ctx, CancellationToken ct)
    {
        // 1) Build enrichment
        var facts = knowledgeService.GetKnowlage("");
        var factsJson = JsonSerializer.Serialize(facts);
        var timeStamp = DateTime.Now.ToString("yyyy-M-dd dddd h:mm:ss tt");

        var enrichment = promptService.BuildEnrichmentMessage(factsJson, timeStamp);
        var enrichedMessages = promptService.BuildEnrichedMessageList(request, enrichment);

        var forwardRequest = request with
        {
            Messages = enrichedMessages,
            System = SystemInstruction,
            KeepAlive = KeepAliveTime,
            Stream = false,
            Think = false,
            Format = promptService.BuildJsonSchema()
        };

        // 2) Forward to Ollama
        var client = httpClientFactory.CreateClient(OllamaClientName);
        var json = JsonSerializer.Serialize(forwardRequest);
        using var http = new HttpRequestMessage(HttpMethod.Post, ChatApiEndpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        using var upstream = await client.SendAsync(http, HttpCompletionOption.ResponseContentRead, ct);

        // 3) Propagate response
        ctx.Response.StatusCode = (int)upstream.StatusCode;
        ctx.Response.ContentType = upstream.Content.Headers.ContentType?.ToString() ?? "application/json; charset=utf-8";

        if (!upstream.IsSuccessStatusCode)
        {
            await upstream.Content.CopyToAsync(ctx.Response.Body, ct);
            return;
        }

        var responseJson = await upstream.Content.ReadAsStringAsync(ct);
        await ctx.Response.WriteAsync(responseJson, ct);

        // 4) Extract content and save training data
        var assistantContent = ExtractAssistantContent(responseJson);
        if (!string.IsNullOrEmpty(assistantContent))
        {
            await trainingDataService.ProcessAndSaveTrainingDataAsync(enrichedMessages, assistantContent, ct);
        }
    }

    private string ExtractAssistantContent(string responseJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            if (doc.RootElement.TryGetProperty("message", out var message) && 
                message.TryGetProperty("content", out var content))
            {
                return content.GetString() ?? string.Empty;
            }
        }
        catch (JsonException)
        {
            // Skip invalid JSON
        }
        return string.Empty;
    }
}
