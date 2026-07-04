namespace ConditioningControlPanel.Core.Services.Chaos;

/// <summary>
/// Platform-agnostic, faithful port of the WPF chaos bubble spawn catalog
/// (WPF ChaosBubbleVariants.cs:131 — the data-driven 8-row pool, the weighted picker,
/// the ordinary Build formulas, and every special-bubble builder). Size maps to
/// Strength (0..100): bigger bubble = stronger payload. All randomness flows through
/// the injected <see cref="Random"/> parameter so callers/tests stay deterministic —
/// the WPF original used a private static Random (WPF ChaosBubbleVariants.cs:150).
/// <para>
/// PayloadKind strings are the variant-id style kinds the Avalonia head already consumes
/// ("flash", "subliminal", "pink", "spiral", "braindrain", "bambifreeze", "video",
/// "htlink") — see AvaloniaHeadStubs.BuildPayload and AvaloniaEffectPayloadFactory.
/// The WPF enum mapping is: flash=Flash, subliminal=Subliminal, pink/spiral/braindrain=
/// Overlay(+OverlayKind), bambifreeze=BambiFreeze, video=Video, htlink=GifCascade
/// (WPF ChaosBubbleVariants.cs:649-676).
/// </para>
/// </summary>
public static class ChaosSpawnCatalog
{
    /// <summary>
    /// One row in the chaos bubble pool — visual + behaviour + payload binding.
    /// Port of the WPF <c>ChaosBubbleVariant</c> record (WPF ChaosBubbleVariants.cs:113-127)
    /// with the WPF <c>EffectBubblePayloadKind</c> enum replaced by the variant-id-style
    /// kind string and the WPF <c>Color</c> tint replaced by raw R/G/B bytes.
    /// </summary>
    public sealed record VariantDef(
        string Id,
        string Name,
        string PayloadKind,
        string? OverlayKind,
        bool IsLive,
        double MinSize,
        double MaxSize,
        ChaosMotion Motion,
        byte TintR,
        byte TintG,
        byte TintB,
        string Label,
        double Weight,
        double MinIntensity,
        int FuseMinMs,
        int FuseMaxMs);

    /// <summary>Curated one-click bubble-pool mix for the setup window (WPF ChaosBubbleVariants.cs:637).</summary>
    public sealed record ChaosPreset(string Name, List<string> VariantIds);

    // ---- Global size envelope used to normalise any bubble's size into a 0..100 Strength ----

    /// <summary>Bottom of the global size envelope (WPF ChaosBubbleVariants.cs:134).</summary>
    public const double SizeMinGlobal = 150;
    /// <summary>Top of the global size envelope (WPF ChaosBubbleVariants.cs:135).</summary>
    public const double SizeMaxGlobal = 320;

    /// <summary>Global field shrink: every variant bubble renders 25% smaller than its
    /// classic band; Breast Enlargement swells them back up via sizeScale
    /// (WPF ChaosBubbleVariants.cs:707).</summary>
    public const double GLOBAL_SIZE_SCALE = 0.75;
    /// <summary>The two giants (video + gif rain) run a further 30% smaller still
    /// (WPF ChaosBubbleVariants.cs:709).</summary>
    public const double GIANT_SIZE_SCALE = 0.70;

    // ---- Darter tuning (bouncing-flash catch target) (WPF ChaosBubbleVariants.cs:138-146) ----

    /// <summary>Safety backstop lifetime; despawn is driven by the 3-bounce-then-exit (WPF ChaosBubbleVariants.cs:138).</summary>
    public const int DARTER_LIFETIME_MS = 8000;
    /// <summary>Catch this fast after going active = bonus (WPF ChaosBubbleVariants.cs:139).</summary>
    public const int DARTER_QUICK_WINDOW_MS = 500;
    /// <summary>Flare-at-origin before it starts moving (WPF ChaosBubbleVariants.cs:140).</summary>
    public const int DARTER_TELEGRAPH_MS = 400;
    /// <summary>DIPs/frame — a speedy orb (WPF ChaosBubbleVariants.cs:141).</summary>
    public const double DARTER_SPEED = 9.0;
    /// <summary>Bounces this many times, then flies off-screen (WPF ChaosBubbleVariants.cs:142).</summary>
    public const int DARTER_MAX_BOUNCES = 3;
    /// <summary>Darter size band low end (WPF ChaosBubbleVariants.cs:143).</summary>
    public const double DARTER_SIZE_MIN = 72;
    /// <summary>Darter size band high end (WPF ChaosBubbleVariants.cs:144).</summary>
    public const double DARTER_SIZE_MAX = 96;
    /// <summary>Base score for a darter catch (WPF ChaosBubbleVariants.cs:145).</summary>
    public const int DARTER_BASE_POINTS = 120;
    /// <summary>Bonus score for a quick darter catch (WPF ChaosBubbleVariants.cs:146).</summary>
    public const int DARTER_QUICK_BONUS = 90;

    // ---- Lucky golden bubble tuning (WPF ChaosBubbleVariants.cs:194-196) ----

    /// <summary>Golden bubble size band low end (WPF ChaosBubbleVariants.cs:194).</summary>
    public const double GOLDEN_SIZE_MIN = 110;
    /// <summary>Golden bubble size band high end (WPF ChaosBubbleVariants.cs:195).</summary>
    public const double GOLDEN_SIZE_MAX = 140;
    /// <summary>Faster than everything else — blink and it's gone (WPF ChaosBubbleVariants.cs:196).</summary>
    public const double GOLDEN_SPEED_MULT = 2.8;

