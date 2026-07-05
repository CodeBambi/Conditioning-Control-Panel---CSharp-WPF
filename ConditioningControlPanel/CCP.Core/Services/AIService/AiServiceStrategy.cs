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
/// <b>v1:</b> Cloud (<see cref="CoreAiService"/>) and Local (<see cref="LocalAiService"/>) are
/// ported. The OpenAI-compatible provider is not yet ported — it falls back to Cloud here (a
/// documented v1 limitation; see the task-board row). <see cref="GetRawChatCompletionAsync"/>
/// delegates to the active provider; both implement it as stateless local Ollama (the quiz contract).
/// </remarks>
public sealed class AiServiceStrategy : IAiService
{
    private readonly CoreAiService _cloud;
    private readonly LocalAiService _local;
    private readonly ISettingsService _settings;

    public AiServiceStrategy(CoreAiService cloud, LocalAiService local, ISettingsService settings)
    {
        _cloud = cloud;
        _local = local;
        _settings = settings;
    }

    /// <summary>The active provider for the current <see cref="CompanionPromptSettings.AiProvider"/>.</summary>
    private IAiService Active =>
        _settings.Current?.CompanionPrompt?.AiProvider == AiProviderType.Local ? _local : _cloud;

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
