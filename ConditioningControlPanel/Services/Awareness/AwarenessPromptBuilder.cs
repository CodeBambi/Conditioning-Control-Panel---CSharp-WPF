using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Companion.Brain;
using ConditioningControlPanel.Services.Moderation;

namespace ConditioningControlPanel.Services.Awareness
{
    /// <summary>
    /// One assembled awareness request: the byte-stable system prompt, the per-call frame message, and
    /// the message array to hand to <see cref="IAiService.SendAsync"/>.
    /// </summary>
    /// <param name="CardKey">Which angle deck was used. Log-safe (already an id).</param>
    /// <param name="CardIds">The cards actually rendered, in order. Log-safe.</param>
    /// <param name="AuthoredTokens">
    /// Approximate tokens of the AWARENESS-authored zones only — persona digest, angle cards, output
    /// contract, frame and tier instruction. This is the number doc 02 §3.1 budgets at 700-900; it
    /// deliberately excludes the constitutional safety block, which is fixed, identical on every LLM
    /// path in the app, and sits at the very head of the prefix where providers cache it.
    /// </param>
    /// <param name="TotalTokens">
    /// Approximate tokens actually billed on a cold (uncached) call — the authored zones PLUS the
    /// safety preamble and floor, which together are the larger half. This lands around twice
    /// <paramref name="AuthoredTokens"/>, so it is this number and not the doc's budget that a cost
    /// model should be built on until a provider is measured caching the prefix.
    /// </param>
    public sealed record AwarenessPrompt(
        string SystemPrompt,
        string FrameMessage,
        IReadOnlyList<ChatMessage> Messages,
        string CardKey,
        IReadOnlyList<string> CardIds,
        RarityTier Tier,
        int AuthoredTokens,
        int TotalTokens)
    {
        /// <summary>The <c>[AWARE]</c> line for the prompt half of a reaction. Ids only, never prompt text.</summary>
        public string LogLine => string.Create(CultureInfo.InvariantCulture,
            $"[AWARE] prompt key={AwarenessText.SanitizeId(CardKey)} cards={string.Join('+', CardIds)} " +
            $"tier={Tier} authored_tok~{AuthoredTokens} total_tok~{TotalTokens}");
    }

    /// <summary>
    /// The dedicated reaction prompt (doc 02 §3.1; MASTER-SCOPE reconciliation 3 — this is the
    /// <see cref="AiPurpose.Reaction"/> variant of Train 1's two-zone scheme). It replaces the reuse of
    /// the full companion system prompt, which sent thousands of tokens of personality, knowledge base,
    /// video lists and quiz context to produce one throwaway line (doc 02 §1.7).
    ///
    /// <para><b>Zone 1 — the stable prefix (the system message).</b> Safety preamble, the persona
    /// digest for the active mod, two or three angle cards chosen DETERMINISTICALLY for this card key
    /// and this app-session, the output contract, and the safety floor. It is byte-identical for every
    /// frame that resolves to the same key inside one launch, which is the whole point: providers
    /// discount the longest common prefix of a request, so a rotating prefix costs full price every
    /// time. Rotation happens per launch, not per call.</para>
    ///
    /// <para><b>Zone 2 — the dynamic tail (the frame message).</b> The sanitised projection
    /// (<see cref="AwarenessProjection"/>), the ban-list rule that points at the <c>recent</c> array
    /// inside it, and the tier instruction last so it is the most recent thing the model read.</para>
    ///
    /// <para><b>Untrusted text.</b> Angle cards and persona digests can be mod-supplied
    /// (<see cref="AwarenessAngleCards"/>), so they are length-capped, control-character-stripped and
    /// role-marker-filtered before they get here, at most
    /// <see cref="MaxCardsPerCall"/> are rendered, and <see cref="SafetyComposer.Floor"/> is composed
    /// AFTER all of them. Nothing a card can say sits between the floor and the model.</para>
    ///
    /// <para><b>What it retains.</b> One in-memory cache of assembled prefixes, keyed by deck, card key
    /// and persona. Its contents are authored prose (angle cards, voice cards) and nothing derived from
    /// the user — no app ids, no titles, no counts, no delivered lines — so a wipe or a per-app forget
    /// has nothing to reach in here. Nothing is written to disk by this class at all.</para>
    ///
    /// <para><b>What is deliberately absent:</b> <c>BambiSprite.GetContextAwarenessRules</c> — the
    /// "SCREEN AWARENESS PROTOCOLS" whose every branch ends in "plug a video from the VIDEO LIST"
    /// (doc 02 §1.3). That method is untouched and still serves the legacy path; v2 simply does not
    /// call it, and the output contract below forbids plugs outright unless an angle card licenses
    /// one.</para>
    /// </summary>
    public sealed class AwarenessPromptBuilder
    {
        // ===================== budget =====================

