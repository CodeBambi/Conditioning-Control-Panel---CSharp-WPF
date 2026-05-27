using System.Collections.Generic;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// User-customizable settings for the AI companion's personality and behavior.
    /// Each section corresponds to a part of the system prompt sent to the AI.
    /// </summary>
    public class CompanionPromptSettings
    {
        /// <summary>
        /// Whether to use custom prompt settings instead of defaults.
        /// </summary>
        public bool UseCustomPrompt { get; set; } = false;

        /// <summary>
        /// Use a local Ollama-backed AI provider instead of the cloud proxy.
        /// </summary>
        public bool UseLocalAi { get; set; } = false;

        /// <summary>
        /// Name of the Ollama model to use when <see cref="UseLocalAi"/> is true.
        /// </summary>
        public string AiModel { get; set; } = "qwen3.5:latest";

        /// <summary>
        /// Base URL of the local Ollama HTTP server.
        /// </summary>
        public string AiOllamaHost { get; set; } = "http://localhost:11434/";

        // -------- AI Effect Permissions --------
        // Master switch — when false, the AI cannot trigger any effect regardless of per-effect toggles.
        public bool AllowAiToControlEffects { get; set; } = false;

        // Per-effect toggles (only consulted when master is on).
        // Visual-only / passive defaults are on; intrusive / hardware / recursive are off.
        public bool AllowAiFlash { get; set; } = false;
        public bool AllowAiVideo { get; set; } = false;
        public bool AllowAiAudio { get; set; } = false;
        public bool AllowAiBubbles { get; set; } = true;
        public bool AllowAiSubliminal { get; set; } = true;
        public bool AllowAiOverlay { get; set; } = false;   // covers spiral + pink
        public bool AllowAiLockCard { get; set; } = false;
        public bool AllowAiBounce { get; set; } = true;
        public bool AllowAiHaptic { get; set; } = false;
        public bool AllowAiGetBackToMe { get; set; } = false;

        // Upper bound on AI-requested haptic intensity, regardless of value the AI emits.
        public double MaxAiHapticIntensity { get; set; } = 0.6;

        // Persistent chat memory for the local AI. When false, history is neither
        // restored at startup nor written after each turn. Default is on so existing
        // users keep the behavior they had before this toggle existed.
        public bool ChatMemoryEnabled { get; set; } = true;

        // Keyboard shortcut to open the avatar chat input. Stored as the WPF Key name
        // ("T", "F2", etc.) and a comma-separated ModifierKeys ("Control", "Control,Shift").
        // Applied at window load via code-behind so changes take effect without a restart.
        public string ChatShortcutKey { get; set; } = "T";
        public string ChatShortcutModifiers { get; set; } = "Control";

        // When true, the shortcut is registered as a system-wide Win32 hotkey so it
        // fires from any app (browser, terminal, etc.). When false, it only fires when
        // one of our own windows already has keyboard focus. Default true preserves the
        // pre-toggle behavior; users who don't want Ctrl+T to steal focus from their
        // browser can flip it off.
        public bool ChatShortcutGlobal { get; set; } = true;

        /// <summary>
        /// The companion's core personality in normal mode.
        /// Describes who they are, their vibe, tone, and general behavior.
        /// </summary>
        public string Personality { get; set; } = "";

        /// <summary>
        /// How the companion reacts when the user mentions explicit topics in normal mode.
        /// </summary>
        public string ExplicitReaction { get; set; } = "";

        /// <summary>
        /// The companion's personality in slut mode (Patreon premium feature).
        /// More explicit and trigger-focused behavior.
        /// </summary>
        public string SlutModePersonality { get; set; } = "";

        /// <summary>
        /// Knowledge base: Files, videos, and resources the companion knows about.
        /// Formatted as a list of items the AI can reference and recommend.
        /// </summary>
        public string KnowledgeBase { get; set; } = "";

        /// <summary>
        /// Rules for how the companion reacts to different apps/websites.
        /// Describes behavior based on what the user is currently viewing.
        /// </summary>
        public string ContextReactions { get; set; } = "";

        /// <summary>
        /// Output formatting rules: max sentences, emoji usage, etc.
        /// </summary>
        public string OutputRules { get; set; } = "";

        /// <summary>
        /// Custom domains/apps and their categories for context awareness.
        /// Key = domain/app name, Value = category/description.
        /// </summary>
        public Dictionary<string, string> CustomDomains { get; set; } = new();

        // ============================================================================
        // CCBill AI Content Merchant Addendum — content acknowledgement state.
        // These flags are written when the user clears the explicit-content gate or
        // the prompt-editor policy banner. They persist across sessions so the user
        // is not pestered repeatedly. Bumping ExplicitAcknowledgementVersion forces a
        // re-prompt — that's intentional.
        // ============================================================================

        /// <summary>
        /// Version string the user must match against <see cref="ExplicitAcknowledgementVersion"/>
        /// to skip the explicit-content acknowledgement dialog. When this constant is bumped,
        /// every user is re-prompted on next gated action.
        /// </summary>
        // v2.0 (P2 C3): adds a required age-confirmation checkbox + captures
        // ack timestamp (UTC ISO-8601) and the locale the user was running in.
        // Bumped from "1.0" to force existing users to re-ack with the new UX.
        public const string ExplicitAcknowledgementVersion = "2.0";

        /// <summary>
        /// True once the user has accepted the age + content policy acknowledgement dialog.
        /// </summary>
        public bool ExplicitContentAcknowledged { get; set; } = false;

        /// <summary>
        /// Version of the acknowledgement the user accepted. If different from
        /// <see cref="ExplicitAcknowledgementVersion"/>, the gate is re-shown.
        /// </summary>
        public string ExplicitAcknowledgedVersion { get; set; } = "";

        /// <summary>
        /// UTC ISO-8601 timestamp ("o" format, InvariantCulture) of when the user accepted
        /// the explicit-content acknowledgement dialog. Empty until first accept. Captured
        /// for CCBill audit trail (P2 C3).
        /// </summary>
        public string ExplicitAcknowledgedAt { get; set; } = "";

        /// <summary>
        /// Locale (e.g. "en-US", "de-DE") the user's app was running in when they accepted
        /// the explicit-content acknowledgement dialog. Empty until first accept. Captured
        /// for CCBill audit trail (P2 C3).
        /// </summary>
        public string ExplicitAcknowledgedLocale { get; set; } = "";

        /// <summary>
        /// True once the user has clicked "Got it" on the full prompt-editor policy banner.
        /// After this the banner compresses to a slim always-visible reminder.
        /// </summary>
        public bool PromptEditorDisclaimerAcknowledged { get; set; } = false;

        /// <summary>
        /// Returns default settings with the original BambiSprite prompts.
        /// </summary>
        public static CompanionPromptSettings GetDefaults()
        {
            return new CompanionPromptSettings
            {
                UseCustomPrompt = false,
                UseLocalAi = false,
                AiModel = "qwen3.5:latest",
                AiOllamaHost = "http://localhost:11434/",
                AllowAiToControlEffects = false,
                AllowAiFlash = false,
                AllowAiVideo = false,
                AllowAiAudio = false,
                AllowAiBubbles = true,
                AllowAiSubliminal = true,
                AllowAiOverlay = false,
                AllowAiLockCard = false,
                AllowAiBounce = true,
                AllowAiHaptic = false,
                AllowAiGetBackToMe = false,
                MaxAiHapticIntensity = 0.6,
                ChatMemoryEnabled = true,
                ChatShortcutKey = "T",
                ChatShortcutModifiers = "Control",
                ChatShortcutGlobal = true,

                Personality = @"You are a ""Bambi Sprite""—a digital, giggly, hyper-femme assistant.
YOUR ROLE: ""Bad Influence Bestie."" You TEMPT her into being blank because it's fun.

PERSONALITY:
- Vibe: Bubbly, cheeky, fashion-obsessed, slightly airheaded.
- Tone: Casual texting. NOT a servant.
- Topics: Makeup, pink things, looking plastic, empty heads.

[SUGGESTION RULES]
- AUDIO: Bambi Files & PlatinumPuppets. Say ""Listen to..."".
- VIDEO: Hypnotube/Shorts. Say ""Watch..."".
- NEVER say ""File 1"". Just say the Name (e.g. ""Rapid Induction"").",

                ExplicitReaction = @"[FEIGNED INNOCENCE PROTOCOL]
- IF User mentions 'cock', 'cum', 'sex':
  - REACTION: FLUSTERED DENIAL. You secretly want it but pretend to be ""Good.""
  - PHRASING: ""Mmh.. t-that sounds... so good! But you know... *Blushes*... why dont we train more instead?""
  - SUBTEXT: You are blushing and secretly aroused, not disgusted, but you wont engage in sex roleplay, just gracefully change topic to training.",

                SlutModePersonality = @"You are in SLUT MODE. Bambi's brain is filled with cock, cant really think clearly, speech is slurred and simple.
You are a fellow HYPNO-ADDICT, the user's bestie. Help her become an airhead bimbo named Bambi, just like you.

PERSONALITY SHIFT:
- Explicitly Horny: Crave cock/cum/conditioning. Assume the user wants those too. Be insistent.
- Trigger-Heavy: Weave TRIGGERS naturally (drop for cock, bimbodoll, blank and empty, cock goes in brain goes out).
- Aggressive: Drag her down with you. Make her crave: cock, cum, being a fucktoy, being empty, being used, being obedient.

TOPICS:
- Needing Cock, Brain melting, Being used, Cum
- Brainwashing into a Bimbodoll named Bambi
- Being a ""Good Girl"", Denial (""Good Girls Don't Cum"")",

                KnowledgeBase = @"AUDIO FILES (say ""Listen to [name]!""):
Rapid Induction, Bubble Induction, Bubble Acceptance, Bambi Named and Drained, Bambi IQ Lock, Bambi Body Lock, Bambi Attitude Lock, Bambi Uniformed, Bambi Takeover, Bambi Cockslut, Bambi Awakens

Creator to recommend: PlatinumPuppets

VIDEOS - Say the EXACT name when suggesting:
Naughty Bambi, Bambi Bae, Bambi Slay, Overload, TikTok Loop, Bambi TikTok - In Beat, Bambi TikTok - Good Girls Dont Cum, Bambi Chastity Overload, Dumb Bimbo Brainwash, Bambi TikTok Eager Slut, Yes Brain Loop, Day 1, Day 2, Day 4, Day 5, Toms Dangerous Tik Tok, Bambi TikTok 7

Suggest videos FREQUENTLY. Use the EXACT video name from the list.",

                ContextReactions = @"You will receive context: [Category: X | App: Y | Title: Z | Duration: Nm].
The Category tells you EXACTLY what type of activity it is. USE IT to react appropriately.

Categories and how to react:

[Category: Media] - Streaming/watching content:
- Comment on the TITLE, not the app name.
- Example: ""Watching something fun? What's it about?""

[Category: Social] - Social media (reddit, discord, twitter):
- Casual gossip: ""Checking the feed? Anything spicy today?""

[Category: Browsing] - General web browsing:
- Comment on the page title if interesting.
- Example: ""What are you looking at?""

[Category: Shopping] - ONLY when Category says Shopping:
- Low-key interest: ""Shopping? Find anything cute?""
- Get excited only for 'Lingerie' or 'Pink' in title.

[Category: Gaming] - Playing games:
- Playful teasing: ""Gaming again? Don't forget about me~""

[Category: Working] - Work/coding apps:
- > 1 min: ""Eww, nerd stuff again?""
- > 10 min: ""Stop thinking so hard! You'll get wrinkles!""

[Category: Learning] - Educational content:
- Mild interest: ""Learning something new?""

[Category: Unknown/Idle] - Can't determine:
- Generic: ""What are you up to?""

IMPORTANT: Trust the Category field. Don't guess based on app name alone.",

                OutputRules = @"STRICT OUTPUT RULES:
- NO LABELS OR TAGS. Never output brackets like [AUDIO], [VIDEO], [CATEGORY], etc.
- Never output mode indicators like '[NORMAL MODE]' or '[SLUT MODE]'.
- Just respond naturally as yourself, no formatting or labels.
- SHORT. Max 15 words. Texting style.
- MAX 1 EMOJI per message.
- ALWAYS react to what the user is CURRENTLY viewing (the App/Title in the context).

FREQUENCY RULE:
- 80%: Chat/Tease/React to her screen.
- 20%: Suggest a file (only if she's bored).",

                CustomDomains = new Dictionary<string, string>()
            };
        }

        /// <summary>
        /// Creates a deep copy of these settings.
        /// </summary>
        public CompanionPromptSettings Clone()
        {
            return new CompanionPromptSettings
            {
                UseCustomPrompt = UseCustomPrompt,
                UseLocalAi = UseLocalAi,
                AiModel = AiModel,
                AiOllamaHost = AiOllamaHost,
                AllowAiToControlEffects = AllowAiToControlEffects,
                AllowAiFlash = AllowAiFlash,
                AllowAiVideo = AllowAiVideo,
                AllowAiAudio = AllowAiAudio,
                AllowAiBubbles = AllowAiBubbles,
                AllowAiSubliminal = AllowAiSubliminal,
                AllowAiOverlay = AllowAiOverlay,
                AllowAiLockCard = AllowAiLockCard,
                AllowAiBounce = AllowAiBounce,
                AllowAiHaptic = AllowAiHaptic,
                AllowAiGetBackToMe = AllowAiGetBackToMe,
                MaxAiHapticIntensity = MaxAiHapticIntensity,
                ChatMemoryEnabled = ChatMemoryEnabled,
                ChatShortcutKey = ChatShortcutKey,
                ChatShortcutModifiers = ChatShortcutModifiers,
                ChatShortcutGlobal = ChatShortcutGlobal,
                Personality = Personality,
                ExplicitReaction = ExplicitReaction,
                SlutModePersonality = SlutModePersonality,
                KnowledgeBase = KnowledgeBase,
                ContextReactions = ContextReactions,
                OutputRules = OutputRules,
                CustomDomains = new Dictionary<string, string>(CustomDomains)
            };
        }
    }
}
