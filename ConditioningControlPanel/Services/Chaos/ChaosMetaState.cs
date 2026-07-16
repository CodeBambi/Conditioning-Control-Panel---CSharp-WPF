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

    // ---- guided FTUE (2026-07): the Warren hub hand-holding beats ----
    /// <summary>The hub welcome beats + portal guide card, shown once on the first-ever Warren open.</summary>
    public bool SeenWarrenWelcome { get; set; } = false;
    /// <summary>The first-return beats + TOYBOX/DIALS guide cards, shown once after run 1.</summary>
    public bool SeenFirstReturn { get; set; } = false;
    /// <summary>One-shot: the NEXT descent deals the scripted classroom config regardless of
    /// RunsCompleted (set by reset-onboarding, consumed + cleared at request-run deal time).</summary>
    public bool ForceScriptedRun { get; set; } = false;

    /// <summary>First-contact verb hints (ChaosBubbleHints): interaction archetypes the player
    /// has performed correctly once — their over-bubble hint text never shows again.</summary>
    public HashSet<string> BubbleHintsLearned { get; set; } = new();

    /// <summary>Highest rank index the player has been shown a rank card for (0 = curious).</summary>
    public int LastRankSeen { get; set; } = 0;

    /// <summary>The Cheshire tutorial arc position (0 = fresh .. 6 = arc done). Climb-only
    /// via set-num; zeroed by reset-onboarding for a full replay. Existing saves self-heal
    /// to done page-side (runsCompleted &gt; 0 stamps 6 before any suppression decision).</summary>
    public int TutorialStage { get; set; } = 0;

    // ---- narrative layer (the Madam): seen-once story lines + per-line cooldown ends ----
    /// <summary>Narrative cue ids that have played and must never repeat (mode == once). Accretes across descents.</summary>
    public HashSet<string> SeenNarrativeLines { get; set; } = new();
    /// <summary>Per-line cooldown ends for pooled lines: cue id -> Unix epoch ms when it may play again.</summary>
    public Dictionary<string, long> NarrativeCooldownEnds { get; set; } = new();

    // ---- crafting (2026-07, THE BOUDOIR): materials drop in the tube, recipes are
    // pictograms drawn on the 3x3 worktable. Id whitelists live in ChaosCraftingIds.cs;
    // grid shapes live page-side (game/crafting.js is the single source of truth) ----
    /// <summary>Banked crafting materials: material id -> count. Granted live per grab
    /// (material-add), like gold, so abandoned runs keep what was picked up.</summary>
    public Dictionary<string, int> Materials { get; set; } = new();
    /// <summary>Recipe ids the player has crafted at least once (drives the discovered-pictures
    /// strip and, in Part 3, which paperwall hints stop being hints).</summary>
    public HashSet<string> DiscoveredRecipes { get; set; } = new();
    /// <summary>Crafted item holdings: recipe id -> count (consumables stack; permanents
    /// stay at 1 — the dupe guard is page-side; the_shot is the repeatable exception, cap 10).</summary>
    public Dictionary<string, int> CraftedItems { get; set; } = new();
    /// <summary>THE PADLOCK: boon id pinned into the first draft of every descent. Null = none.
    /// Requires owning the_padlock (enforced by the pin-boon op).</summary>
    public string? PinnedBoon { get; set; } = null;
    /// <summary>THE CAGE's DENIAL modifier toggle (no hearts fall, everything pays +50%).
    /// Arming requires owning the_cage (enforced by the set-denial op).</summary>
    public bool DenialArmed { get; set; } = false;
    /// <summary>Cheshire's once-ever Boudoir tour (boudoir_intro VN scene).</summary>
    public bool SeenBoudoirIntro { get; set; } = false;

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