    // ---- Pop-up Notification heart tuning (WPF ChaosBubbleVariants.cs:223-225) ----

    /// <summary>Heart size band low end (WPF ChaosBubbleVariants.cs:223).</summary>
    public const double HEART_SIZE_MIN = 88;
    /// <summary>Heart size band high end (WPF ChaosBubbleVariants.cs:224).</summary>
    public const double HEART_SIZE_MAX = 110;
    /// <summary>A lazy drift — kind, but you still have to notice it (WPF ChaosBubbleVariants.cs:225).</summary>
    public const double HEART_SPEED_MULT = 0.8;

    // ---- Gold Digger droplet tuning (WPF ChaosBubbleVariants.cs:253-255) ----

    /// <summary>Droplet size band low end (WPF ChaosBubbleVariants.cs:253).</summary>
    public const double DROPLET_SIZE_MIN = 58;
    /// <summary>Droplet size band high end (WPF ChaosBubbleVariants.cs:254).</summary>
    public const double DROPLET_SIZE_MAX = 74;
    /// <summary>They fall fast — lean in or lose them (WPF ChaosBubbleVariants.cs:255).</summary>
    public const double DROPLET_SPEED_MULT = 2.2;

    // ---- Heavy Drop tuning (WPF ChaosBubbleVariants.cs:283-285) ----

    /// <summary>On top of the treat's max band — a true giant (WPF ChaosBubbleVariants.cs:283).</summary>
    public const double HEAVY_SIZE_MULT = 1.55;
    /// <summary>Slow, stately, unmissable (WPF ChaosBubbleVariants.cs:284).</summary>
    public const double HEAVY_SPEED_MULT = 0.45;
    /// <summary>Heavy Drops pay triple on pop (WPF ChaosBubbleVariants.cs:285).</summary>
    public const double HEAVY_PAY_MULT = 3.0;

    // ---- "Look at the bright colors..." prism tuning (WPF ChaosBubbleVariants.cs:315-317) ----

    /// <summary>Prism size band low end (WPF ChaosBubbleVariants.cs:315).</summary>
    public const double PRISM_SIZE_MIN = 165;
    /// <summary>Prism size band high end (WPF ChaosBubbleVariants.cs:316).</summary>
    public const double PRISM_SIZE_MAX = 215;
    /// <summary>A lazy, mesmerising drift (WPF ChaosBubbleVariants.cs:317).</summary>
    public const double PRISM_SPEED_MULT = 0.7;

    // ---- The Brittle tuning (WPF ChaosBubbleVariants.cs:356-357) ----

    /// <summary>Brittle size band low end (WPF ChaosBubbleVariants.cs:356).</summary>
    public const double BRITTLE_SIZE_MIN = 150;
    /// <summary>Brittle size band high end (WPF ChaosBubbleVariants.cs:357).</summary>
    public const double BRITTLE_SIZE_MAX = 185;

    // ---- The Echo tuning (WPF ChaosBubbleVariants.cs:393-394) ----

    /// <summary>Echo size band low end (WPF ChaosBubbleVariants.cs:393).</summary>
    public const double ECHO_SIZE_MIN = 180;
    /// <summary>Echo size band high end (WPF ChaosBubbleVariants.cs:394).</summary>
    public const double ECHO_SIZE_MAX = 240;

    // ---- The Tease tuning (WPF ChaosBubbleVariants.cs:458-459) ----

    /// <summary>Tease size band low end (WPF ChaosBubbleVariants.cs:458).</summary>
    public const double TEASE_SIZE_MIN = 170;
    /// <summary>Tease size band high end (WPF ChaosBubbleVariants.cs:459).</summary>
    public const double TEASE_SIZE_MAX = 210;

    // ---- The Chaperone escort tuning (WPF ChaosBubbleVariants.cs:534-535) ----

    /// <summary>Escort size band low end (WPF ChaosBubbleVariants.cs:534).</summary>
    public const double ESCORT_SIZE_MIN = 95;
    /// <summary>Escort size band high end (WPF ChaosBubbleVariants.cs:535).</summary>
    public const double ESCORT_SIZE_MAX = 120;

    /// <summary>Bound pair link id source (WPF ChaosBubbleVariants.cs:493 seeds at 1 and
    /// post-increments; Interlocked from 0 yields the same first id of 1).</summary>
    private static int _nextBoundPairId;

