using System.Text.Json;
using ConditioningControlPanel.Services.AIService;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #629 — the local model sat resident in VRAM/RAM for the whole keep_alive window after the
/// app exited. <see cref="LocalAiService.Dispose"/> now fires a best-effort synchronous
/// <c>POST api/generate</c> with <c>{"model": ..., "keep_alive": 0}</c> (2s cap) so Ollama
/// evicts the model immediately. These tests pin the payload shape produced by
/// <see cref="LocalAiService.BuildUnloadPayload(string)"/>.
///
/// The HTTP round-trip itself isn't exercised: the Dispose path resolves the base URL through
/// App.Settings and constructs a full service instance, which can't stand up headlessly, so we
/// assert the payload contract (the part that actually broke) rather than spin up an HttpListener.
/// </summary>
public class OllamaUnloadTests
{
    [Theory]
    [InlineData("qwen3.5:latest")]
    [InlineData("llama3.1:8b")]
    [InlineData("deepseek-r1:14b")]
    public void UnloadPayload_CarriesModel_AndKeepAliveZero(string model)
    {
        var json = LocalAiService.BuildUnloadPayload(model);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(model, root.GetProperty("model").GetString());

        var keepAlive = root.GetProperty("keep_alive");
        // keep_alive must be the NUMBER 0 (Ollama's "evict now" sentinel), not the string "0"
        // or the "10m"/"30m" residency hints used by warm-up.
        Assert.Equal(JsonValueKind.Number, keepAlive.ValueKind);
        Assert.Equal(0, keepAlive.GetInt32());
    }

    [Fact]
    public void UnloadPayload_HasExactlyModelAndKeepAlive()
    {
        var json = LocalAiService.BuildUnloadPayload("qwen3.5:latest");

        using var doc = JsonDocument.Parse(json);
        int count = 0;
        foreach (var _ in doc.RootElement.EnumerateObject()) count++;
        Assert.Equal(2, count); // no stray fields that would change Ollama's behavior
    }
}