        /// <summary>Angle cards rendered per call (doc 02 §3.1: "rotates 2-3 per call").</summary>
        public const int MaxCardsPerCall = 3;

        /// <summary>
        /// The doc 02 §3.1 target for the awareness-authored zones, in tokens. Not a hard clamp: it is
        /// the number the tests assert against so that an edit to the contract or a fat mod digest
        /// shows up as a failing test rather than as a quietly larger bill.
        /// </summary>
        public const int AuthoredTokenTarget = 900;

        /// <summary>Response cap for a reaction. ~40 tokens out is the doc 02 §3.1 target; 60 is headroom.</summary>
        public const int ResponseMaxTokens = 60;

        /// <summary>Marks the boundary between the cached zone and the per-call zone (same word as Train 1's tail).</summary>
        public const string TailHeader = "--- RIGHT NOW ---";

        // ===================== authored prompt text =====================

        /// <summary>
        /// The output contract (doc 02 §3.1 item 5). Every sentence here is enforced or honoured
        /// somewhere: the 140-character cap and the <c>[PASS]</c> / <c>CALLBACK:</c> forms are parsed
        /// out by <see cref="AwarenessReactionService"/>, and the "never name a website / track /
        /// document" rule matches what the projection actually contains — the model is not being asked
        /// to keep a secret it was told.
        /// </summary>
        public const string OutputContract =
@"Write ONE line, at most 140 characters, in your own voice, about the moment below.
Use only the facts and numbers given. Never invent a number, never imply you know anything that is not there.
You cannot see websites, page text, documents, messages, tracks or people - only what is below. Never name one.
Never recommend, plug or link a video, file or playlist unless an angle above licenses it.
Never mention screens, watching, tracking or how you know. Never explain the joke. Never greet, never offer help, never open with ""I see"".
Nothing worth saying? Answer exactly [PASS]. Passing is a good answer and costs you nothing.
You may add ONE line beginning ""CALLBACK:"" - the same beat in past tense, for if they have already moved on.
Output only those lines: no quotes, no name label, no brackets, no stage directions.";

        /// <summary>Points the model at the <c>recent</c> array inside the frame (doc 02 §3.1 item 4).</summary>
        public const string BanListRule =
            "\"recent\" is what you already said. Never reuse a structure, an opening or a punchline from it - " +
            "if your idea rhymes with one, answer [PASS].";

        /// <summary>Header above the persona digest.</summary>
        public const string PersonaHeader = "--- WHO YOU ARE ---";

        /// <summary>Header above the angle cards.</summary>
        public const string AnglesHeader = "--- YOUR ANGLES ---";

        /// <summary>The one line that makes cards read as shapes rather than as scripts.</summary>
        public const string AnglesLead =
            "Pick ONE of these and commit to it. They are shapes, not lines: never echo this wording back.";

        /// <summary>Header above the output contract.</summary>
        public const string ContractHeader = "--- HOW TO ANSWER ---";

        /// <summary>
        /// Appended to a plug-licensed card. It licenses an OFFER, never a TITLE.
        ///
        /// <para>Unlike the legacy companion prompt, this one deliberately carries no media catalogue —
        /// the projection is app ids and bucketed numbers — so a licence to "name one thing of yours"
        /// is a licence to invent a filename the user does not have, in direct contradiction of the
        /// contract's "never imply you know anything that is not there". What she can honestly offer is
        /// herself, or something the app does.</para>
        /// </summary>
        public const string PlugLicenseNote =
            "(this angle, and only this angle, may make an offer - of yourself, or of something this app does. " +
            "You cannot see their library: never name a title.)";

