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
/// <item>Hypnotube video links (only when the active mod ships NO video pool; slug-fallback names)</item>
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
/// <para><b>v1 scope (deferred):</b> the community-prompt branch + active-preset lookup +
/// <c>GetDefaultBambiSpritePrompt</c> fallback (WPF branches 1-3 in <c>GetSystemPrompt</c>) are not
/// ported — Core reads <c>CompanionPrompt</c> directly, which is behaviorally identical for the
/// common case. The hypnotube block uses slug-fallback names only (WPF's <c>KnownVideoLinks</c>
/// reverse-lookup is a Window static, WPF-coupled); a reverse-map seam is a follow-up.</para>
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
                // WPF resolves URL->name via AvatarTubeWindow.KnownVideoLinks (a Window static,
                // WPF-coupled). Core port uses the slug-fallback path only (v1 scope). The fallback
                // produces readable names from the URL slug.
                foreach (var rawUrl in hypnotubeLinks.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    sb.AppendLine($"- \"{SlugToName(rawUrl)}\"");
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

        return SafetyComposer.Wrap(prompt);
    }

    // ============================ Helpers (ported verbatim from BambiSprite) ============================

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