    /// <summary>
    /// The 8-row data-driven chaos bubble pool, in WPF table order
    /// (WPF ChaosBubbleVariants.cs:649-676). Values verbatim: sizes, motions, tints,
    /// labels, weights, MinIntensity gates and fuse bands. Note: "htlink" displays as
    /// "Gif Rain" but the id is save/discovery-persisted — keep it.
    /// </summary>
    public static readonly IReadOnlyList<VariantDef> All = new List<VariantDef>
    {
        // benign "treats" — pop fires a small payload, no fuse
        new("flash",       "Flash",       "flash",       null,          false, 150, 210, ChaosMotion.FloatUp,    0xFF, 0xD0, 0xE8, "",  3.0,  0.00, 0,    0),
        new("subliminal",  "Subliminal",  "subliminal",  null,          false, 170, 220, ChaosMotion.FloatUp,    0xB0, 0x80, 0xFF, "♥", 3.0,  0.00, 0,    0),
        // live "threats" — defuse for reward or they detonate the effect
        new("pink",        "Pink Filter", "pink",        "pink_filter", true,  180, 240, ChaosMotion.RainDown,   0xFF, 0x3D, 0xA5, "◑", 2.0,  0.10, 3500, 5000),
        new("spiral",      "Spiral",      "spiral",      "spiral",      true,  180, 240, ChaosMotion.RoamBounce, 0x40, 0xD0, 0xC0, "◎", 2.0,  0.15, 3500, 5000),
        new("braindrain",  "BrainDrain",  "braindrain",  "braindrain",  true,  240, 320, ChaosMotion.RoamBounce, 0x40, 0x60, 0xC0, "☁", 1.4,  0.25, 4500, 6500),
        // freeze — a GOOD pickup (no fuse); FloatUp so an uncaught one drifts off harmlessly
        new("bambifreeze", "Freeze",      "bambifreeze", null,          false, 190, 250, ChaosMotion.FloatUp,    0x8A, 0xE6, 0xFF, "❄", 0.5,  0.15, 0,    0),
        new("video",       "Video",       "video",       null,          true,  240, 300, ChaosMotion.RainDown,   0xE0, 0x40, 0x4D, "▶", 0.5,  0.50, 5000, 7000),
        new("htlink",      "Gif Rain",    "htlink",      null,          true,  200, 280, ChaosMotion.FloatUp,    0xFF, 0xC8, 0x3D, "▼", 0.45, 0.60, 4500, 6500),
    };

    /// <summary>All variant ids, in table order (WPF ChaosBubbleVariants.cs:588).</summary>
    public static List<string> AllIds() => All.Select(v => v.Id).ToList();

    /// <summary>
    /// Curated one-click bubble-pool mixes for the setup window
    /// (WPF ChaosBubbleVariants.cs:640-647): Balanced = every id; Tease = the lighter mix;
    /// Flash-only = the two treats. ("Overload" was removed 2026-06-12 in WPF — identical
    /// to Balanced.)
    /// </summary>
    public static List<ChaosPreset> Presets => new()
    {
        new("Balanced",   AllIds()),
        new("Tease",      new() { "flash", "subliminal", "pink", "spiral", "bambifreeze" }),
        new("Flash-only", new() { "flash", "subliminal" }),
    };

    /// <summary>
    /// Pick a variant (weighted, filtered by MinIntensity) and build a concrete
    /// <see cref="ChaosBubbleSpec"/> (WPF ChaosBubbleVariants.cs:682-704).
    /// <paramref name="intensity"/> (0..1) also biases size toward the top of the variant's
    /// band; <paramref name="fuseTimeMult"/> scales live fuses (boons);
    /// <paramref name="motionOverride"/> forces a motion if set;
    /// <paramref name="enabledIds"/> null means ALL variants are enabled. Fallback ladder:
    /// intensity-gated pool → enabled-but-gated pool → last-ditch <c>All[0]</c> (flash).
    /// </summary>
    public static ChaosBubbleSpec Pick(double intensity, double fuseTimeMult, ChaosMotion? motionOverride,
                                       IReadOnlyList<string>? enabledIds, double effectIntensity,
                                       double sizeScale, double sideDriftChance, Random rng)
    {
        var pool = All.Where(v => intensity >= v.MinIntensity && v.Weight > 0
                                  && (enabledIds == null || enabledIds.Contains(v.Id))).ToList();
        // Fall back to enabled-but-gated variants if intensity filtered everything out.
        if (pool.Count == 0)
            pool = All.Where(v => v.Weight > 0 && (enabledIds == null || enabledIds.Contains(v.Id))).ToList();
        if (pool.Count == 0) pool = new List<VariantDef> { All[0] };

        double total = pool.Sum(v => v.Weight);
        double roll = rng.NextDouble() * total;
        var variant = pool[^1];
        foreach (var v in pool)
        {
            roll -= v.Weight;
            if (roll <= 0) { variant = v; break; }
        }

        return Build(variant, intensity, fuseTimeMult, motionOverride, effectIntensity, sizeScale, sideDriftChance, rng);
    }

