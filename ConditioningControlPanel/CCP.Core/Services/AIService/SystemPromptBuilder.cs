using System.Text;
using ConditioningControlPanel.Core.Services.Moderation;
using ConditioningControlPanel.Core.Services.Settings;
using Microsoft.Extensions.Logging;

namespace ConditioningControlPanel.Core.Services.AIService;

/// <summary>
/// Assembles the companion system prompt from the active persona settings, mirroring the WPF
/// <c>BambiSprite.GetSystemPrompt</c> + <c>BuildPromptFromPreset</c> contract
/// (<c>Services/AiService.cs:120</c>, <c>Services/BambiSprite.cs:510-720</c>) and wrapping it
/// with the hardcoded <see cref="SafetyComposer"/> (Layer-2 refusal preamble/floor). The active
/// persona is read directly from <see cref="Models.CompanionPromptSettings"/> (Personality /
/// ExplicitReaction / KnowledgeBase / ContextReactions / OutputRules), with SlutMode swapping
/// Personality for SlutModePersonality — these fields hold whichever built-in or custom preset
/// the user last applied. Mod-aware video-link names are appended when an
/// <see cref="IModService"/> is available.
/// </summary>
/// <remarks>
/// v1 scope: persona fields + mod video-link names + SafetyComposer wrap. The WPF builder also
/// injects GlobalKnowledgeBaseLinks, hypnotube reverse-link lookup, quiz-context, and
/// FillVideoPlaceholders; those are follow-ups once their data seams are confirmed in Core.
/// </remarks>
public interface ISystemPromptBuilder
{
    /// <summary>The SafetyComposer-wrapped system prompt for the companion persona.</summary>
    string GetSystemPrompt();
}

public sealed class SystemPromptBuilder : ISystemPromptBuilder
{
    private readonly ISettingsService _settings;
    private readonly IModService? _mods;
    private readonly ILogger<SystemPromptBuilder>? _logger;

    public SystemPromptBuilder(ISettingsService settings, IModService? mods = null, ILogger<SystemPromptBuilder>? logger = null)
    {
        _settings = settings;
        _mods = mods;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string GetSystemPrompt()
    {
        var settings = _settings.Current;
        var cp = settings?.CompanionPrompt;
        if (settings == null || cp == null)
            return SafetyComposer.Wrap(string.Empty);

        var slutMode = settings.SlutModeEnabled && !string.IsNullOrWhiteSpace(cp.SlutModePersonality);
        var personality = slutMode ? cp.SlutModePersonality : cp.Personality;

        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(personality))
        {
            sb.AppendLine(personality);
            sb.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(cp.ExplicitReaction))
        {
            sb.AppendLine(cp.ExplicitReaction);
            sb.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(cp.KnowledgeBase))
        {
            sb.AppendLine("KNOWLEDGE BASE:");
            sb.AppendLine(cp.KnowledgeBase);
            sb.AppendLine();
        }

        AppendVideoNames(sb);

        if (!string.IsNullOrWhiteSpace(cp.ContextReactions))
        {
            sb.AppendLine(cp.ContextReactions);
            sb.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(cp.OutputRules))
        {
            sb.AppendLine(cp.OutputRules);
            sb.AppendLine();
        }

        var assembled = sb.ToString().Trim();
        if (_mods != null)
            assembled = _mods.MakeModAware(assembled);
        return SafetyComposer.Wrap(assembled);
    }

    /// <summary>Appends the active mod's video-link catalog names (WPF GetCoreMediaLinks).</summary>
    private void AppendVideoNames(StringBuilder sb)
    {
        IReadOnlyDictionary<string, string>? links = null;
        try { links = _mods?.GetVideoLinks(); }
        catch (Exception ex) { _logger?.LogDebug(ex, "SystemPromptBuilder: GetVideoLinks failed; omitting media names."); }
        if (links == null || links.Count == 0) return;

        sb.AppendLine("AVAILABLE VIDEOS (you may reference these by name):");
        sb.AppendLine(string.Join(", ", links.Keys));
        sb.AppendLine();
    }
}