        // ===================== state =====================

        private static readonly int ProcessSalt = Environment.TickCount;

        private readonly Func<string> _modId;
        private readonly Func<AwarenessAngleDeck> _deck;
        private readonly Func<string?> _presetPersonality;
        private readonly int _salt;

        private readonly object _cacheGate = new();
        private readonly Dictionary<string, string> _prefixCache = new(StringComparer.Ordinal);

        /// <param name="modId">Active mod id. Defaults to <c>App.Mods.ActiveModId</c>.</param>
        /// <param name="deck">Angle deck source. Defaults to the loaded <see cref="AwarenessAngleCards.Deck"/>.</param>
        /// <param name="presetPersonality">
        /// Raw personality text for a mod the deck has no authored digest for, distilled into a
        /// fallback voice card (doc 02 §3.1 item 1). Defaults to the live personality preset.
        /// </param>
        /// <param name="sessionSalt">
        /// Rotation seed. Fixed per process by default, which is what makes the prefix byte-stable
        /// within a launch and different between launches — the same trick the stable companion prompt
        /// uses for its worked examples.
        /// </param>
        public AwarenessPromptBuilder(
            Func<string>? modId = null,
            Func<AwarenessAngleDeck>? deck = null,
            Func<string?>? presetPersonality = null,
            int? sessionSalt = null)
        {
            _modId = modId ?? DefaultModId;
            _deck = deck ?? (() => AwarenessAngleCards.Deck);
            _presetPersonality = presetPersonality ?? DefaultPresetPersonality;
            _salt = sessionSalt ?? ProcessSalt;
        }

        /// <summary>
        /// Builds the request for one cut frame.
        /// </summary>
        /// <param name="frame">The frame. Null yields a prompt over an empty projection rather than throwing.</param>
        /// <param name="local">
        /// True for the machine-local Ollama transport, which receives the fuller projection. One
        /// codepath, two projections (doc 02 §6.2).
        /// </param>
        public AwarenessPrompt Build(ContextFrame? frame, bool local = false)
        {
            var deck = _deck() ?? AwarenessAngleCards.Deck;
            var key = deck.ResolveKey(frame?.AppId, frame?.AppCluster,
                frame?.Category ?? ActivityCategory.Unknown);

            var cards = SelectCards(deck, key);
            var persona = ResolvePersona(deck, _modId());
            var tier = frame?.Tier ?? RarityTier.Uncommon;

            var prefix = GetOrBuildPrefix(deck, key, persona, cards);
            var tail = BuildTail(frame, local, tier);

            var messages = new List<ChatMessage>(2)
            {
                ChatMessage.System(prefix),
                ChatMessage.User(tail)
            };

            var cardIds = new List<string>(cards.Count);
            foreach (var card in cards) cardIds.Add(card.Id);

            int authored = ChatSession.ApproxTokens(persona)
                           + ChatSession.ApproxTokens(RenderCards(cards))
                           + ChatSession.ApproxTokens(OutputContract)
                           + ChatSession.ApproxTokens(tail);
            int total = ChatSession.ApproxTokens(prefix) + ChatSession.ApproxTokens(tail);

            return new AwarenessPrompt(prefix, tail, messages, key, cardIds, tier, authored, total);
        }

        // ===================== zone 1 =====================