    /// <summary>
    /// Build one ordinary spec from a variant row (WPF ChaosBubbleVariants.cs:714-775).
    /// Size: random across the band, nudged upward by run intensity —
    /// <c>t = clamp(rng*0.7 + intensity*0.45, 0, 1)</c>. Strength is keyed to the CLASSIC
    /// (unscaled) size so visual sizing never weakens payloads or scoring:
    /// <c>strength = round(clamp((size-150)/170, 0, 1)*100)</c>, then
    /// <c>Strength = (int)clamp(strength*effectIntensity, 0, 100)</c>. Visual scale is
    /// <c>0.75 * max(0.5, sizeScale)</c>, video/htlink a further ×0.70. Motion:
    /// <c>motionOverride ?? variant.Motion</c>; a freeze forced onto RoamBounce remaps to
    /// FloatUp (RoamBounce never exits, an uncaught freeze would live forever); the
    /// side-drift swap rolls only when no override is set, motion isn't RoamBounce and
    /// <paramref name="sideDriftChance"/> &gt; 0. Live fuse:
    /// <c>baseFuse = FuseMinMs + rng.Next(max(1, FuseMaxMs-FuseMinMs))</c>, then
    /// <c>fuse = (int)max(1200, baseFuse * (1 - intensity*0.25) * fuseTimeMult)</c>.
    /// <para>DEVIATION: the WPF <c>ambient</c> parameter (dashboard "Trigger Bubbles" reuse,
    /// WPF ChaosBubbleVariants.cs:711-718) is not ported in this slice — it flips a
    /// payload-level <c>Ambient</c> flag the Core spec does not carry.</para>
    /// </summary>
    public static ChaosBubbleSpec Build(VariantDef variant, double intensity, double fuseTimeMult,
                                        ChaosMotion? motionOverride, double effectIntensity,
                                        double sizeScale, double sideDriftChance, Random rng)
    {
        double t = Math.Clamp(rng.NextDouble() * 0.7 + intensity * 0.45, 0, 1);
        double size = variant.MinSize + (variant.MaxSize - variant.MinSize) * t;
        int strength = (int)Math.Round(Math.Clamp((size - SizeMinGlobal) / (SizeMaxGlobal - SizeMinGlobal), 0, 1) * 100);
        double visual = GLOBAL_SIZE_SCALE * Math.Max(0.5, sizeScale);
        if (variant.Id is "video" or "htlink") visual *= GIANT_SIZE_SCALE;
        size *= visual;

        // The freeze bubble has no fuse, so it must use a motion that exits the screen
        // (RoamBounce never leaves). Force FloatUp if an override picked roam.
        var motion = motionOverride ?? variant.Motion;
        bool isFreezeVariant = variant.PayloadKind == "bambifreeze";
        if (isFreezeVariant && motion == ChaosMotion.RoamBounce) motion = ChaosMotion.FloatUp;
        // Entry variety: on Mixed motion, a slice of the vertical travellers swap to drifting
        // in from a side edge instead (SideDrift exits on its own, so freeze stays legal).
        if (motionOverride == null && motion != ChaosMotion.RoamBounce
            && sideDriftChance > 0 && rng.NextDouble() < sideDriftChance)
            motion = ChaosMotion.SideDrift;

        int fuse = 0;
        if (variant.IsLive)
        {
            int baseFuse = variant.FuseMinMs + rng.Next(Math.Max(1, variant.FuseMaxMs - variant.FuseMinMs));
            // Harder/later in the run = a bit shorter; boons (fuseTimeMult>1) lengthen.
            fuse = (int)Math.Max(1200, baseFuse * (1.0 - intensity * 0.25) * fuseTimeMult);
        }

        return new ChaosBubbleSpec
        {
            VariantId = variant.Id,
            PayloadKind = variant.PayloadKind,
            OverlayKind = variant.OverlayKind,
            Strength = (int)Math.Clamp(strength * effectIntensity, 0, 100),
            SizePx = size,
            TintR = variant.TintR, TintG = variant.TintG, TintB = variant.TintB,
            Label = variant.Label,
            IsLive = variant.IsLive,
            IsFreeze = isFreezeVariant,
            FuseMs = fuse,
            Motion = motion,
            EffectIntensity = effectIntensity,
            SideDriftChance = sideDriftChance,
        };
    }

    /// <summary>
    /// Build a lucky golden bubble (WPF ChaosBubbleVariants.cs:204-219): benign, small,
    /// quick, vertical (rises or falls, 50/50) and gone fast. Popping it pays real gold
    /// on the spot — the payout is keyed off <c>IsGolden</c> in the run engine. Payload is
    /// a zero-strength flash (the gold IS the treat).
    /// </summary>
    public static ChaosBubbleSpec BuildGolden(Random rng)
    {
        double size = GOLDEN_SIZE_MIN + (GOLDEN_SIZE_MAX - GOLDEN_SIZE_MIN) * rng.NextDouble();
        return new ChaosBubbleSpec
        {
            VariantId = "golden",
            PayloadKind = "flash",
            Strength = 0,
            SizePx = size,
            TintR = 0xFF, TintG = 0xD7, TintB = 0x00,
            Label = "🍀",
            IsLive = false,
            FuseMs = 0,
            Motion = rng.NextDouble() < 0.5 ? ChaosMotion.FloatUp : ChaosMotion.RainDown,
            IsGolden = true,
            SpeedMult = GOLDEN_SPEED_MULT,
        };
    }

    /// <summary>
    /// Build the Pop-up Notification heart (WPF ChaosBubbleVariants.cs:234-249): a small
    /// benign pickup drifting down from the top. Catching it grants +1 resistance (keyed
    /// off <c>IsHeart</c>); missing it costs nothing.
    /// </summary>
    public static ChaosBubbleSpec BuildHeart(Random rng)
    {
        double size = HEART_SIZE_MIN + (HEART_SIZE_MAX - HEART_SIZE_MIN) * rng.NextDouble();
        return new ChaosBubbleSpec
        {
            VariantId = "heart",
            PayloadKind = "flash",
            Strength = 0,
            SizePx = size,
            TintR = 0xFF, TintG = 0x4D, TintB = 0x6E,
            Label = "💖",
            IsLive = false,
            FuseMs = 0,
            Motion = ChaosMotion.RainDown,
            IsHeart = true,
            SpeedMult = HEART_SPEED_MULT,
        };
    }

