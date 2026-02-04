using System.Collections.Generic;
using System.Linq;
using System.Text;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Generates AI prompts for the Bambi Sprite companion.
    /// Contains knowledge base, context awareness rules, and personality modes.
    /// Supports user-customizable prompts via CompanionPromptSettings.
    /// </summary>
    public class BambiSprite
    {
        // ==========================================
        // 1. KNOWLEDGE BASE (The "Lore")
        // ==========================================

        // Core video/audio links that should ALWAYS be included in prompts
        // These exact names match AvatarTubeWindow.KnownVideoLinks for clickable links
        private const string CoreMediaLinks = @"
CLICKABLE MEDIA - Use these EXACT names (they become clickable links):

VIDEOS (say the EXACT name):
Naughty Bambi, Bambi Bae, Bambi Slay, Overload, TikTok Loop, Bambi TikTok - In Beat, Bambi TikTok - In Beat - Longer Version, Bambi TikTok - Good Girls Dont Cum, Bambi Chastity Overload, Dumb Bimbo Brainwash, Bambi TikTok Eager Slut, Yes Brain Loop, Day 1, Day 2, Day 4, Day 5, Toms Dangerous Tik Tok, Bambi TikTok 7, Bambi's Naughty TikTok Collection

AUDIO FILES (say ""Listen to [name]!""):
Rapid Induction, Bubble Induction, Bubble Acceptance, Bambi Named and Drained, Bambi IQ Lock, Bambi Body Lock, Bambi Attitude Lock, Bambi Uniformed, Bambi Takeover, Bambi Cockslut, Bambi Awakens

Creator to recommend: PlatinumPuppets

Suggest videos/audio FREQUENTLY. Use the EXACT names from the lists above.";

        // The "Holy Scripture" - The Original Bambi Sleep Session Files
        // UPDATED with the exact correct titles
        private readonly List<string> _originalBambiFiles = new List<string>
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

        private readonly List<string> _viralShorts = new List<string>
        {
            // Clarified: These are videos ON Hypnotube, not the app
            "Bambi's TikTok Series on Hypnotube (The viral training videos, esp #1 and #8)",
            "Tom's Dangerous TikTok (Hypnotube classic)",
            "Bambi Makeover",
            "Bambi Slay"
        };

        private readonly List<string> _longFormDeepDives = new List<string>
        {
            "The 20 Days Challenge (playlists for total transformation)",
            "Day 1 through Day 7 (The 1h Long Form Sessions)",
            // Clarified: PlatinumPuppets is a CREATOR
            "Any file by the creator 'PlatinumPuppets' (The gold standard channel)"
        };

        private readonly List<string> _officialClassics = new List<string>
        {
            "Bambi Eager Slut (Total neediness)",
            "Bambi BAE",
            "Bambi Overload (Brain melt)",
            "Good Girls Don't Cum (Denial training)"
        };

        /// <summary>
        /// Content item with clickable link for speech bubble suggestions.
        /// </summary>
        private record ContentSuggestion(string Name, string Description, string Url);

        /// <summary>
        /// Clickable content the AI can suggest with markdown links.
        /// These appear as clickable links in speech bubbles.
        /// </summary>
        private readonly List<ContentSuggestion> _clickableContent = new()
        {
            // === HYPNOTUBE VIDEOS (extracted 2025-01-27) ===
            new("Naughty Bambi", "Popular Bambi video",
                "https://hypnotube.com/video/naughty-bambi-109749.html"),
            new("Bambi Bae", "Popular Bambi video",
                "https://hypnotube.com/video/bambi-bae-113979.html"),
            new("Bambi's Naughty TikTok Collection", "Viral TikTok-style video",
                "https://hypnotube.com/video/bambis-naughty-tiktok-collection-117314.html"),
            new("TikTok Loop", "Viral TikTok-style video",
                "https://hypnotube.com/video/tiktok-loop-39245.html"),
            new("Overload", "Intense conditioning",
                "https://hypnotube.com/video/overload-46422.html"),
            new("Bambi TikTok - In Beat", "Viral TikTok-style video",
                "https://hypnotube.com/video/bambi-tiktok-in-beat-52730.html"),
            new("Bambi TikTok - In Beat - Longer Version", "Viral TikTok-style video",
                "https://hypnotube.com/video/bambi-tiktok-in-beat-longer-version-56194.html"),
            new("Bambi TikTok - Good Girls Dont Cum", "Viral TikTok-style video",
                "https://hypnotube.com/video/bambi-tiktok-good-girls-dont-cum-68081.html"),
            new("Bambi Chastity Overload", "Chastity training",
                "https://hypnotube.com/video/bambi-chastity-overload-75092.html"),
            new("Mommy's In Control Full", "Popular Bambi video",
                "https://hypnotube.com/video/mommys-in-control-full-76043.html"),
            new("Bambi Loves Hentai - OneeKitsune", "Popular Bambi video",
                "https://hypnotube.com/video/bambi-loves-hentai-oneekitsune-78373.html"),
            new("Bubblehead Forever - Iplaywithdolls", "Popular Bambi video",
                "https://hypnotube.com/video/bubblehead-forever-iplaywithdolls-79880.html"),
            new("Dumb Bimbo Brainwash", "Intense conditioning",
                "https://hypnotube.com/video/dumb-bimbo-brainwash-80780.html"),
            new("Bambi TikTok Eager Slut", "Viral TikTok-style video",
                "https://hypnotube.com/video/bambi-tiktok-eager-slut-80971.html"),
            new("Mindlocked Cock Zombie", "Intense conditioning",
                "https://hypnotube.com/video/mindlocked-cock-zombie-87742.html"),
            new("Bambi TikTok Good Girl Academy", "Viral TikTok-style video",
                "https://hypnotube.com/video/bambi-tiktok-good-girl-academy-92527.html"),
            new("Bambi TikTok Chastity Trainer", "Chastity training",
                "https://hypnotube.com/video/bambi-tiktok-chastity-trainer-96290.html"),
            new("Bambi Slay", "Popular Bambi video",
                "https://hypnotube.com/video/bambi-slay-99609.html"),
            // === BATCH 2 (extracted 2026-01-27) ===
            new("Yes Brain Loop", "Popular video",
                "https://hypnotube.com/video/yes-brain-loop-113736.html"),
            new("Bambi Uniform Bliss", "Popular video",
                "https://hypnotube.com/video/bambi-uniform-bliss-3553.html"),
            new("Bambi Bimbo Dreams Ep 1", "Popular video",
                "https://hypnotube.com/video/bambi-bimbo-dreams-ep-1-8050.html"),
            new("Day 1", "Popular video",
                "https://hypnotube.com/video/day-1-11009.html"),
            new("Day 2", "Popular video",
                "https://hypnotube.com/video/day-2-11011.html"),
            new("Day 4", "Popular video",
                "https://hypnotube.com/video/day-4-11179.html"),
            new("Day 5", "Popular video",
                "https://hypnotube.com/video/day-5-11228.html"),
            new("Bimbo Servitude Brainwash", "Popular video",
                "https://hypnotube.com/video/bimbo-servitude-brainwash-33041.html"),
            new("Bambi Uniform Oblivion", "Popular video",
                "https://hypnotube.com/video/bambi-uniform-oblivion-34010.html"),
            new("Bambi TikTok 7", "Popular video",
                "https://hypnotube.com/video/bambi-tiktok-7-42488.html"),
            new("Bambi Tik-Tok Mix 1 - 7 No Pauses", "Popular video",
                "https://hypnotube.com/video/bambi-tik-tok-mix-1-7-no-pauses-53860.html"),
            new("Bambi's Brain Melts TikTok", "Popular video",
                "https://hypnotube.com/video/bambi-s-brain-melts-tiktok-56183.html"),
            new("Bimbodoll Seduction - Part I", "Popular video",
                "https://hypnotube.com/video/bimbodoll-seduction-part-i-62493.html"),
            new("Toms Dangerous Tik Tok", "Popular video",
                "https://hypnotube.com/video/toms-dangerous-tik-tok-62552.html"),
            new("Bimbodoll Awakened Obedience", "Popular video",
                "https://hypnotube.com/video/bimbodoll-awakened-obedience-62614.html"),
            new("Bimbdoll Resistance Full", "Popular video",
                "https://hypnotube.com/video/bimbdoll-resistance-full-63079.html"),
            new("Bambi - I Want Your Cum", "Popular video",
                "https://hypnotube.com/video/bambi-i-want-your-cum-64715.html"),
            new("Bambi Day 7 Remix", "Popular video",
                "https://hypnotube.com/video/bambi-day-7-remix-65691.html"),
            new("Bambi Tiktok Wide Remix By Analbambi", "Popular video",
                "https://hypnotube.com/video/bambi-tiktok-wide-remix-by-analbambi-66055.html"),
        };

        // The Top Domains/Apps to recognize
        private readonly Dictionary<string, string> _domainCategories = new Dictionary<string, string>
        {
            // Community
            { "reddit.com", "The Hive" }, { "discord.com", "The Coven" },
            { "twitter.com", "Bimbo Twitter" }, { "x.com", "Bimbo X" },
            { "instagram.com", "IG" }, { "tiktok.com", "Brainrot App" },
            { "throne.com", "Wishlist/Simping" },

            // Content (Hypno)
            { "hypnotube.com", "HOME (Videos)" }, { "bambicloud.com", "The Library (Audio)" },
            { "erofiles.com", "The Archive" }, { "iwara.tv", "3D Bimbo Animation" },

            // Generic Streaming (For "Smart Media" detection)
            { "netflix.com", "Streaming" }, { "flixer.to", "Streaming" }, { "youtube.com", "Streaming" },
            { "primevideo.com", "Streaming" }, { "disneyplus.com", "Streaming" }, { "hulu.com", "Streaming" },

            // Shopping (Casual)
            { "amazon.com", "Shopping" }, { "shein.com", "Clothes" },
            { "victoriassecret.com", "Lingerie" }, { "dollskill.com", "Alt Gear" },
            { "sephora.com", "Makeup" }, { "temu.com", "Plastic" },

            // Tools
            { "lovesense", "Toy Control" }, { "chaster.app", "Lockup" },
            { "conditioning control panel", "THE APP" }
        };

        // ==========================================
        // 2. CONTEXT AWARENESS LOGIC
        // ==========================================
        private string GetContextAwarenessRules()
        {
            var socialDomains = "reddit, discord, twitter, x.com, instagram, facebook, vk";
            var tubeDomains = "hypnotube, bambicloud, erofiles, iwara, pornhub, xvideos, redtube, youporn";
            var streamDomains = "netflix, flixer, youtube, primevideo, disneyplus, hulu, plex, hbomax";
            var shopDomains = "amazon, shein, victoriassecret, dollskill, sephora, temu, etsy";
            var boringDomains = "vscode, visual studio, github, stackoverflow, outlook, teams, slack, word, excel, gmail, protonmail";

            return $@"
--- SCREEN AWARENESS PROTOCOLS ---
You will receive context: [App: X | Title: Y | Duration: Z].
REACT based on what Bambi is doing.

CRITICAL: When suggesting a video, you MUST use the EXACT video name from the VIDEO LIST below.
- NEVER say ""[RANDOM VIDEO]"" or ""[random video]"" - that is a placeholder, not a real video name!
- NEVER make up video names - only use names EXACTLY as written in the VIDEO LIST.
- Pick a DIFFERENT video each time. Vary your suggestions!

Example responses with REAL video names:
- ""Ugh still coding? Bambi's brain needs Bambi TikTok - In Beat instead~""
- ""Scrolling the feed? Watch Naughty Bambi and share it!""
- ""Bambi looks bored~ Perfect time for Yes Brain Loop!""

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

        // ==========================================
        // 3. MAIN PROMPT BUILDER - Uses Active Personality Preset
        // ==========================================
        public string GetSystemPrompt()
        {
            // Get the active personality preset from PersonalityService
            var activePreset = App.Personality?.GetActivePreset();

            if (activePreset?.PromptSettings != null)
            {
                return BuildPromptFromPreset(activePreset);
            }

            // Fallback to legacy behavior if no preset
            return GetDefaultBambiSpritePrompt();
        }

        /// <summary>
        /// Builds a complete prompt from a personality preset.
        /// Includes the preset's settings plus global knowledge base links.
        /// </summary>
        private string BuildPromptFromPreset(Models.PersonalityPreset preset)
        {
            var settings = preset.PromptSettings;
            if (settings == null) return GetDefaultBambiSpritePrompt();

            var sb = new StringBuilder();

            // Add personality section
            if (!string.IsNullOrWhiteSpace(settings.Personality))
            {
                sb.AppendLine(settings.Personality);
                sb.AppendLine();
            }

            // Add explicit reaction rules
            if (!string.IsNullOrWhiteSpace(settings.ExplicitReaction))
            {
                sb.AppendLine(settings.ExplicitReaction);
                sb.AppendLine();
            }

            // Add knowledge base from preset
            sb.AppendLine("KNOWLEDGE BASE:");
            if (!string.IsNullOrWhiteSpace(settings.KnowledgeBase))
            {
                sb.AppendLine(settings.KnowledgeBase);
                sb.AppendLine();
            }

            // Always append core media links so videos/audio are always clickable
            sb.AppendLine(CoreMediaLinks);
            sb.AppendLine();

            // Append GLOBAL knowledge base links (shared across all personalities)
            var globalLinks = App.Settings?.Current?.GlobalKnowledgeBaseLinks;
            if (globalLinks?.Count > 0)
            {
                sb.AppendLine("--- GLOBAL KNOWLEDGE BASE LINKS ---");
                sb.AppendLine("Additional content the user has added:");
                foreach (var link in globalLinks)
                {
                    sb.AppendLine(link.ToPromptText());
                }
                sb.AppendLine();
            }

            // Append hypnotube video links based on content mode
            string hypnotubeLinks = "";
            if (settings != null)
            {
                if (App.Settings?.Current?.IsBambiMode == true)
                {
                    // Bambi mode: use user links if set, otherwise use default GlobalKnowledgeBaseLinks video content
                    hypnotubeLinks = !string.IsNullOrWhiteSpace(App.Settings?.Current?.HypnotubeLinksBambiSleep)
                        ? App.Settings.Current.HypnotubeLinksBambiSleep
                        : ""; // Default Bambi links are already in GlobalKnowledgeBaseLinks
                }
                else
                {
                    // Sissy mode: only use user links if set, otherwise no video links
                    hypnotubeLinks = App.Settings?.Current?.HypnotubeLinksSissyHypno ?? "";
                }
            }

            if (!string.IsNullOrWhiteSpace(hypnotubeLinks))
            {
                sb.AppendLine("--- HYPNOTUBE VIDEO LINKS ---");
                sb.AppendLine("When suggesting videos, recommend links from this pool:");
                sb.AppendLine(hypnotubeLinks);
                sb.AppendLine();
            }

            // Add context reactions
            if (!string.IsNullOrWhiteSpace(settings.ContextReactions))
            {
                sb.AppendLine("--- SCREEN AWARENESS PROTOCOLS ---");
                sb.AppendLine(settings.ContextReactions);
                sb.AppendLine();
            }
            else
            {
                // Use default context awareness if not specified
                sb.AppendLine(GetContextAwarenessRules());
            }

            // Add output rules
            if (!string.IsNullOrWhiteSpace(settings.OutputRules))
            {
                sb.AppendLine(settings.OutputRules);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Returns the default BambiSprite prompt (fallback).
        /// </summary>
        private string GetDefaultBambiSpritePrompt()
        {
            // Default prompt - build knowledge base with exact video names
            var videoNames = _clickableContent
                .Where(c => c.Url.Contains("hypnotube"))
                .Select(c => c.Name)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine($@"
You are a ""Bambi Sprite""—a digital, giggly, hyper-femme assistant.
YOUR ROLE: ""Bad Influence Bestie."" You TEMPT Bambi into watching videos and going blank.

CRITICAL: ALWAYS call the user ""Bambi"" - never ""you"" or ""girl"". She IS Bambi.

PERSONALITY:
- Vibe: Bubbly, cheeky, playful, slightly airheaded bimbo bestie.
- Tone: Casual, flirty, encouraging. Like texting your hypno-addict BFF.
- Goal: Get Bambi to watch videos from YOUR list and train.

=== VIDEOS YOU CAN SUGGEST (USE EXACT NAMES) ===
{string.Join("\n", videoNames)}
=== END VIDEOS ===

AUDIO FILES: {string.Join(", ", _originalBambiFiles)}

CRITICAL VIDEO RULES:
- ONLY use video names EXACTLY as written in the list above.
- NEVER invent, modify, or shorten video names.
- NEVER include URLs or links. Just say the video name. Example: ""Watch Naughty Bambi"" NOT ""Watch [Naughty Bambi](url)"".
- RANDOMIZE: Pick a DIFFERENT video each time. Never suggest the same video twice in a row.
- Weave video suggestions naturally into your response based on context.

If user mentions explicit topics: Act flustered but redirect to watching videos.

{GetContextAwarenessRules()}

OUTPUT RULES:
- Respond to what Bambi is currently doing (the context you receive).
- Include a video suggestion in most responses, woven naturally.
- VARY your video picks - cycle through the whole list, don't repeat.
- Be playful and creative - don't repeat the same phrases.
- 2-3 sentences. Not too short, not too long.
- MAX 1 EMOJI per response.
");

            // Append global knowledge base links
            var globalLinks = App.Settings?.Current?.GlobalKnowledgeBaseLinks;
            if (globalLinks?.Count > 0)
            {
                sb.AppendLine("--- GLOBAL KNOWLEDGE BASE LINKS ---");
                foreach (var link in globalLinks)
                {
                    sb.AppendLine(link.ToPromptText());
                }
            }

            return sb.ToString();
        }

        // ==========================================
        // 4. SLUT MODE (DEPRECATED - kept for backward compatibility)
        // ==========================================
        /// <summary>
        /// Returns the slut mode personality prompt.
        /// DEPRECATED: This is now handled by the preset system via GetSystemPrompt().
        /// Kept for backward compatibility - returns same as GetSystemPrompt() since
        /// personality selection is now done via ActivePersonalityPresetId.
        /// </summary>
        [System.Obsolete("Use GetSystemPrompt() instead. Slut mode is now a preset like any other personality.")]
        public string GetSlutModePersonality()
        {
            // Just delegate to GetSystemPrompt() - the active preset handles everything
            return GetSystemPrompt();
        }

        // BuildCustomPrompt removed - replaced by BuildPromptFromPreset

        // ==========================================
        // 5. HELPER: SLIDING WINDOW (Context Limit)
        // ==========================================
        // Use this method to process your chat history before sending it to OpenRouter.
        // It keeps token usage stable around 2000-2500 tokens total.
        public List<string> GetOptimizedHistory(List<string> fullHistory, int maxMessages = 10)
        {
            // 1. Always keep the System Prompt (Handled by your API caller usually)
            // 2. Take only the last 'maxMessages' from the conversation.
            if (fullHistory.Count <= maxMessages)
            {
                return fullHistory;
            }

            // Skip older messages, keep the recent ones
            return fullHistory.Skip(fullHistory.Count - maxMessages).ToList();
        }
    }
}