        private string GetOrBuildPrefix(
            AwarenessAngleDeck deck, string key, string persona, IReadOnlyList<AwarenessAngleCard> cards)
        {
            // The persona is part of the identity, not just the mod id: two users on the same mod can
            // have different personality text, and a mid-launch edit must not keep serving the old
            // voice card from cache.
            var cacheKey = string.Create(CultureInfo.InvariantCulture,
                $"{deck.Stamp}|{key}|{_salt}|{persona.Length}|{persona.GetHashCode(StringComparison.Ordinal)}");

            lock (_cacheGate)
            {
                if (_prefixCache.TryGetValue(cacheKey, out var cached)) return cached;
            }

            var built = BuildPrefix(persona, cards);

            lock (_cacheGate)
            {
                // Bounded: one entry per (deck, key, persona). A pathological mod deck with 64 keys
                // costs 64 short strings, and the cache is per-builder anyway.
                if (_prefixCache.Count > AwarenessAngleCards.MaxKeys * 2) _prefixCache.Clear();
                _prefixCache[cacheKey] = built;
            }

            return built;
        }

        /// <summary>
        /// Assembles zone 1. Order is load-bearing: the safety preamble first for primacy, then
        /// everything a mod could have authored, then the safety floor LAST so no card text sits
        /// between the floor and the model.
        /// </summary>
        internal static string BuildPrefix(string persona, IReadOnlyList<AwarenessAngleCard> cards)
        {
            var sb = new StringBuilder(2600);
            sb.Append(SafetyComposer.Preamble);
            sb.Append("\n\n").Append(PersonaHeader).Append('\n').Append(persona);
            sb.Append("\n\n").Append(AnglesHeader).Append('\n').Append(AnglesLead).Append('\n')
              .Append(RenderCards(cards));
            sb.Append("\n\n").Append(ContractHeader).Append('\n').Append(OutputContract);
            sb.Append("\n\n").Append(SafetyComposer.Floor);
            return sb.ToString();
        }

        internal static string RenderCards(IReadOnlyList<AwarenessAngleCard> cards)
        {
            if (cards == null || cards.Count == 0) return "";

            var sb = new StringBuilder(cards.Count * 260);
            for (int i = 0; i < cards.Count; i++)
            {
                if (i > 0) sb.Append('\n');
                sb.Append(CultureInfo.InvariantCulture, $"{i + 1}. {cards[i].Bit}");
                if (cards[i].AllowsPlug) sb.Append(' ').Append(PlugLicenseNote);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Picks <see cref="MaxCardsPerCall"/> cards deterministically from the key's pool: a stable
        /// hash of the key and the session salt chooses the start index and the window wraps. Same key
        /// plus same launch always yields the same cards in the same order — which is what keeps the
        /// prefix byte-stable and therefore cacheable at the provider.
        /// </summary>
        internal IReadOnlyList<AwarenessAngleCard> SelectCards(AwarenessAngleDeck deck, string key)
        {
            var pool = deck.CardsFor(key);
            if (pool.Count == 0) return Array.Empty<AwarenessAngleCard>();

            int take = Math.Min(MaxCardsPerCall, pool.Count);
            int start = (int)((uint)StableHash(key, _salt) % (uint)pool.Count);

            var chosen = new List<AwarenessAngleCard>(take);
            for (int i = 0; i < take; i++)
            {
                var card = pool[(start + i) % pool.Count];
                if (card.IsUsable) chosen.Add(card);
            }
            return chosen;
        }

        /// <summary>
        /// FNV-1a over the key and the salt. A plain <c>string.GetHashCode</c> is randomised per
        /// process, which would still be stable within a launch — but this is also the value the tests
        /// pin, and a test that cannot predict the rotation cannot prove the rotation is deterministic.
        /// </summary>
        internal static int StableHash(string key, int salt)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (var ch in key ?? string.Empty)
                {
                    hash ^= ch;
                    hash *= 16777619;
                }
                for (int shift = 0; shift < 32; shift += 8)
                {
                    hash ^= (uint)((salt >> shift) & 0xFF);
                    hash *= 16777619;
                }
                return (int)(hash & 0x7FFFFFFF);
            }
        }

        /// <summary>
        /// The persona digest: the deck's authored card for this mod when there is one, otherwise a
        /// distillation of the live personality settings, otherwise the deck's default card. A custom
        /// mod therefore still gets a voice rather than the stock one (doc 02 §3.1 item 1).
        /// </summary>
        internal string ResolvePersona(AwarenessAngleDeck deck, string? modId)
        {
            var id = AwarenessText.SanitizeId(modId);
            if (id != AwarenessText.UnknownId && deck.Personas.TryGetValue(id, out var authored))
                return authored.Digest;

            var distilled = Distil(_presetPersonality());
            if (distilled.Length > 0) return distilled;

            return deck.PersonaFor(null)?.Digest ?? "";
        }

