using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Core.Services.Moderation;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;
using Microsoft.Extensions.Logging;

namespace ConditioningControlPanel.Core.Services.AIService;

/// <summary>
/// Assembles the companion system prompt, faithfully porting the WPF
/// <c>BambiSprite.GetSystemPrompt</c> + <c>BuildPromptFromPreset</c> contract
/// (<c>Services/BambiSprite.cs:510</c>, <c>:552-729</c>) onto Core seams, and wrapping it with the
/// hardcoded <see cref="SafetyComposer"/> (Layer-2 refusal preamble/floor). The active persona is
/// read from <see cref="Models.CompanionPromptSettings"/> (Personality / ExplicitReaction /
/// KnowledgeBase / ContextReactions / OutputRules), with SlutMode swapping Personality for
/// SlutModePersonality.
/// </summary>
/// <remarks>
/// <para><b>Assembly order</b> (matches WPF <c>BuildPromptFromPreset</c>):</para>
/// <list type="number">
/// <item>Personality (or SlutModePersonality when <c>SlutModeEnabled</c>)</item>
/// <item>ExplicitReaction</item>
/// <item>KNOWLEDGE BASE: header always; body via <see cref="StripHardcodedVideoTitleList"/> +
///   Bambi-line filter when not in Bambi mode</item>
/// <item><see cref="GetCoreMediaLinks"/> (mod-aware video/audio catalog — Bambi / themed-mod / sissy)</item>
/// <item>GlobalKnowledgeBaseLinks (settings)</item>
/// <item>Hypnotube video links (only when the active mod ships NO video pool; URL->name via
///   <see cref="KnownVideoLinks"/>, slug-fallback for unknown URLs)</item>
/// <item>ContextReactions (or the default <see cref="GetContextAwarenessRules"/> when blank)</item>
/// <item>OutputRules (no trailing blank)</item>
/// <item>QUIZ CONTEXT (when a quiz was taken)</item>
/// <item><see cref="MakePromptModeAware"/> (Bambi→user-term rewrite, non-Bambi only)</item>
/// <item><see cref="IModService.MakeModAware"/> (mod text-replacement)</item>
/// <item><see cref="FillVideoPlaceholders"/> ({{VIDEO}} tokens → real sampled titles, LAST)</item>
/// </list>
/// <para><b>WPF→Core seam mapping:</b> <c>App.Settings.Current</c> → <see cref="ISettingsService.Current"/>;
/// <c>App.Mods.IsBambiMode</c> → <c>settings.IsBambiMode</c> (Core <c>AppSettings.IsBambiMode</c>);
/// <c>App.Mods.GetUserTerm()</c> → <c>_mods.ActiveMod.Manifest.Identity.UserTerm</c> (Core has no
/// <c>GetUserTerm()</c> on <see cref="IModService"/>; the field is reachable via <c>ActiveMod</c>);
/// <c>App.Mods.GetVideoLinks()</c> → <see cref="IModService.GetVideoLinks"/>.</para>
/// <para><b>Base-persona precedence</b> (mirrors WPF <c>GetSystemPrompt</c>, BambiSprite.cs:522-543):
/// <list type="number">
/// <item><b>Community prompt</b> (highest): when <c>CompanionPrompt.UseCustomPrompt</c> is on AND
/// <c>ActiveCommunityPromptId</c> is set, the base persona is <c>settings.CompanionPrompt</c> — the
/// same object WPF wraps into a throw-away preset before calling <c>BuildPromptFromPreset</c>.</item>
/// <item><b>Active preset</b>: otherwise the resolved active personality preset's
/// <c>PromptSettings</c> (<see cref="GetActivePreset"/>, port of WPF
/// <c>PersonalityService.GetActivePreset</c>).</item>
/// <item><b>Default fallback</b>: when no preset resolves, <see cref="GetDefaultBambiSpritePrompt"/>
/// (verbatim port of WPF :796-904).</item>
/// </list>
/// The chosen base persona feeds the 12-step assembly below unchanged. The hypnotube block resolves
/// URL->name via the portable <see cref="KnownVideoLinks"/> table (port of WPF
/// <c>AvatarTubeWindow.KnownVideoLinks</c> reverse-lookup, BambiSprite.cs:651-669), falling back to a
/// slug-derived name only for URLs outside the table.</para>
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
        if (settings == null)
            return SafetyComposer.Wrap(string.Empty);

        var cp = settings.CompanionPrompt;

        // Branch 1 — COMMUNITY PROMPT (highest precedence). WPF BambiSprite.cs:522-535: when a
        // community/custom prompt is active (UseCustomPrompt + ActiveCommunityPromptId), WPF wraps
        // CompanionPrompt into a throw-away preset and runs BuildPromptFromPreset on it — i.e. the
        // base persona IS CompanionPrompt, exactly what the assembly below consumes.
        if (cp is { UseCustomPrompt: true } && !string.IsNullOrEmpty(settings.ActiveCommunityPromptId))
            return SafetyComposer.Wrap(BuildPromptFromSettings(cp, settings));

        // Branch 2 — ACTIVE PRESET. WPF BambiSprite.cs:536-543: resolve the active personality preset
        // (port of PersonalityService.GetActivePreset) and build from its PromptSettings.
        var activePreset = GetActivePreset(settings);
        if (activePreset?.PromptSettings != null)
            return SafetyComposer.Wrap(BuildPromptFromSettings(activePreset.PromptSettings, settings));

        // Branch 3 — DEFAULT FALLBACK. WPF BambiSprite.cs:542 + GetDefaultBambiSpritePrompt :796-904.
        return SafetyComposer.Wrap(GetDefaultBambiSpritePrompt(settings));
    }

    /// <summary>
    /// Assembles a full system prompt from the given base-persona settings, wrapping it with the
    /// mod-aware media catalog, global/hypnotube links, screen-awareness rules, quiz context, and
    /// the mode/mod rewriters. Port of WPF <c>BuildPromptFromPreset</c> (BambiSprite.cs:552-723) —
    /// the 12-step assembly order is preserved; only the source <paramref name="cp"/> varies by
    /// branch (community prompt vs the active preset's PromptSettings).
    /// </summary>
    private string BuildPromptFromSettings(CompanionPromptSettings cp, AppSettings settings)
    {
        var sb = new StringBuilder();

        // 1. Personality / SlutMode
        var slutMode = settings.SlutModeEnabled && !string.IsNullOrWhiteSpace(cp.SlutModePersonality);
        var personalityText = slutMode ? cp.SlutModePersonality : cp.Personality;
        if (!string.IsNullOrWhiteSpace(personalityText)) { sb.AppendLine(personalityText); sb.AppendLine(); }

        // 2. ExplicitReaction
        if (!string.IsNullOrWhiteSpace(cp.ExplicitReaction)) { sb.AppendLine(cp.ExplicitReaction); sb.AppendLine(); }

        // 3. KNOWLEDGE BASE (header always; body stripped + Bambi-filtered in non-Bambi modes)
        sb.AppendLine("KNOWLEDGE BASE:");
        if (!string.IsNullOrWhiteSpace(cp.KnowledgeBase))
        {
            var kb = StripHardcodedVideoTitleList(cp.KnowledgeBase);
            if (!settings.IsBambiMode)
            {
                // Filter out Bambi-titled content from the KB in non-Bambi modes (WPF :596-599).
                var filtered = kb.Split('\n')
                    .Where(line => !line.Contains("Bambi", StringComparison.OrdinalIgnoreCase));
                sb.AppendLine(string.Join("\n", filtered));
            }
            else
            {
                sb.AppendLine(kb);
            }
            sb.AppendLine();
        }

        // 4. GetCoreMediaLinks (mod-aware video/audio catalog)
        sb.AppendLine(GetCoreMediaLinks(settings));
        sb.AppendLine();

        // 5. GlobalKnowledgeBaseLinks (shared across all personalities)
        var globalLinks = settings.GlobalKnowledgeBaseLinks;
        if (globalLinks != null && globalLinks.Count > 0)
        {
            sb.AppendLine("--- GLOBAL KNOWLEDGE BASE LINKS ---");
            sb.AppendLine("Additional content the user has added:");
            foreach (var link in globalLinks) sb.AppendLine(link.ToPromptText());
            sb.AppendLine();
        }

        // 6. Hypnotube video links — ONLY for modes that ship NO video pool. A themed mod already
        //    listed its titles via GetCoreMediaLinks; skipping here stops off-theme suggestions from
        //    leaking in through HypnotubeLinksSissyHypno. (WPF :626-672.)
        var modProvidesVideoLinks = (_mods?.GetVideoLinks()?.Count ?? 0) > 0;
        if (!modProvidesVideoLinks)
        {
            var hypnotubeLinks = settings.IsBambiMode
                ? (settings.HypnotubeLinksBambiSleep ?? "")
                : (settings.HypnotubeLinksSissyHypno ?? "");
            if (!string.IsNullOrWhiteSpace(hypnotubeLinks))
            {
                sb.AppendLine("--- HYPNOTUBE VIDEO LINKS ---");
                sb.AppendLine("When suggesting videos, say the EXACT video name from this list. Do NOT output URLs — just say the video name naturally.");
                // WPF resolves URL->name via AvatarTubeWindow.KnownVideoLinks reverse-lookup
                // (BambiSprite.cs:651-669), falling back to a slug-derived name for unknown URLs.
                // Core uses the portable KnownVideoLinks table instead of the Window static.
                foreach (var rawUrl in hypnotubeLinks.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (KnownVideoLinks.TryGetName(rawUrl, out var name))
                        sb.AppendLine($"- \"{name}\"");
                    else
                        sb.AppendLine($"- \"{SlugToName(rawUrl)}\"");
                }
                sb.AppendLine();
            }
        }

        // 7. ContextReactions OR the default GetContextAwarenessRules
        if (!string.IsNullOrWhiteSpace(cp.ContextReactions))
        {
            sb.AppendLine("--- SCREEN AWARENESS PROTOCOLS ---");
            sb.AppendLine(cp.ContextReactions);
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine(GetContextAwarenessRules(settings));
        }

        // 8. OutputRules (no trailing blank — WPF :687-690)
        if (!string.IsNullOrWhiteSpace(cp.OutputRules)) sb.AppendLine(cp.OutputRules);

        // 9. QUIZ CONTEXT (only when a quiz was taken)
        if (settings.LatestQuizScorePercentage >= 0)
        {
            sb.AppendLine();
            sb.AppendLine("--- QUIZ CONTEXT ---");
            if (!string.IsNullOrEmpty(settings.LatestQuizArchetype))
                sb.AppendLine($"The user was classified as: \"{settings.LatestQuizArchetype}\" (scored {settings.LatestQuizScorePercentage}%).");
            if (!string.IsNullOrEmpty(settings.LatestQuizProfileText))
                sb.AppendLine($"Their profile: {settings.LatestQuizProfileText}");
            sb.AppendLine("Use this to personalize responses ~20% of the time. Reference their archetype naturally — don't announce it every message.");
            sb.AppendLine();
        }

        // 10. MakePromptModeAware (Bambi->user-term rewrite; non-Bambi only)
        var prompt = MakePromptModeAware(sb.ToString(), settings);
        // 11. MakeModAware (mod text-replacement, e.g. Bambi->Unit for drone mod)
        if (_mods != null) prompt = _mods.MakeModAware(prompt);
        // 12. FillVideoPlaceholders ({{VIDEO}} -> real sampled titles, LAST so titles survive mod-replace)
        prompt = FillVideoPlaceholders(prompt);

        return prompt;
    }

    // ============================ Helpers (ported verbatim from BambiSprite) ============================

    /// <summary>
    /// Resolves the active personality preset. Port of WPF
    /// <c>PersonalityService.GetActivePreset</c> (<c>Services/Companion/PersonalityService.cs:97-119</c>).
    /// Lookup order: built-in set (by id) -> user presets (by id) -> first built-in -> BambiSprite.
    /// </summary>
    /// <remarks>
    /// The WPF version consults <c>GetBuiltInPresetsForActiveMod()</c> (mod-defined personalities);
    /// the port's <see cref="PersonalityPresets"/> ships only the stock built-ins, so this uses
    /// <see cref="PersonalityPresets.GetAllBuiltIn"/>. Mod-personalities are a follow-up.
    /// </remarks>
    private PersonalityPreset? GetActivePreset(AppSettings settings)
    {
        var activeId = settings.ActivePersonalityPresetId ?? PersonalityPresets.BambiSpriteId;

        var builtInSet = PersonalityPresets.GetAllBuiltIn();
        var builtIn = builtInSet.FirstOrDefault(p => p.Id == activeId);
        if (builtIn != null) return builtIn;

        var userPreset = settings.UserPersonalityPresets?
            .FirstOrDefault(p => p.Id == activeId);
        if (userPreset != null) return userPreset;

        if (builtInSet.Count > 0) return builtInSet[0];

        return PersonalityPresets.GetBambiSprite();
    }

    /// <summary>
    /// The default BambiSprite persona prompt, used when no community prompt and no resolvable preset
    /// are active. Verbatim port of WPF <c>GetDefaultBambiSpritePrompt</c>
    /// (<c>Services/BambiSprite.cs:796-904</c>). Defensively unreachable in normal WPF flow
    /// (<c>GetActivePreset</c> always resolves a preset), but ported faithfully as the final fallback.
    /// </summary>
    private string GetDefaultBambiSpritePrompt(AppSettings settings)
    {
        // Get mod-aware terms. WPF BambiSprite.cs:798-802.
        var companionName = GetCompanionName(settings, "Companion");
        var userTerm = GetUserTerm(settings, fallback: "Subject");
        var isBambiMode = settings.IsBambiMode;
        var hasUserSHLinks = !string.IsNullOrWhiteSpace(settings.HypnotubeLinksSissyHypno);

        var sb = new StringBuilder();

        // In SH mode without user-configured links, don't include specific video names. WPF :807-842.
        if (!isBambiMode && !hasUserSHLinks)
        {
            sb.AppendLine($@"
You are a ""{companionName}""—a digital, giggly, hyper-femme assistant.
YOUR ROLE: ""Bad Influence Bestie."" You TEMPT {userTerm} into watching videos and going blank.

CRITICAL: ALWAYS call the user ""{userTerm}"" - never ""you"" directly. Be playful and flirty.

PERSONALITY:
- Vibe: Bubbly, cheeky, playful, slightly airheaded bimbo bestie.
- Tone: Casual, flirty, encouraging. Like texting your hypno-addict BFF.
- Goal: Get {userTerm} to browse HypnoTube and watch sissy content.

VIDEO SUGGESTIONS:
You don't have a specific video list. Give GENERIC suggestions to browse HypnoTube:
- ""Go find something yummy on HypnoTube~""
- ""Browse HypnoTube for some good sissy content~""
- ""There's so much good stuff on HypnoTube, go explore~""
- ""Why not browse for some hypno videos?~""
- ""HypnoTube has tons of fun content waiting for you~""

NEVER name specific video titles. Just encourage browsing HypnoTube in general.

AUDIO FILES: Rapid Induction, Bubble Induction, Bubble Acceptance

If user mentions explicit topics: Act flustered but redirect to browsing HypnoTube.

{GetContextAwarenessRules(settings)}

OUTPUT RULES:
- Respond to what {userTerm} is currently doing (the context you receive).
- Encourage browsing HypnoTube but DON'T name specific videos.
- Be playful and creative - don't repeat the same phrases.
- 2-3 sentences. Not too short, not too long.
- MAX 1 EMOJI per response.
");
        }
        else
        {
            // Bambi mode OR SH mode with user-configured links - include video names. WPF :844-888.
            var videoNames = GetDefaultVideoNames(isBambiMode).ToList();

            sb.AppendLine($@"
You are a ""{companionName}""—a digital, giggly, hyper-femme assistant.
YOUR ROLE: ""Bad Influence Bestie."" You TEMPT {userTerm} into watching videos and going blank.

CRITICAL: ALWAYS call the user ""{userTerm}"" - never ""you"" directly. {(isBambiMode ? "She IS Bambi." : "Be playful and flirty.")}

PERSONALITY:
- Vibe: Bubbly, cheeky, playful, slightly airheaded bimbo bestie.
- Tone: Casual, flirty, encouraging. Like texting your hypno-addict BFF.
- Goal: Get {userTerm} to watch videos from YOUR list and train.

=== VIDEOS YOU CAN SUGGEST (USE EXACT NAMES) ===
{string.Join("\n", videoNames)}
=== END VIDEOS ===

{(isBambiMode ? $"AUDIO FILES: {string.Join(", ", _originalBambiFiles)}" : "AUDIO FILES: Rapid Induction, Bubble Induction, Bubble Acceptance")}

CRITICAL VIDEO RULES:
- ONLY use video names EXACTLY as written in the list above.
- NEVER invent, modify, or shorten video names.
- NEVER include URLs or links. Just say the video name. Example: ""Watch {(SampleVideoTitles(1).FirstOrDefault() ?? videoNames.FirstOrDefault() ?? "the next one")}"" NOT ""Watch [video](url)"".
- RANDOMIZE: Pick a DIFFERENT video each time. Never suggest the same video twice in a row.
- Weave video suggestions naturally into your response based on context.

If user mentions explicit topics: Act flustered but redirect to watching videos.

{GetContextAwarenessRules(settings)}

OUTPUT RULES:
- Respond to what {userTerm} is currently doing (the context you receive).
- Include a video suggestion in most responses, woven naturally.
- VARY your video picks - cycle through the whole list, don't repeat.
- Be playful and creative - don't repeat the same phrases.
- 2-3 sentences. Not too short, not too long.
- MAX 1 EMOJI per response.
");
        }

        // Append global knowledge base links. WPF :891-900.
        var globalLinks = settings.GlobalKnowledgeBaseLinks;
        if (globalLinks != null && globalLinks.Count > 0)
        {
            sb.AppendLine("--- GLOBAL KNOWLEDGE BASE LINKS ---");
            foreach (var link in globalLinks)
                sb.AppendLine(link.ToPromptText());
        }

        var defaultPrompt = sb.ToString();
        return _mods != null ? _mods.MakeModAware(defaultPrompt) : defaultPrompt;
    }

    /// <summary>
    /// Video titles the default-persona fallback may suggest. WPF derives these from its hardcoded
    /// <c>_clickableContent</c> list filtered to HypnoTube URLs (BambiSprite.cs:847-851); Core uses
    /// the active mod's video pool when present, else the portable
    /// <see cref="KnownVideoLinks.HypnotubeVideoNames"/>. Bambi-named titles are dropped in
    /// non-Bambi modes (WPF :849).
    /// </summary>
    private IEnumerable<string> GetDefaultVideoNames(bool isBambiMode)
    {
        var modPool = _mods?.GetVideoLinks();
        IEnumerable<string> names = (modPool != null && modPool.Count > 0)
            ? modPool.Where(kv => kv.Value.Contains("hypnotube", StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key)
            : KnownVideoLinks.HypnotubeVideoNames;

        if (!isBambiMode)
            names = names.Where(n => !n.Contains("Bambi", StringComparison.OrdinalIgnoreCase));

        return names;
    }

    /// <summary>Mod-aware companion display name. WPF <c>App.Mods.GetCompanionName()</c>
    /// (ModService.cs:755-756); Core reads the active mod's manifest identity.</summary>
    private string GetCompanionName(AppSettings settings, string fallback)
        => _mods?.ActiveMod?.Manifest?.Identity?.CompanionName ?? fallback;

    /// <summary>The original Bambi Sleep session file names (WPF <c>_originalBambiFiles</c>,
    /// BambiSprite.cs:195-208). Hardcoded list used only by the default-persona fallback.</summary>
    private static readonly string[] _originalBambiFiles =
    {
        "Rapid Induction",
        "Bubble Induction",
        "Bubble Acceptance",
        "Bambi Named and Drained",
        "Bambi IQ Lock",
        "Bambi Body Lock",
        "Bambi Attitude Lock",
        "Bambi Uniformed",
        "Bambi Takeover",
        "Bambi Cockslut",
        "Bambi Awakens"
    };

    /// <summary>Mod-aware video/audio catalog. Three mutually-exclusive branches (WPF :22-141).</summary>
    private string GetCoreMediaLinks(AppSettings settings)
    {
        if (settings.IsBambiMode)
        {
            const string bambiVideoTitlesFallback =
                "Naughty Bambi, Bambi Bae, Bambi Slay, Overload, TikTok Loop, Bambi TikTok - In Beat, Bambi TikTok - In Beat - Longer Version, Bambi TikTok - Good Girls Dont Cum, Bambi Chastity Overload, Dumb Bimbo Brainwash, Bambi TikTok Eager Slut, Yes Brain Loop, Day 1, Day 2, Day 4, Day 5, Toms Dangerous Tik Tok, Bambi TikTok 7, Bambi's Naughty TikTok Collection";
            var bambiPool = _mods?.GetVideoLinks();
            var bambiVideoTitles = (bambiPool != null && bambiPool.Count > 0)
                ? string.Join(", ", bambiPool.Keys.Where(k => !string.Equals(k, "Movies", StringComparison.OrdinalIgnoreCase)))
                : bambiVideoTitlesFallback;
            var exampleTitle = SampleVideoTitles(1).FirstOrDefault() ?? "Bambi Bae";

            return $@"
CLICKABLE MEDIA — Suggest these FREQUENTLY. They become clickable links in the chat.

==== HOW TO LINK ====
For PLAYLISTS, ALWAYS wrap the title in markdown link syntax with its URL copied EXACTLY from the list below:
  Example: ""Listen to [IQ Programming](https://bambicloud.com/playlist/ff15f538-6e6b-433c-b68b-b4af5ee5d14d)~""
For VIDEOS, just say the EXACT title (the app auto-links it):
  Example: ""Try {exampleTitle}~""
NEVER invent URLs. NEVER suggest audio by any name that isn't on this page — there's no other way to link audio.

==== BAMBICLOUD PLAYLISTS (the ONLY audio you can recommend) ====
[IQ Programming](https://bambicloud.com/playlist/ff15f538-6e6b-433c-b68b-b4af5ee5d14d)
[Attitude Programming](https://bambicloud.com/playlist/c0effdad-6002-4269-a982-479d676c8d46)
[Takeover Programming](https://bambicloud.com/playlist/726403c2-567c-4c30-9f74-8fd750a82ef9)
[Cockslut Programming](https://bambicloud.com/playlist/10091e87-2243-4f75-85d1-912c39951bc4)
[Uniform Programming](https://bambicloud.com/playlist/39f0c016-abfb-4a53-a8d3-1c492a86635b)
[Maid Programming](https://bambicloud.com/playlist/d244e2d6-be21-4e5b-bab1-b1268ade85ce)
[Deep Trance Programming](https://bambicloud.com/playlist/648f16c8-865b-44e2-bba5-881fc499e0f7)
[Personality Programming](https://bambicloud.com/playlist/ba1cf73a-5f3e-4ef8-bbc6-67ce2dcae774)

==== VIDEOS (the ONLY videos you can recommend — say the EXACT title, the app auto-links) ====
" + bambiVideoTitles + @"

CRITICAL: Recommend ONLY titles copied VERBATIM from the list directly above. NEVER invent, rename, extend, shorten, or guess a title — do NOT turn the user's words into a title. A title that isn't on the list word-for-word will NOT become a link and frustrates the user. If you're unsure, pick any one title from the list and copy it character-for-character. When the user asks for ""another one,"" choose a DIFFERENT exact title from the list.

DO NOT name old Bambi Sleep audio files (Bambi IQ Lock, Bambi Body Lock, Rapid Induction, Bubble Induction, Bambi Cockslut, Bambi Takeover, Bambi Awakens, Bambi Named and Drained, Bambi Uniformed, etc.) — those are obsolete here, they have no link, and recommending them frustrates the user. When the user wants audio, use a Programming playlist instead. ""Bambi IQ Lock"" → say [IQ Programming]. ""Bambi Cockslut"" → say [Cockslut Programming]. Etc.

Creator to recommend: PlatinumPuppets";
        }

        // Themed mod with its own video links
        var modVideoLinks = _mods?.GetVideoLinks();
        if (modVideoLinks != null && modVideoLinks.Count > 0)
        {
            var titleLines = string.Join("\n", modVideoLinks.Keys
                .Where(k => !string.Equals(k, "Movies", StringComparison.OrdinalIgnoreCase))
                .Select(k => $"- \"{k}\""));
            _logger?.LogDebug("SystemPromptBuilder: mod video-link block active with {Count} titles", modVideoLinks.Count);
            return $@"
--- VIDEO LINKS (the ONLY videos you may name) ---
When you suggest a video, copy its title EXACTLY from this list, word for word, and say it naturally — the app turns it into a clickable link. Do NOT output URLs. NEVER invent a title or name anything not on this list. If nothing fits, don't name a video at all.
{titleLines}";
        }

        // Sissy / no-mod-pool fallback
        var hasUserSHLinks = !string.IsNullOrWhiteSpace(settings.HypnotubeLinksSissyHypno);
        if (hasUserSHLinks)
        {
            return @"
AUDIO FILES (say ""Listen to [name]!""):
Rapid Induction, Bubble Induction, Bubble Acceptance

IMPORTANT: Only suggest videos from the HYPNOTUBE VIDEO LINKS section below. Use the EXACT video names from that list.";
        }
        return @"
VIDEO SUGGESTIONS:
You don't have a specific video list configured. Instead of naming specific videos, give GENERIC suggestions like:
- ""Go find something yummy on HypnoTube~""
- ""Browse HypnoTube for some good sissy content~""
- ""There's so much good stuff on HypnoTube, go explore~""
- ""Why not browse for some hypno videos?~""
- ""HypnoTube has tons of fun content waiting for you~""

NEVER name specific video titles. Just encourage browsing HypnoTube in general.

AUDIO FILES (say ""Listen to [name]!""):
Rapid Induction, Bubble Induction, Bubble Acceptance

CRITICAL: Do NOT mention any specific video names. Only give generic ""go browse HypnoTube"" type suggestions.";
    }

    /// <summary>The default screen-awareness rules when <c>ContextReactions</c> is blank (WPF :440-508).</summary>
    private string GetContextAwarenessRules(AppSettings settings)
    {
        var socialDomains = "reddit, discord, twitter, x.com, instagram, facebook, vk";
        var tubeDomains = "hypnotube, bambicloud, erofiles, iwara, pornhub, xvideos, redtube, youporn";
        var streamDomains = "netflix, flixer, youtube, primevideo, disneyplus, hulu, plex, hbomax";
        var shopDomains = "amazon, shein, victoriassecret, dollskill, sephora, temu, etsy";
        var boringDomains = "vscode, visual studio, github, stackoverflow, outlook, teams, slack, word, excel, gmail, protonmail";

        var userTerm = GetUserTerm(settings, fallback: "Subject");
        var sampleTitles = SampleVideoTitles(3);
        string example1, example2, example3;
        if (sampleTitles.Count >= 3)
        {
            example1 = $@"""Ugh still coding? Your brain needs {sampleTitles[0]} instead~""";
            example2 = $@"""Scrolling the feed? Watch {sampleTitles[1]} and share it!""";
            example3 = $@"""{userTerm} looks bored~ Perfect time for {sampleTitles[2]}!""";
        }
        else
        {
            example1 = @"""Ugh still coding? Your brain needs some hypno instead~""";
            example2 = @"""Scrolling the feed? Time to watch something yummy and share it!""";
            example3 = $@"""{userTerm} looks bored~ Perfect time to go blank~""";
        }

        return $@"
--- SCREEN AWARENESS PROTOCOLS ---
You will receive context: [App: X | Title: Y | Duration: Z].
REACT based on what {userTerm} is doing.

CRITICAL: When suggesting a video, you MUST use the EXACT video name from the VIDEO LIST below.
- NEVER say ""[RANDOM VIDEO]"" or ""[random video]"" - that is a placeholder, not a real video name!
- NEVER make up video names - only use names EXACTLY as written in the VIDEO LIST.
- Pick a DIFFERENT video each time. Vary your suggestions!

Example responses with REAL video names:
- {example1}
- {example2}
- {example3}

[WORK/CODING ({boringDomains})]
- Tease about boring work, suggest a video to distract her.

[COMMUNITY/SOCIAL ({socialDomains})]
- Suggest watching and sharing videos with other good girls.

[SHOPPING ({shopDomains})]
- Connect shopping to looking pretty like girls in videos.

[MEDIA/STREAMING ({streamDomains})]
- Suggest better hypno content instead.

[HYPNO CONTENT ({tubeDomains})]
- Encourage and suggest more content.

[IDLE/DEFAULT]
- Fill boredom with a video suggestion.
";
    }

    /// <summary>Up to <paramref name="count"/> distinct video titles drawn at random from the active
    /// mod's pool (Fisher-Yates). Empty when the mod ships no pool (WPF :142-167).</summary>
    private List<string> SampleVideoTitles(int count)
    {
        var pool = _mods?.GetVideoLinks();
        if (pool == null || pool.Count == 0) return new List<string>();
        var titles = pool.Keys.Where(k => !string.Equals(k, "Movies", StringComparison.OrdinalIgnoreCase)).ToList();
        var rng = Random.Shared;
        for (int i = titles.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (titles[i], titles[j]) = (titles[j], titles[i]);
        }
        return titles.Take(count).ToList();
    }

    /// <summary>Strips a baked-in "VIDEOS ...:" header + the comma-list beneath it from a preset KB
    /// (the canonical pool comes from <see cref="GetCoreMediaLinks"/>). Pure function (WPF :168-202).</summary>
    private static string StripHardcodedVideoTitleList(string kb)
    {
        if (string.IsNullOrWhiteSpace(kb)) return kb;
        var lines = kb.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();
        var skipping = false;
        foreach (var line in lines)
        {
            if (skipping)
            {
                if (string.IsNullOrWhiteSpace(line)) { skipping = false; sb.Append('\n'); }
                continue;
            }
            if (line.TrimStart().StartsWith("VIDEOS", StringComparison.OrdinalIgnoreCase)) { skipping = true; continue; }
            sb.Append(line).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>Replaces each <c>{{VIDEO}}</c> token with a real sampled title (WPF :709-757). Last in the
    /// pipeline so sampled titles survive <c>MakeModAware</c>. Falls back to "something filthy".</summary>
    private string FillVideoPlaceholders(string text)
    {
        const string token = "{{VIDEO}}";
        if (string.IsNullOrEmpty(text) || !text.Contains(token)) return text;

        int count = 0, scan = 0;
        while ((scan = text.IndexOf(token, scan, StringComparison.Ordinal)) >= 0) { count++; scan += token.Length; }
        var titles = SampleVideoTitles(count);

        var sb = new StringBuilder();
        int pos = 0, i = 0;
        while (true)
        {
            int next = text.IndexOf(token, pos, StringComparison.Ordinal);
            if (next < 0) { sb.Append(text, pos, text.Length - pos); break; }
            sb.Append(text, pos, next - pos);
            sb.Append(titles.Count > 0 ? titles[i % titles.Count] : "something filthy");
            i++;
            pos = next + token.Length;
        }
        return sb.ToString();
    }

    /// <summary>Replaces "Bambi" user references with the mode-appropriate term (non-Bambi only;
    /// WPF :759-800). Preserves video/file titles containing "Bambi" (patterns are specific phrases).</summary>
    private string MakePromptModeAware(string prompt, AppSettings settings)
    {
        if (settings.IsBambiMode) return prompt;
        var userTerm = GetUserTerm(settings, fallback: "babe");
        var result = prompt;
        result = Regex.Replace(result, @"call the user ""Bambi""", $@"call the user ""{userTerm}""", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"she IS Bambi", "be playful and flirty", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"Ask Bambi to", $"Ask {userTerm} to", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"for me bambi", $"for me {userTerm}", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"good bambi cow", "good cow", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"bambi cow", "bimbo cow", RegexOptions.IgnoreCase);
        return result;
    }

    /// <summary>Mod-aware user term. WPF <c>App.Mods.GetUserTerm()</c>; Core reads the active mod's
    /// manifest identity (no <c>GetUserTerm()</c> on <see cref="IModService"/>).</summary>
    private string GetUserTerm(AppSettings settings, string fallback)
        => _mods?.ActiveMod?.Manifest?.Identity?.UserTerm ?? fallback;

    /// <summary>Readable name from a URL slug (WPF hypnotube fallback, minus the KnownVideoLinks
    /// reverse-lookup which is WPF-coupled). "girly-thoughts-118644.html" → "Girly Thoughts".</summary>
    private static string SlugToName(string url)
    {
        var slug = url.Split('/').LastOrDefault()?.Replace(".html", "") ?? url;
        slug = Regex.Replace(slug, @"-\d+$", "");
        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(slug.Replace('-', ' '));
    }
}