    /// <summary>
    /// Build one Gold Digger droplet (WPF ChaosBubbleVariants.cs:262-278): a small gold
    /// bead that bursts out of a popped lucky bubble (pinned at the pop point in physical
    /// px) and rains straight down. Catching it banks a few Sparks (keyed off
    /// <c>IsDroplet</c>).
    /// </summary>
    public static ChaosBubbleSpec BuildGoldDroplet(double atPxX, double atPxY, Random rng)
    {
        double size = DROPLET_SIZE_MIN + (DROPLET_SIZE_MAX - DROPLET_SIZE_MIN) * rng.NextDouble();
        return new ChaosBubbleSpec
        {
            SpawnAtPxX = atPxX,
            SpawnAtPxY = atPxY,
            VariantId = "gold_droplet",
            PayloadKind = "flash",
            Strength = 0,
            SizePx = size,
            TintR = 0xFF, TintG = 0xD7, TintB = 0x00,
            Label = "✧",
            IsLive = false,
            FuseMs = 0,
            Motion = ChaosMotion.RainDown,
            IsDroplet = true,
            SpeedMult = DROPLET_SPEED_MULT,
        };
    }

    /// <summary>
    /// Build a Heavy Drop (WPF ChaosBubbleVariants.cs:291-311): a giant, slow treat
    /// (flash or subliminal, 50/50) that pays triple on pop. Strength keys off the
    /// variant's classic MAX size (near the band's ceiling). The WPF
    /// <paramref name="intensity"/> parameter is unused there too — kept for parity.
    /// </summary>
    public static ChaosBubbleSpec BuildHeavy(double intensity, double effectIntensity, double sizeScale, Random rng)
    {
        var variant = All[rng.Next(2)];   // rows 0/1 = the flash + subliminal treats
        double classic = variant.MaxSize; // top of the band → Strength near the band's ceiling
        int strength = (int)Math.Round(Math.Clamp((classic - SizeMinGlobal) / (SizeMaxGlobal - SizeMinGlobal), 0, 1) * 100);
        return new ChaosBubbleSpec
        {
            VariantId = variant.Id,
            PayloadKind = variant.PayloadKind,
            OverlayKind = variant.OverlayKind,
            Strength = (int)Math.Clamp(strength * effectIntensity, 0, 100),
            SizePx = classic * GLOBAL_SIZE_SCALE * Math.Max(0.5, sizeScale) * HEAVY_SIZE_MULT,
            TintR = variant.TintR, TintG = variant.TintG, TintB = variant.TintB,
            Label = variant.Label,
            IsLive = false,
            FuseMs = 0,
            Motion = ChaosMotion.RainDown,   // it DROPS — heavy, after all
            SpeedMult = HEAVY_SPEED_MULT,
            PayMult = HEAVY_PAY_MULT,
            TreatLifeMs = 9000,              // slow faller — give it time to be reached
            EffectIntensity = effectIntensity,
        };
    }

    /// <summary>
    /// Build the mimic prism (WPF ChaosBubbleVariants.cs:328-352): a swirling iridescent
    /// ball wearing another bubble's soul. Popping it pays 10x and fires the copied
    /// variant's payload. Mimic pool excludes video (too much hijack) and bambifreeze;
    /// <paramref name="treatOnly"/> (shielded bright_colors) additionally drops every
    /// live row. Size 165–215 × GLOBAL_SIZE_SCALE — note NO sizeScale here, verbatim WPF.
    /// </summary>
    public static ChaosBubbleSpec BuildPrism(double intensity, double effectIntensity, bool treatOnly, Random rng)
    {
        var pool = All.Where(v => v.Id != "video" && v.PayloadKind != "bambifreeze"
                                  && (!treatOnly || !v.IsLive)).ToList();
        var mimic = pool[rng.Next(pool.Count)];
        double size = PRISM_SIZE_MIN + (PRISM_SIZE_MAX - PRISM_SIZE_MIN) * rng.NextDouble();
        int strength = (int)Math.Round(Math.Clamp((size - SizeMinGlobal) / (SizeMaxGlobal - SizeMinGlobal), 0, 1) * 100);
        return new ChaosBubbleSpec
        {
            VariantId = "prism",
            PayloadKind = mimic.PayloadKind,
            OverlayKind = mimic.OverlayKind,
            Strength = (int)Math.Clamp(strength * effectIntensity, 0, 100),
            SizePx = size * GLOBAL_SIZE_SCALE,
            TintR = 0xC8, TintG = 0xA8, TintB = 0xFF,
            Label = "❂",
            IsLive = false,
            FuseMs = 0,
            Motion = rng.NextDouble() < 0.5 ? ChaosMotion.RainDown : ChaosMotion.RoamBounce,
            IsPrism = true,
            MimicVariantId = mimic.Id,
            SpeedMult = PRISM_SPEED_MULT,
            EffectIntensity = effectIntensity,
        };
    }

    /// <summary>
    /// Build The Brittle (WPF ChaosBubbleVariants.cs:366-389): a glass mine wearing a
    /// random LIVE bubble's effect (video and gif rain included — the whole live pool).
    /// The cursor merely brushing it shatters it. Vertical drift only (50/50
    /// FloatUp/RainDown), so a dodged one always clears the screen on its own.
    /// </summary>
    public static ChaosBubbleSpec BuildBrittle(double intensity, double effectIntensity, double sizeScale, Random rng)
    {
        var pool = All.Where(v => v.IsLive).ToList();
        var mimic = pool[rng.Next(pool.Count)];
        double size = BRITTLE_SIZE_MIN + (BRITTLE_SIZE_MAX - BRITTLE_SIZE_MIN) * rng.NextDouble();
        int strength = (int)Math.Round(Math.Clamp((size - SizeMinGlobal) / (SizeMaxGlobal - SizeMinGlobal), 0, 1) * 100);
        return new ChaosBubbleSpec
        {
            VariantId = "brittle",
            PayloadKind = mimic.PayloadKind,
            OverlayKind = mimic.OverlayKind,
            Strength = (int)Math.Clamp(strength * effectIntensity, 0, 100),
            SizePx = size * GLOBAL_SIZE_SCALE * Math.Max(0.5, sizeScale),
            TintR = 0xD9, TintG = 0xEF, TintB = 0xFF,
            Label = "◇",
            IsLive = false,            // no fuse — its trigger is your own hand straying
            FuseMs = 0,
            Motion = rng.NextDouble() < 0.5 ? ChaosMotion.FloatUp : ChaosMotion.RainDown,
            IsBrittle = true,
            MimicVariantId = mimic.Id,
            SpeedMult = ChaosTuning.BRITTLE_SPEED_MULT,
            EffectIntensity = effectIntensity,
        };
    }

