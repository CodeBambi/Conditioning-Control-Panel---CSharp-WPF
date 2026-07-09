using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// Persistent meta-progression save model for Chaos Mode — banked between runs and
/// loaded once at startup (<see cref="ChaosMeta.Init"/>). Serialized to
/// <c>chaos_meta.json</c> in the same folder as settings.json. Additive-only: every
/// field has a neutral default, so a fresh state leaves a run byte-for-byte unchanged.
/// </summary>
public sealed class ChaosMetaState
{
    // v2: added narrative-line persistence (additive, no migration)
    // v3: gold cutover - dials cost gold, pockets retired + refunded (ChaosMetaStore migration)
    public int SchemaVersion { get; set; } = 3;

    public int Sparks { get; set; } = 0;
    public HashSet<string> PurchasedUpgrades { get; set; } = new();
    /// <summary>Options-panel "Dials" the player has bought back with GOLD (UNLOCK_LADDER
    /// ids in engine/settings.js; drops until the v3 gold cutover). Absent = locked, so old
    /// saves start with the fall pre-set and the gear panel almost entirely padlocked.</summary>
    public HashSet<string> PurchasedDials { get; set; } = new();
    /// <summary>Trained habits the player has switched OFF (absent = on, so old saves stay fully active).</summary>
    public HashSet<string> DisabledUpgrades { get; set; } = new();
    public bool ExtremeUnlocked { get; set; } = false;

    /// <summary>Boon id pre-equipped to apply at run start (Loadout tab). Null = none.</summary>
    public string? EquippedStartBoon { get; set; } = null;

    /// <summary>Codex entries the player has encountered (prefixed: "bubble:{id}" / "boon:{id}").</summary>
    public HashSet<string> DiscoveredCodexIds { get; set; } = new();

    /// <summary>Lifetime-boon levels (Skills/Accessories/Utility): id -> level (>=1 means unlocked). 0/absent = locked.</summary>
    public Dictionary<string, int> LifetimeBoonLevels { get; set; } = new();

    /// <summary>Lifetime-boon ids currently toggled on (applied to a run at start, icon shown in the HUD strip).</summary>
    public HashSet<string> ActiveLifetimeBoons { get; set; } = new();

    // ---- hold-to-defuse onboarding (2026-06-11 verb rework) — all default false so old saves load clean ----
    public bool SeenDefuseTutorial { get; set; } = false;
    public bool SeenBarkDefuseFirst { get; set; } = false;
    public bool SeenBarkDefuseNoFocus { get; set; } = false;
    public bool SeenBarkDefuseRelease { get; set; } = false;
    public bool SeenBarkClickDetonate { get; set; } = false;

    // ---- behavioral-bubble debuts: first encounter spawns alone with an extended trance ----
    public bool SeenEcho { get; set; } = false;
    public bool SeenChaperone { get; set; } = false;
    public bool SeenTease { get; set; } = false;
    public bool SeenBound { get; set; } = false;
    public bool SeenBrittle { get; set; } = false;
    /// <summary>Braindrain's happy-path debut on the second descent (spawn alone + announce).</summary>
    public bool SeenBraindrain { get; set; } = false;

    // ---- two-currency split (v3 gold cutover): Sparks (code name frozen) is the DROPS
    // balance banked end-of-run - it LEVELS things (deepen/train/hands). Gold is the
    // instant in-run balance - it UNLOCKS things (dials + console extras) ----
    public int Gold { get; set; } = 0;

    // ---- RETIRED (v3): loadout pockets. Kept for schema compat; migration zeroes
    // them and refunds their gold. Do not read in new code ----
    public int ToyPockets { get; set; } = 0;
    public int AccessoryPockets { get; set; } = 0;

    /// <summary>Grab-in-the-tube rework (2026-07): consumable (active-toy) HUD slots the player
    /// can hold at once during a fall. Starts at 1; the dollhouse sews more with Sparks up to
    /// <see cref="ChaosMeta.MAX_CONSUMABLE_SLOTS"/>. Defaults to 1 so old saves get a working slot.</summary>
    public int ConsumableSlots { get; set; } = 1;

    /// <summary>Gold purchases at her bench (non-power conveniences): id -> owned.</summary>
    public HashSet<string> BenchPurchases { get; set; } = new();

    /// <summary>One-time auto-cover of a short balance on the first toy pocket attempt.</summary>
    public bool GiftGiven { get; set; } = false;

    // ---- lessons (challenge-gated buyability): id == purchasable id ----
    public Dictionary<string, long> LessonProgress { get; set; } = new();
    public HashSet<string> LessonsComplete { get; set; } = new();

    // ---- reveal framework: element ids pending their dollhouse flash / already flashed ----
    public HashSet<string> PendingReveals { get; set; } = new();
    public HashSet<string> SeenReveals { get; set; } = new();

    // ---- first-times bonuses (drops, one-time each): first_taste/first_snap/first_whisper/first_yes/first_play ----
    public HashSet<string> FirstTimesAwarded { get; set; } = new();

    // ---- happy-path scripted beats ----
    /// <summary>The first-open intro guide ("the invitation") — shown once, ever, the
    /// first time the Dollhouse opens, before any reveal flash.</summary>
    public bool SeenIntroGuide { get; set; } = false;
    public bool SeenDuoDemo { get; set; } = false;
    public bool SeenSkipDebut { get; set; } = false;
    public bool SeenGoldFirst { get; set; } = false;
    public bool SeenDollhouse { get; set; } = false;
    public bool SeenFirstSin { get; set; } = false;
    /// <summary>Once-ever gentle heads-up the first time focus dips below a snap's price
    /// (fires BEFORE the harsher NO FOCUS lesson can ever land).</summary>
    public bool SeenFocusTip { get; set; } = false;
    /// <summary>The Ripple's right-click teach: set on the FIRST successful cast, ever.
    /// Until then the ready-cue announce re-offers the verb once per (non-scripted) run.</summary>
    public bool SeenRippleTeach { get; set; } = false;
    /// <summary>Once-ever line the first time heat climbs — names the orange bar and its x2.</summary>
    public bool SeenHeatTeach { get; set; } = false;

    /// <summary>First-contact verb hints (ChaosBubbleHints): interaction archetypes the player
    /// has performed correctly once — their over-bubble hint text never shows again.</summary>
    public HashSet<string> BubbleHintsLearned { get; set; } = new();

    /// <summary>Highest rank index the player has been shown a rank card for (0 = curious).</summary>
    public int LastRankSeen { get; set; } = 0;

    // ---- narrative layer (the Madam): seen-once story lines + per-line cooldown ends ----
    /// <summary>Narrative cue ids that have played and must never repeat (mode == once). Accretes across descents.</summary>
    public HashSet<string> SeenNarrativeLines { get; set; } = new();
    /// <summary>Per-line cooldown ends for pooled lines: cue id -> Unix epoch ms when it may play again.</summary>
    public Dictionary<string, long> NarrativeCooldownEnds { get; set; } = new();

    // lifetime stats (consumed by the Stats tab in a later session)
    public int RunsCompleted { get; set; } = 0;
    public long BestScore { get; set; } = 0;
    public int BestCombo { get; set; } = 0;
    public long TotalDefused { get; set; } = 0;
    /// <summary>Total time spent down the hole across all completed descents, in seconds.</summary>
    public double TotalRunSeconds { get; set; } = 0;
    /// <summary>Lifetime seconds spent holding defuse channels ("time holding on" in the
    /// Looking Glass). Keeps accumulating after the slow_fuses lesson completes.</summary>
    public double TotalChannelSeconds { get; set; } = 0;
}
