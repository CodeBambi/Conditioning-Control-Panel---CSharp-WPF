using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ConditioningControlPanel;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.AIService;
using ConditioningControlPanel.Core.Services.Moderation;
using ConditioningControlPanel.Core.Services.Settings;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// AI-5 contract tests: the OpenAI-compatible transport is reachable ONLY when an API key is
/// stored under the exact ISecretStore key the UI writes. This pins the literal
/// <c>"openai-api-key"</c> (<c>CCP.Core/Services/AIService/OpenAiService.cs:67</c>,
/// <c>SecretKey</c>) that the CompanionTabView key-entry UI must write, and proves the
/// presence/absence of that secret gates <see cref="OpenAiService.IsAvailable"/> — the exact
/// behavior the UI's Save/Clear affordances drive.
/// </summary>
public class OpenAiApiKeyTests
{
    [Fact]
    public void SecretKey_Constant_IsExactOpenAiApiKeyLiteral()
    {
        // The UI writes this exact key; OpenAiService.GetApiKey reads it. Drift here silently
        // breaks the transport (the AI-5 audit finding), so pin it at the Core seam.
        Assert.Equal("openai-api-key", OpenAiService.SecretKey);
    }

    [Fact]
    public void IsAvailable_TogglesWithSecretStoreKeyPresence()
    {
        var env = new TempAppEnvironment();
        var secrets = new InMemorySecretStore();
        try
        {
            var settings = new SettingsService(env, secrets);
            // Configure the OpenAI-compatible endpoint so the key is the only remaining gate.
            settings.Current.CompanionPrompt.OpenAiCompatibleEndpoint = "https://api.openai.com/v1";
            settings.Current.OfflineMode = false;

            var svc = new OpenAiService(
                settings,
                secrets,
                new PassModerationGuard(),
                new FakeSystemPromptBuilder(),
                new FakeAiResponseParser());

            // No key stored -> transport unreachable (the AI-5 gap the UI fixes).
            Assert.False(svc.IsAvailable);

            // UI Save writes the key under the exact constant -> transport reachable.
            secrets.Store(OpenAiService.SecretKey, Encoding.UTF8.GetBytes("sk-test-key-value"));
            Assert.True(svc.IsAvailable);

            // UI Clear deletes the secret -> transport gated again.
            secrets.Delete(OpenAiService.SecretKey);
            Assert.False(svc.IsAvailable);
        }
        finally
        {
            env.Cleanup();
        }
    }

    private sealed class TempAppEnvironment : IAppEnvironment
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), $"ccp-openai-key-tests-{Guid.NewGuid():N}");
        public string BaseDirectory => AppContext.BaseDirectory;
        public string UserDataPath { get; }
        public string ApplicationDataPath { get; }
        public string EffectiveAssetsPath { get; }

        public TempAppEnvironment()
        {
            UserDataPath = Path.Combine(Root, "local");
            ApplicationDataPath = Path.Combine(Root, "roaming");
            EffectiveAssetsPath = Path.Combine(Root, "assets");
        }

        public void Cleanup()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class InMemorySecretStore : ISecretStore
    {
        private readonly Dictionary<string, byte[]> _store = new();
        public void Store(string key, byte[] value) => _store[key] = value.ToArray();
        public byte[]? Retrieve(string key) => _store.TryGetValue(key, out var v) ? v.ToArray() : null;
        public void Delete(string key) => _store.Remove(key);
    }

    private sealed class PassModerationGuard : IModerationGuard
    {
        public ModerationResult CheckInput(string text) => ModerationResult.Pass();
        public ModerationResult CheckOutput(string text) => ModerationResult.Pass();
    }

    private sealed class FakeSystemPromptBuilder : ISystemPromptBuilder
    {
        public string GetSystemPrompt() => string.Empty;
    }

    private sealed class FakeAiResponseParser : IAiResponseParser
    {
        public ParsedAiResponse Parse(string response) => new() { CleanText = response };
    }
}