    /// <summary>
    /// Build The Echo (WPF ChaosBubbleVariants.cs:403-425): a live bubble whose payload
    /// never fires — TRIGGERING it splits it into two children instead; a completed
    /// defuse deflates it cleanly. Fuse
    /// <c>= max(1200, (3500 + rng.Next(1500)) * (1 - intensity*0.25) * fuseTimeMult * max(0.1, fuseMult))</c>;
    /// <paramref name="fuseMult"/> &gt; 1 = the gentler debut trance.
    /// </summary>
    public static ChaosBubbleSpec BuildEcho(double intensity, double fuseTimeMult, double sizeScale,
                                            double fuseMult, Random rng)
    {
        double t = Math.Clamp(rng.NextDouble() * 0.7 + intensity * 0.45, 0, 1);
        double size = ECHO_SIZE_MIN + (ECHO_SIZE_MAX - ECHO_SIZE_MIN) * t;
        int baseFuse = 3500 + rng.Next(1500);
        int fuse = (int)Math.Max(1200, baseFuse * (1.0 - intensity * 0.25) * fuseTimeMult * Math.Max(0.1, fuseMult));
        return new ChaosBubbleSpec
        {
            VariantId = "echo",
            PayloadKind = "flash",
            Strength = 0,   // never fires — the split IS the trigger
            SizePx = size * GLOBAL_SIZE_SCALE * Math.Max(0.5, sizeScale),
            TintR = 0xC9, TintG = 0xC4, TintB = 0xE8,
            Label = "◌",
            IsLive = true,
            FuseMs = fuse,
            Motion = ChaosMotion.FloatUp,
            IsEcho = true,
        };
    }

    /// <summary>
    /// Build one Echo split-child (WPF ChaosBubbleVariants.cs:429-454): a NORMAL live from
    /// the light trio (pink/spiral/braindrain), smaller and faster, with a short trance.
    /// Children carry no IsEcho flag, so they never re-split. Size
    /// <c>= max(60, parent*0.6)</c>; Strength is keyed back through the global shrink
    /// (<c>classicEq = size / 0.75</c>) so a child hits like a small classic bubble. The
    /// CALLER pins the spawn point (WPF passes ChaosLastPopXPx±70 / ChaosLastPopYPx±50) —
    /// this builder takes explicit <paramref name="atPxX"/>/<paramref name="atPxY"/>.
    /// </summary>
    public static ChaosBubbleSpec BuildEchoChild(double parentVisualSizePx, double atPxX, double atPxY,
                                                 double effectIntensity, Random rng)
    {
        var v = All[2 + rng.Next(3)];   // rows 2..4 = pink / spiral / braindrain
        double size = Math.Max(60, parentVisualSizePx * ChaosTuning.ECHO_CHILD_SCALE);
        double classicEq = size / GLOBAL_SIZE_SCALE;
        int strength = (int)Math.Round(Math.Clamp((classicEq - SizeMinGlobal) / (SizeMaxGlobal - SizeMinGlobal), 0, 1) * 100);
        int fuse = ChaosTuning.ECHO_CHILD_FUSE_MIN_MS
                   + rng.Next(Math.Max(1, ChaosTuning.ECHO_CHILD_FUSE_MAX_MS - ChaosTuning.ECHO_CHILD_FUSE_MIN_MS));
        return new ChaosBubbleSpec
        {
            SpawnAtPxX = atPxX,
            SpawnAtPxY = atPxY,
            VariantId = v.Id,
            PayloadKind = v.PayloadKind,
            OverlayKind = v.OverlayKind,
            Strength = (int)Math.Clamp(strength * effectIntensity, 0, 100),
            SizePx = size,
            TintR = v.TintR, TintG = v.TintG, TintB = v.TintB,
            Label = v.Label,
            IsLive = true,
            FuseMs = fuse,
            Motion = ChaosMotion.RoamBounce,   // they scatter from the split point
            SpeedMult = ChaosTuning.ECHO_CHILD_SPEED_MULT,
            EffectIntensity = effectIntensity,
        };
    }

