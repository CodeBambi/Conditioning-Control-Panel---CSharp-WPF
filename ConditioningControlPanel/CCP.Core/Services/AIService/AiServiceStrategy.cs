using ConditioningControlPanel.Core.Services.Moderation;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Core.Services.AIService;

/// <summary>
/// Provider strategy for <see cref="IAiService"/> — selects the active provider from
/// <see cref="CompanionPromptSettings.AiProvider"/> and delegates. Ports the WPF
/// <c>Services/AIService/AiServiceStrategy.cs</c> onto DI-injected providers (the WPF version
/// lazy-constructs them against <c>App.*</c> statics). Both providers are injected as singletons;
/// selection is live (read on every call, no restart) — matching WPF (<c>AiServiceStrategy.cs:21,32-65</c>).
/// </summary>
/// <remarks>
/// <b>v1:</b> Cloud (<see cref="CoreAiService"/>), Local (<see cref="LocalAiService"/>), and OpenAI-compatible
/// (<see cref="OpenAiService"/>) providers are all ported. <see cref="GetRawChatCompletionAsync"/> delegates to
/// the active provider; cloud/local implement it as stateless local Ollama, OpenAI as its own transport (the
/// quiz contract). AI-command execution (<c>AllowAiToControlEffects</c>) + the enrichment block + a key-entry
/// UI for the OpenAI provider are filed follow-ups (see the task-board row).
/// </remarks>
public sealed class AiServiceStrategy : IAiService
{
    private readonly CoreAiService _cloud;
    private readonly LocalAiService _local;
    private readonly OpenAiService _openai;
    private readonly ISettingsService _settings;

    public AiServiceStrategy(CoreAiService cloud, LocalAiService local, OpenAiService openai, ISettingsService settings)
    {
        _cloud = cloud;
        _local = local;
        _openai = openai;
        _settings = settings;
    }

    /// <summary>The active provider for the current <see cref="CompanionPromptSettings.AiProvider"/>.</summary>
    private IAiService Active => _settings.Current?.CompanionPrompt?.AiProvider switch
    {
        AiProviderType.Local => _local,
        AiProviderType.OpenAiCompatible => _openai,
        _ => _cloud
    };

    /// <inheritdoc/>
    public bool IsAvailable => Active.IsAvailable;

    /// <inheritdoc/>
    public int DailyRequestsRemaining => Active.DailyRequestsRemaining;

    /// <inheritdoc/>
    public Task<string> GetBambiReplyAsync(string userInput, bool isUserMessage = false)
        => Active.GetBambiReplyAsync(userInput, isUserMessage);

    /// <inheritdoc/>
    public Task<AiReplyResult> GetBambiReplyExAsync(string userInput, bool isUserMessage = false)
        => Active.GetBambiReplyExAsync(userInput, isUserMessage);

    /// <inheritdoc/>
    public Task<string?> GetAwarenessReactionAsync(string detectedName, string category, string serviceName = "", string pageTitle = "")
        => Active.GetAwarenessReactionAsync(detectedName, category, serviceName, pageTitle);

    /// <inheritdoc/>
    public Task<string?> GetStillOnReactionAsync(string displayName, string category, TimeSpan duration)
        => Active.GetStillOnReactionAsync(displayName, category, duration);

    /// <inheritdoc/>
    public Task<string?> GetKeywordCommentAsync(string keyword, string? promptTemplate = null)
        => Active.GetKeywordCommentAsync(keyword, promptTemplate);

    /// <inheritdoc/>
    public Task<string?> GetLockScreenReaction(string sentance, int mistakes, int amount, string? promptTemplate = null)
        => Active.GetLockScreenReaction(sentance, mistakes, amount, promptTemplate);

    /// <inheritdoc/>
    public Task<string?> GetVideoDoneReaction(string title, string? promptTemplate = null)
        => Active.GetVideoDoneReaction(title, promptTemplate);

    /// <inheritdoc/>
    public Task<string?> GetRawChatCompletionAsync(IEnumerable<(string role, string content)> messages, double temperature = 0.8)
        => Active.GetRawChatCompletionAsync(messages, temperature);

    /// <summary>
    /// No-op: both providers are DI singletons and are disposed by the container. Implementing
    /// IDisposable here only to satisfy the <see cref="IAiService"/> contract.
    /// </summary>
    public void Dispose() { }
}