        /// <summary>
        /// Turns a personality prompt wall into a one-paragraph voice card: sanitised the same way a
        /// mod-supplied digest is (it is user- or mod-authored text heading for a system prompt),
        /// collapsed to a single paragraph, and capped.
        /// </summary>
        internal static string Distil(string? personality)
        {
            var clean = AwarenessText.SanitizeCardText(personality, AwarenessAngleCards.MaxDigestLength);
            if (clean.Length == 0) return "";

            var collapsed = System.Text.RegularExpressions.Regex.Replace(clean, @"\s+", " ").Trim();
            if (collapsed.Length > AwarenessAngleCards.MaxDigestLength)
            {
                collapsed = collapsed.Substring(0, AwarenessAngleCards.MaxDigestLength);
                int lastSpace = collapsed.LastIndexOf(' ');
                if (lastSpace > AwarenessAngleCards.MaxDigestLength / 2)
                    collapsed = collapsed.Substring(0, lastSpace);
            }
            return collapsed.TrimEnd();
        }

        // ===================== zone 2 =====================

        /// <summary>
        /// The per-call tail: the projection, the ban-list rule that points into it, and the tier
        /// instruction last. Deliberately carries no authored card text — everything here is either our
        /// own sanitised projection or her own already-moderated lines, so the cache-invalidating half
        /// of the prompt is also the half nothing hostile can reach.
        /// </summary>
        internal static string BuildTail(ContextFrame? frame, bool local, RarityTier tier)
        {
            var projection = local
                ? AwarenessProjection.BuildLocalProjection(frame)
                : AwarenessProjection.BuildCloudProjection(frame);

            var sb = new StringBuilder(768);
            sb.Append(TailHeader).Append('\n');
            sb.Append(projection).Append('\n');
            sb.Append(BanListRule).Append('\n');
            sb.Append(TierInstruction(tier));
            return sb.ToString();
        }

        /// <summary>
        /// What the arbiter decided this line is worth, told to the model so it knows what it is
        /// writing (doc 02 §3.2). <see cref="RarityTier.Legendary"/> is a Train 4 slot and is never
        /// produced by Train 2; its text exists so the ladder is complete rather than so it fires.
        /// </summary>
        public static string TierInstruction(RarityTier tier) => tier switch
        {
            RarityTier.Common =>
                "TIER: Common. A small moment. One light beat, or [PASS] — this one is not worth reaching for.",
            RarityTier.Rare =>
                "TIER: Rare. The numbers above are real history you have been keeping. Land the callback: say the number, let it be the punchline, and never explain how you know it.",
            RarityTier.Legendary =>
                "TIER: Legendary. A running bit is paying off. Escalate it exactly once and then stop.",
            _ =>
                "TIER: Uncommon. Good snapshot, no history worth leaning on. Be specific about what is in front of you and do not pretend to remember anything."
        };

        // ===================== defaults =====================

        private static string DefaultModId()
        {
            try { return App.Mods?.ActiveModId ?? ""; }
            catch { return ""; }
        }

        /// <summary>
        /// The personality text the companion is actually running on, mirroring how
        /// <c>BambiSprite.CaptureFingerprintInputs</c> resolves it: a community prompt when one is
        /// assigned and enabled, otherwise the active preset.
        /// </summary>
        private static string? DefaultPresetPersonality()
        {
            try
            {
                var settings = App.Settings?.Current;
                if (BambiSprite.UsesCustomPrompt(settings)) return settings?.CompanionPrompt?.Personality;
                return App.Personality?.GetActivePreset()?.PromptSettings?.Personality;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("AwarenessPromptBuilder: personality lookup failed: {Error}", ex.Message);
                return null;
            }
        }
    }
}