    /// <summary>
    /// Build The Chaperone (WPF ChaosBubbleVariants.cs:543-585): a live bubble (light
    /// trio) plus a small escort treat (flash/subliminal, size 95–120) that orbits it.
    /// While the escort lives the live is SHIELDED. The escort's strength is floored at
    /// 10 before the effectIntensity scale: <c>clamp(max(10, estrength) * effectIntensity, 0, 100)</c>.
    /// </summary>
    public static (ChaosBubbleSpec Live, ChaosBubbleSpec Escort) BuildChaperonePair(
        double intensity, double fuseTimeMult, double effectIntensity,
        double sizeScale, double fuseMult, Random rng)
    {
        var v = All[2 + rng.Next(3)];   // pink / spiral / braindrain
        double t = Math.Clamp(rng.NextDouble() * 0.7 + intensity * 0.45, 0, 1);
        double size = v.MinSize + (v.MaxSize - v.MinSize) * t;
        int strength = (int)Math.Round(Math.Clamp((size - SizeMinGlobal) / (SizeMaxGlobal - SizeMinGlobal), 0, 1) * 100);
        int baseFuse = v.FuseMinMs + rng.Next(Math.Max(1, v.FuseMaxMs - v.FuseMinMs));
        int fuse = (int)Math.Max(1200, baseFuse * (1.0 - intensity * 0.25) * fuseTimeMult * Math.Max(0.1, fuseMult));
        var live = new ChaosBubbleSpec
        {
            VariantId = v.Id,
            PayloadKind = v.PayloadKind,
            OverlayKind = v.OverlayKind,
            Strength = (int)Math.Clamp(strength * effectIntensity, 0, 100),
            SizePx = size * GLOBAL_SIZE_SCALE * Math.Max(0.5, sizeScale),
            TintR = v.TintR, TintG = v.TintG, TintB = v.TintB,
            Label = v.Label,
            IsLive = true,
            FuseMs = fuse,
            Motion = ChaosMotion.RoamBounce,   // the pair roams together — orbit reads best in motion
            IsChaperoneLive = true,
            EffectIntensity = effectIntensity,
        };

        var ev = All[rng.Next(2)];   // flash / subliminal escort
        double esize = ESCORT_SIZE_MIN + (ESCORT_SIZE_MAX - ESCORT_SIZE_MIN) * rng.NextDouble();
        int estrength = (int)Math.Round(Math.Clamp((esize - SizeMinGlobal) / (SizeMaxGlobal - SizeMinGlobal), 0, 1) * 100);
        var escort = new ChaosBubbleSpec
        {
            VariantId = ev.Id,
            PayloadKind = ev.PayloadKind,
            OverlayKind = ev.OverlayKind,
            Strength = (int)Math.Clamp(Math.Max(10, estrength) * effectIntensity, 0, 100),
            SizePx = esize * GLOBAL_SIZE_SCALE * Math.Max(0.5, sizeScale),
            TintR = ev.TintR, TintG = ev.TintG, TintB = ev.TintB,
            Label = ev.Label,
            IsLive = false,
            IsEscort = true,
            Motion = ChaosMotion.RoamBounce,   // motion is overridden by the orbit while linked
            EffectIntensity = effectIntensity,
        };
        return (live, escort);
    }

    /// <summary>
    /// Build The Bound (WPF ChaosBubbleVariants.cs:501-531): two tethered live bubbles
    /// (light trio, independently rolled) sharing a PairId from an internal counter. Both
    /// must be defused — the second within the bound window of the first — or the survivor
    /// enrages.
    /// <para>DEVIATION vs WPF: the Core spec carries <c>BoundWindowMs</c>
    /// (= <see cref="ChaosTuning.BOUND_WINDOW_MS"/>) directly; in WPF the window lives in
    /// ChaosModeService and the spec has no such field.</para>
    /// </summary>
    public static (ChaosBubbleSpec A, ChaosBubbleSpec B) BuildBoundPair(
        double intensity, double fuseTimeMult, double effectIntensity,
        double sizeScale, double fuseMult, Random rng)
    {
        int pairId = Interlocked.Increment(ref _nextBoundPairId);
        ChaosBubbleSpec One()
        {
            var v = All[2 + rng.Next(3)];   // pink / spiral / braindrain
            double t = Math.Clamp(rng.NextDouble() * 0.7 + intensity * 0.45, 0, 1);
            double size = v.MinSize + (v.MaxSize - v.MinSize) * t;
            int strength = (int)Math.Round(Math.Clamp((size - SizeMinGlobal) / (SizeMaxGlobal - SizeMinGlobal), 0, 1) * 100);
            int baseFuse = v.FuseMinMs + rng.Next(Math.Max(1, v.FuseMaxMs - v.FuseMinMs));
            int fuse = (int)Math.Max(1200, baseFuse * (1.0 - intensity * 0.25) * fuseTimeMult * Math.Max(0.1, fuseMult));
            return new ChaosBubbleSpec
            {
                VariantId = v.Id,
                PayloadKind = v.PayloadKind,
                OverlayKind = v.OverlayKind,
                Strength = (int)Math.Clamp(strength * effectIntensity, 0, 100),
                SizePx = size * GLOBAL_SIZE_SCALE * Math.Max(0.5, sizeScale),
                TintR = v.TintR, TintG = v.TintG, TintB = v.TintB,
                Label = v.Label,
                IsLive = true,
                FuseMs = fuse,
                Motion = ChaosMotion.RoamBounce,
                IsBoundHalf = true,
                PairId = pairId,
                BoundWindowMs = ChaosTuning.BOUND_WINDOW_MS,
                EffectIntensity = effectIntensity,
            };
        }
        return (One(), One());
    }

