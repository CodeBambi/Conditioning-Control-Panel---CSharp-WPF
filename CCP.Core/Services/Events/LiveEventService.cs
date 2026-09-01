using System;

namespace ConditioningControlPanel.Services.Events
{
    // ============================================================================
    // LIVE EVENTS — the dormant switchboard for world events (The Descent §11/§14).
    //
    // WHAT THIS IS: three values a world event may set, and nothing else. A skin id
    // (themed bubbles), an accent hex (the palette shift) and an additive XP boost.
    // Every Phase-1 "S" item consults this object FIRST and falls straight through
    // when it is dark, which is its state in this build and every build until the
    // server grows an `events[]` block on the quest-definitions channel (§14).
    //
    // WHAT THIS IS NOT: a fetcher. There is deliberately no network code, no timer,
    // no cache and no persistence here. Nothing in the shipped app calls
    // <see cref="Apply"/>. The wire half lands with the event engine (Phase 2); this
    // half exists now so that the five renderers, the palette chain and the XP funnel
    // are already reading through a seam by the time it does — the alternative is
    // touching all of them again on the day an event has to go live.
    //
    // DORMANCY IS THE CONTRACT: with no event applied, <see cref="SkinId"/> and
    // <see cref="AccentHex"/> are null and <see cref="XpBoost"/> is 0.0, so every
    // consumer below is a provable no-op:
    //   • BubbleSkinResolver returns the mod/default sprite exactly as before,
    //   • ModService's FX chain resolves at its old first link,
    //   • SkillTreeService adds +0.0 to the multiplier.
    //
    // SERVER-DRIVEN, NEVER CLIENT-INVENTED: when this is wired, the values arrive
    // from the server's event block. A client that can name its own event skin is a
    // client that can hand itself an XP boost, so nothing here is persisted to
    // settings and nothing reconstructs an event from local state.
    // ============================================================================

    /// <summary>
    /// The active world event's cosmetic + economy overrides. Dark in this build:
    /// nothing calls <see cref="Apply"/>, so every property sits at its dormant
    /// default and every consumer falls through to today's behaviour.
    /// </summary>
    public sealed class LiveEventService
    {
        /// <summary>
        /// The hard ceiling on an event's XP boost, as an additive term on the
        /// multiplier (0.25 = +25%). An event is a nudge, not a new economy: the
        /// funnel already reaches ~6.3x on skills alone, and an uncapped server
        /// number would let a typo mint a level. Clamped on the way in AND read
        /// through <see cref="ClampXpBoost"/> on the way out.
        /// </summary>
        public const double MaxXpBoost = 0.25;

        /// <summary>
        /// Id of the event's bubble skin (e.g. "snowglobe"), or null for no event
        /// skin. Resolved against the sprite pipeline's skin folders; an id with no
        /// art on disk resolves back to the default, so a server typo costs nothing.
        /// </summary>
        public string? SkinId { get; private set; }

        /// <summary>
        /// The event's accent colour as "#RRGGBB", or null to leave the mod's own
        /// palette alone. Consulted ahead of the mod's fxPalette slot, which is the
        /// whole point — an event dresses every mod, not just the default one.
        /// </summary>
        public string? AccentHex { get; private set; }

        /// <summary>
        /// Additive XP multiplier term, 0.0 when no event is running. Always within
        /// [0, <see cref="MaxXpBoost"/>].
        /// </summary>
        public double XpBoost { get; private set; }

        /// <summary>True when any of the three overrides is actually set.</summary>
        public bool IsActive => SkinId != null || AccentHex != null || XpBoost > 0.0;

        /// <summary>
        /// Raised after <see cref="Apply"/> or <see cref="Clear"/> so live surfaces
        /// can repaint. No subscribers in this build — nothing fires it.
        /// </summary>
        public event EventHandler? EventChanged;

        /// <summary>
        /// Install an event's overrides. NOT CALLED ANYWHERE IN THIS BUILD — the
        /// caller is the event engine, which ships later. Values are sanitised here
        /// rather than at the call site so the wire half cannot skip it.
        /// </summary>
        public void Apply(string? skinId, string? accentHex, double xpBoost)
        {
            SkinId = NormalizeId(skinId);
            AccentHex = NormalizeHex(accentHex);
            XpBoost = ClampXpBoost(xpBoost);
            EventChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Back to dormant. Also the state this object is constructed in.</summary>
        public void Clear()
        {
            SkinId = null;
            AccentHex = null;
            XpBoost = 0.0;
            EventChanged?.Invoke(this, EventArgs.Empty);
        }

        // ---- pure helpers (testable without a live App) ------------------------

        /// <summary>
        /// Clamp an event boost into [0, <see cref="MaxXpBoost"/>]. NaN and negative
        /// values collapse to 0 — a malformed event must never subtract XP.
        /// </summary>
        public static double ClampXpBoost(double value)
        {
            if (double.IsNaN(value) || value <= 0.0) return 0.0;
            return Math.Min(value, MaxXpBoost);
        }

        /// <summary>Blank/whitespace ids are "no skin", not a skin called " ".</summary>
        public static string? NormalizeId(string? id)
            => string.IsNullOrWhiteSpace(id) ? null : id!.Trim();

        /// <summary>
        /// Accept ONLY a literal "#RRGGBB". Anything else is treated as unset: the
        /// palette chain's next link is always a valid colour, so a bad event hex
        /// degrades to the mod's own accent instead of a brush parse throw somewhere
        /// deep in a renderer.
        ///
        /// SIX DIGITS, NOT EIGHT, and that is a real constraint rather than laziness:
        /// ModService.ParseHexColor accepts 6 only and silently returns hot pink for
        /// anything else, so an "#AARRGGBB" event accent would resolve to a correct
        /// colour in some places and to #FF69B4 in others. One length, one answer.
        /// </summary>
        public static string? NormalizeHex(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return null;
            var trimmed = hex!.Trim();
            if (trimmed.Length != 7) return null;
            if (trimmed[0] != '#') return null;
            for (int i = 1; i < trimmed.Length; i++)
            {
                if (!Uri.IsHexDigit(trimmed[i])) return null;
            }
            return trimmed.ToUpperInvariant();
        }
    }
}