    /// <summary>
    /// Build The Tease (WPF ChaosBubbleVariants.cs:468-490): a glossy black/red bubble
    /// marked with a pulsing ✖. Any mouse-down triggers its payload AND halves the streak;
    /// left alone it expires into the DENIED bonus. Payload pool = the standard table
    /// minus video and bambifreeze (uniform pick, like the prism's pool).
    /// <para>DEVIATION vs WPF: the Core spec carries <c>LifetimeMs</c>
    /// (= <see cref="ChaosTuning.TEASE_LIFE_MS"/>) directly; in WPF the tease life is
    /// applied by BubbleService, not stamped on the spec.</para>
    /// </summary>
    public static ChaosBubbleSpec BuildTease(double intensity, double effectIntensity, double sizeScale, Random rng)
    {
        var pool = All.Where(v => v.Id != "video" && v.PayloadKind != "bambifreeze").ToList();
        var v = pool[rng.Next(pool.Count)];
        double size = TEASE_SIZE_MIN + (TEASE_SIZE_MAX - TEASE_SIZE_MIN) * rng.NextDouble();
        int strength = (int)Math.Round(Math.Clamp((size - SizeMinGlobal) / (SizeMaxGlobal - SizeMinGlobal), 0, 1) * 100);
        return new ChaosBubbleSpec
        {
            VariantId = "tease",
            PayloadKind = v.PayloadKind,
            OverlayKind = v.OverlayKind,
            Strength = (int)Math.Clamp(strength * effectIntensity, 0, 100),
            SizePx = size * GLOBAL_SIZE_SCALE * Math.Max(0.5, sizeScale),
            TintR = 0xB3, TintG = 0x0E, TintB = 0x2E,
            Label = "✖",
            IsLive = false,            // its own life/expiry path — not a trance, not a treat
            FuseMs = 0,
            Motion = ChaosMotion.RoamBounce,   // drift handled per-frame (center pull + wiggle)
            IsTease = true,
            LifetimeMs = ChaosTuning.TEASE_LIFE_MS,
            EffectIntensity = effectIntensity,
        };
    }

    /// <summary>
    /// Build a darter spec (WPF ChaosBubbleVariants.cs:165-190): benign, no fuse; carries
    /// a brief micro-flash payload (Strength 8). Size 72–96 (×1.15 spotlight), RoamBounce,
    /// lifetime backstop 8000 ms (real despawn: 3 bounces then exit), telegraph 400 ms
    /// (sweepers bolt at 150 ms), quick window 500 ms, speed 9.0 DIPs/frame.
    /// <paramref name="atPxX"/>/<paramref name="atPxY"/> (physical px) pin the spawn point
    /// — Rabbit Caller's summon-at-click. The WPF <paramref name="intensity"/> parameter
    /// is unused there too — kept for parity.
    /// </summary>
    public static ChaosBubbleSpec BuildDarter(double intensity, bool spotlight, bool sweeper, Random rng,
                                              double? atPxX = null, double? atPxY = null)
    {
        double size = DARTER_SIZE_MIN + (DARTER_SIZE_MAX - DARTER_SIZE_MIN) * rng.NextDouble();
        if (spotlight) size *= 1.15;   // Tunnel Vision capstone: rabbits run bigger
        return new ChaosBubbleSpec
        {
            SpawnAtPxX = atPxX,
            SpawnAtPxY = atPxY,
            VariantId = "darter",
            PayloadKind = "flash",
            Strength = 8,   // a brief micro-flash on catch
            SizePx = size,
            Spotlight = spotlight,
            TintR = 0xFF, TintG = 0x4D, TintB = 0xC4,
            Label = "",
            IsLive = false,
            FuseMs = 0,
            Motion = ChaosMotion.RoamBounce,              // bounce style; darter path overrides speed
            IsDarter = true,
            IsSweeper = sweeper,                          // GG make more GG: born spanked, never caught
            LifetimeMs = DARTER_LIFETIME_MS,
            TelegraphMs = sweeper ? 150 : DARTER_TELEGRAPH_MS,   // sweepers bolt almost immediately
            QuickWindowMs = DARTER_QUICK_WINDOW_MS,
            DarterSpeed = DARTER_SPEED,
            DarterMaxBounces = DARTER_MAX_BOUNCES,
        };
    }

    /// <summary>
    /// Per-spawn-tick roll for a darter (WPF ChaosBubbleVariants.cs:155-160). Chance
    /// <c>= (0.0125 + clamp(intensity, 0, 1) * 0.03) * max(0, rateMult)</c> — ~0.0125
    /// early → ~0.0425 late. Returns a built darter spec, or null on a no-spawn roll.
    /// </summary>
    public static ChaosBubbleSpec? RollDarter(double intensity, double rateMult, Random rng, bool spotlight = false)
    {
        double chance = (0.0125 + Math.Clamp(intensity, 0, 1) * 0.03) * Math.Max(0, rateMult);
        if (rng.NextDouble() >= chance) return null;
        return BuildDarter(intensity, spotlight, false, rng);
    }

    /// <summary>
    /// Build one Welcome Shower treat (WPF ChaosModeService.cs:1649-1665
    /// SpawnWelcomeShower): a flash/subliminal treat (50/50) built through the ordinary
    /// <see cref="Build"/> path with a forced RainDown motion and no side-drift roll —
    /// the run engine spawns 6 of these at run start / each loop GO.
    /// </summary>
    public static ChaosBubbleSpec BuildWelcomeShowerTreat(double intensity, double fuseTimeMult,
                                                          double effectIntensity, double sizeScale, Random rng)
    {
        var variant = All[rng.Next(2)];   // rows 0/1 = the treats
        return Build(variant, intensity, fuseTimeMult, ChaosMotion.RainDown, effectIntensity, sizeScale, 0.0, rng);
    }
}
