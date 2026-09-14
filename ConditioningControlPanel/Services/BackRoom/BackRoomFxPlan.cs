using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.BackRoom;

// THE BACK ROOM, effects half one: the pure recipe table (CONTRACT section 4). Nothing in this file
// touches a service, a window or a clock, so every rule the host enforces on an fx id - intensity,
// motion, toggles, symbol resolution - is decided here and pinned by BackRoomFxPlanTests. The
// executor (BackRoomFx) only walks the plan this produces.

/// <summary>The primitives an fx id resolves into (CONTRACT section 4, primitive table).</summary>
public enum FxPrim
{
    FlashBurst,
    GifRain,
    GlitchBubbles,
    SubSingle,
    SubSeq,
    SubBurst9,
    SpiralFull,
    BrainDrainMelt,
    /// <summary>Brain drain without the drip: what <see cref="BrainDrainMelt"/> becomes at MotionLevel
    /// Off or with the melt toggle off. Never authored in a recipe.</summary>
    BrainDrain,
    GifFull,
    /// <summary>Hypno v3 (10.13.B): a soft colour wash over the virtual screen, optionally with a picture.</summary>
    Wash,
    /// <summary>Hypno v3: a dealt picture growing out of a page rect to cover the room window's screen.</summary>
    GifFrom,
    /// <summary>Hypno v3: a Loom-woven spiral GIF over the virtual screen. <see cref="SpiralFull"/> plays through it too.</summary>
    SpiralLoom,
    /// <summary>Hypno v3: the app's BrainDrain blur without the drip, Full only.</summary>
    Haze,
}

/// <summary>
/// One authored step. <paramref name="AtMs"/> is the offset from the fx start. <paramref name="Count"/>
/// is the flash amount, the wash count or the word count. <paramref name="Level"/> is the fraction of
/// the user's own opacity or strength (1 = as set, 0.5 = the Calm "half"). <paramref name="Still"/>
/// is set by the motion rules, never authored. For the Hypno v3 primitives <paramref name="Level"/> is the
/// resolved amount: the wash peak alpha, the gif-from dim factor, the spiral-loom alpha, the haze fraction;
/// <paramref name="Look"/> carries the rest of the validated <c>args</c>.
/// </summary>
public sealed record FxStep(FxPrim Prim, int AtMs, int Count = 1, int DurationMs = 0, double Level = 1.0, bool Still = false,
    FxLook? Look = null);

/// <summary>The validated <c>args</c> of one Hypno v3 step (CONTRACT 10.13.B, host validation column).</summary>
/// <param name="From">Null = grow from the centre (a missing or bad rect).</param>
/// <param name="Hold">Stays until <c>fx-release</c> or the 20 s cap.</param>
public sealed record FxLook(FxRgb Color, FxCssRect? From = null, double Scale = 1.0, string Preset = BackRoomSpiralSource.Screen, bool Hold = false);

/// <summary>A recipe: its steps and, for a hero, how long the hero window holds the stage.</summary>
public sealed record FxRecipe(int HeroMs, IReadOnlyList<FxStep> Steps)
{
    public bool IsHero => HeroMs > 0;
}

/// <summary>The feature toggles a plan is gated by, read once per fire. <paramref name="Melt"/> is the room's own melt
/// switch (<c>AppSettings.BackRoomMelt</c>) and <paramref name="Tunnel"/> its own tunnel vision switch
/// (<c>AppSettings.BackRoomTunnel</c>), both on by default (CONTRACT 10.14).</summary>
public sealed record FxGates(bool Flash, bool Subliminal, bool Spiral, bool BrainDrain, bool Melt, bool SpiralStill = true, bool Tunnel = true)
{
    public static readonly FxGates AllOn = new(true, true, true, true, true);
}

/// <summary>A resolved step with the media it will use (words for sub primitives, one GIF for gif-full and
/// gif-from, an optional picture for a wash, the woven file for spiral-full and spiral-loom).</summary>
public sealed record FxPlannedStep(FxStep Step, IReadOnlyList<string> Words, BackRoomGif? Gif, string? SpiralPath = null);

/// <summary>What one fx id will do under the current settings, plus every skip for the ack.</summary>
public sealed record FxPlan(string FxId, BackRoomFxIntensity Intensity, int HeroMs,
    IReadOnlyList<FxPlannedStep> Steps, IReadOnlyList<BackRoomFxSkip> Skipped)
{
    public bool IsHero => HeroMs > 0;
    public IReadOnlyList<string> Fired => Steps.Select(s => BackRoomFxPlan.WireName(s.Step.Prim)).ToList();
}

public static class BackRoomFxPlan
{
    /// <summary>Brake: no strobe over 6 Hz. Repeated words sit at least this far apart, which is
    /// ~4.5 Hz, comfortably under the ceiling.</summary>
    public const int WordGapMs = 220;

    /// <summary>One glitch wash: the overlay fades in over 500 ms, so a wash shorter than this never
    /// reads as its own beat.</summary>
    public const int GlitchWashMs = 800;

    /// <summary>The glitch wash opacity the contract names (<c>ChaosFlashOverlay.Show(ms, 0.3)</c>).</summary>
    public const double GlitchOpacity = 0.3;

    /// <summary>FlashService staggers a one-shot's images this far apart (FlashService, <c>i * 300</c>),
    /// so a flash-burst of n images is n onsets spread over <c>(n - 1) * FlashImageGapMs</c>.</summary>
    public const int FlashImageGapMs = 300;

    /// <summary>At most one symbol key per dealt item (4 words + 4 GIFs). A page sending hundreds of
    /// keys must not schedule hundreds of subliminals.</summary>
    public const int MaxSymbolKeys = 8;

    // ---- Hypno v3 (CONTRACT 10.13.B) ----
    /// <summary>Wash peak alpha at strength 1 (Law 5).</summary>
    public const double WashPeak = 0.42;
    public const double DefaultWashStrength = 0.7;
    /// <summary>A wash is gone this long after it starts (80 ms up, then 4.5/s decay).</summary>
    public const int WashMs = 900;
    /// <summary>Strobe channel Wash: a wash inside this gap of the previous one is dropped as busy.</summary>
    public const int WashGapMs = 360;
    public const int DefaultGifFromMs = 3400, MinGifFromMs = 1500, MaxGifFromMs = 5000;
    public const int DefaultSpiralLoomMs = 4200, MinHoldableMs = 1000;
    /// <summary>A hold ends by itself here, and no timed spiral or haze runs longer.</summary>
    public const int HoldCapMs = 20_000;
    public const double DefaultSpiralAlpha = 0.85;
    /// <summary>The haze is the user's BrainDrain blur at this fraction.</summary>
    public const double HazeLevel = 0.5;
    /// <summary>Calm (the mockup's reduced motion): strengths x0.5, fullscreen durations x0.6, gif-from dim x0.8.</summary>
    public const double CalmStrength = 0.5, CalmDuration = 0.6, CalmDim = 0.8;
    /// <summary>A page rect narrower or shorter than this reads as no rect.</summary>
    public const double MinFromPx = 8;

    /// <summary>Length of a word run, used for the "then" in a recipe.</summary>
    public static int WordsMs(int words) => Math.Max(0, words) * WordGapMs;

    public static readonly IReadOnlyList<string> KnownIds = new[]
    {
        "fx.jackpot", "fx.gif_storm", "fx.sub_cascade", "fx.spiral_full", "fx.spiral_brief",
        "fx.gif_burst", "fx.sub_pair", "fx.sub_single", "fx.melt",
        "fx.wash", "fx.gif_from", "fx.loom_spiral", "fx.haze",
    };

    public static string WireName(FxPrim p) => p switch
    {
        FxPrim.FlashBurst => "flash-burst",
        FxPrim.GifRain => "gif-rain",
        FxPrim.GlitchBubbles => "glitch-bubbles",
        FxPrim.SubSingle => "sub-single",
        FxPrim.SubSeq => "sub-seq",
        FxPrim.SubBurst9 => "sub-burst9",
        FxPrim.SpiralFull => "spiral-full",
        FxPrim.BrainDrainMelt => "brain-drain-melt",
        FxPrim.BrainDrain => "brain-drain",
        FxPrim.GifFull => "gif-full",
        FxPrim.Wash => "wash",
        FxPrim.GifFrom => "gif-from",
        FxPrim.SpiralLoom => "spiral-loom",
        FxPrim.Haze => "haze",
        _ => "unknown",
    };

    private static FxStep S(FxPrim p, int at, int count = 1, int ms = 0, double level = 1.0) => new(p, at, count, ms, level);
    private static FxRecipe R(params FxStep[] steps) => new(0, steps);
    private static FxRecipe Hero(int heroMs, params FxStep[] steps) => new(heroMs, steps);

    /// <summary>
    /// The table, verbatim from CONTRACT section 4. "+" in the contract is the same start, "then" is
    /// after the previous piece. <paramref name="wordCount"/> only feeds <c>fx.sub_single</c> (one
    /// word per subliminal showing). <paramref name="args"/> only feeds the Hypno v3 ids (10.13.B), which
    /// the page always sends at Normal values: Calm is applied here, once.
    /// </summary>
    public static FxRecipe? Recipe(string fxId, BackRoomFxIntensity intensity, int wordCount = 1, BackRoomFxArgs? args = null)
    {
        int b9 = WordsMs(9);
        var a = args ?? BackRoomFxArgs.None;
        bool calm = intensity == BackRoomFxIntensity.Calm;
        switch (fxId)
        {
            case "fx.jackpot":
                return intensity switch
                {
                    BackRoomFxIntensity.Calm => Hero(2000,
                        S(FxPrim.SpiralFull, 0, ms: 2000), S(FxPrim.GifFull, 0, ms: 1500), S(FxPrim.SubSingle, 0)),
                    BackRoomFxIntensity.Full => Hero(4000,
                        S(FxPrim.SpiralFull, 0, ms: 4000),
                        S(FxPrim.FlashBurst, 4000, 8), S(FxPrim.GifRain, 4000, ms: 4000),
                        S(FxPrim.GlitchBubbles, 4000, 3), S(FxPrim.SubBurst9, 4000, 9), S(FxPrim.SubBurst9, 4000 + b9, 9),
                        S(FxPrim.GifFull, 4000, ms: 2000)),
                    _ => Hero(2400,
                        S(FxPrim.SpiralFull, 0, ms: 2400),
                        S(FxPrim.FlashBurst, 2400, 4), S(FxPrim.GifRain, 2400, ms: 2000),
                        S(FxPrim.GlitchBubbles, 2400, 1), S(FxPrim.SubBurst9, 2400, 9)),
                };
            case "fx.gif_storm":
                return intensity switch
                {
                    BackRoomFxIntensity.Calm => R(S(FxPrim.FlashBurst, 0, 1)),
                    BackRoomFxIntensity.Full => R(S(FxPrim.FlashBurst, 0, 6), S(FxPrim.GifRain, 0, ms: 3500), S(FxPrim.GlitchBubbles, 0, 2)),
                    _ => R(S(FxPrim.FlashBurst, 0, 4), S(FxPrim.GifRain, 0, ms: 2000), S(FxPrim.GlitchBubbles, 0, 1)),
                };
            case "fx.sub_cascade":
                return intensity switch
                {
                    BackRoomFxIntensity.Calm => R(S(FxPrim.SubSeq, 0, 2), S(FxPrim.GifFull, 0, ms: 1500)),
                    BackRoomFxIntensity.Full => R(S(FxPrim.SubBurst9, 0, 9), S(FxPrim.GifFull, b9, ms: 2500)),
                    _ => R(S(FxPrim.SubBurst9, 0, 9), S(FxPrim.GifFull, b9, ms: 1500)),
                };
            case "fx.spiral_full":
                return intensity switch
                {
                    BackRoomFxIntensity.Calm => R(S(FxPrim.SpiralFull, 0, ms: 2500, level: 0.5)),
                    BackRoomFxIntensity.Full => R(S(FxPrim.SpiralFull, 0, ms: 4000)),
                    _ => R(S(FxPrim.SpiralFull, 0, ms: 2500)),
                };
            case "fx.spiral_brief":
                return intensity switch
                {
                    BackRoomFxIntensity.Calm => R(S(FxPrim.SpiralFull, 0, ms: 1200, level: 0.5)),
                    BackRoomFxIntensity.Full => R(S(FxPrim.SpiralFull, 0, ms: 2000)),
                    _ => R(S(FxPrim.SpiralFull, 0, ms: 1200)),
                };
            case "fx.gif_burst":
                return R(S(FxPrim.FlashBurst, 0, intensity switch
                {
                    BackRoomFxIntensity.Calm => 1,
                    BackRoomFxIntensity.Full => 3,
                    _ => 2,
                }));
            case "fx.sub_pair":
                return intensity switch
                {
                    BackRoomFxIntensity.Calm => R(S(FxPrim.SubSeq, 0, 2)),
                    BackRoomFxIntensity.Full => R(S(FxPrim.SubSeq, 0, 3), S(FxPrim.SpiralFull, WordsMs(3), ms: 2000)),
                    _ => R(S(FxPrim.SubSeq, 0, 2), S(FxPrim.SpiralFull, WordsMs(2), ms: 1200)),
                };
            case "fx.sub_single":
                // "sub-single per word", the same at every intensity. Each word is its own step so
                // the word gap is visible in the plan (and in the 6 Hz test).
                return R(Enumerable.Range(0, Math.Max(1, wordCount))
                    .Select(i => S(FxPrim.SubSingle, i * WordGapMs)).ToArray());
            case "fx.melt":
                return intensity switch
                {
                    BackRoomFxIntensity.Calm => R(S(FxPrim.BrainDrainMelt, 0, ms: 4000, level: 0.5)),
                    BackRoomFxIntensity.Full => R(S(FxPrim.BrainDrainMelt, 0, ms: 9000)),
                    _ => R(S(FxPrim.BrainDrainMelt, 0, ms: 6000)),
                };
            case "fx.wash":
            {
                double strength = Math.Clamp(a.Strength ?? DefaultWashStrength, 0.1, 1.0);
                return R(new FxStep(FxPrim.Wash, 0, DurationMs: WashMs, Level: WashPeak * strength * (calm ? CalmStrength : 1),
                    Look: new FxLook(FxColor.Safe(a.Color))));
            }
            case "fx.gif_from":
            {
                int ms = Math.Clamp(a.Ms ?? DefaultGifFromMs, MinGifFromMs, MaxGifFromMs);
                return R(new FxStep(FxPrim.GifFrom, 0, DurationMs: calm ? Calm(ms) : ms, Level: calm ? CalmDim : 1,
                    Look: new FxLook(FxColor.Safe(null), a.From, Math.Clamp(a.Scale ?? 1.0, 0.3, 1.0))));
            }
            case "fx.loom_spiral":
            {
                int ms = a.Hold ? HoldCapMs : Math.Clamp(a.Ms ?? DefaultSpiralLoomMs, MinHoldableMs, HoldCapMs);
                if (calm && !a.Hold) ms = Calm(ms);
                double alpha = Math.Clamp(a.Alpha ?? DefaultSpiralAlpha, 0.3, 0.9) * (calm ? CalmStrength : 1);
                return R(new FxStep(FxPrim.SpiralLoom, 0, DurationMs: ms, Level: alpha,
                    Look: new FxLook(FxColor.Safe(null), Preset: BackRoomSpiralSource.Preset(a.Preset), Hold: a.Hold)));
            }
            case "fx.haze":
                // Full only: the page-wide blur is the costliest thing in the mockup (10.13 law 7).
                if (intensity != BackRoomFxIntensity.Full) return R();
                return R(new FxStep(FxPrim.Haze, 0,
                    DurationMs: a.Hold ? HoldCapMs : Math.Clamp(a.Ms ?? DefaultSpiralLoomMs, MinHoldableMs, HoldCapMs),
                    Level: HazeLevel, Look: new FxLook(FxColor.Safe(null), Hold: a.Hold)));
            default:
                return null;
        }
    }

    private static int Calm(int ms) => (int)Math.Round(ms * CalmDuration);

    /// <summary>Calm also applies whenever MotionLevel is not Full, whatever the setting.</summary>
    public static BackRoomFxIntensity EffectiveIntensity(BackRoomFxIntensity setting, MotionLevel motion)
        => motion == MotionLevel.Full ? setting : BackRoomFxIntensity.Calm;

    /// <summary>
    /// Resolve <paramref name="fxId"/> into a plan. Order of the rules: intensity (Calm forced below
    /// Full motion), then the motion rules per primitive, then the feature toggles. Every primitive
    /// that does not play is reported once, with the first rule that removed it.
    /// </summary>
    /// <param name="spiralSource">Preset -> the woven spiral file (<see cref="BackRoomSpiralSource"/>), null
    /// when none exists. Null resolver = no weave anywhere, so spiral-full and spiral-loom skip <c>unknown</c>.</param>
    public static FxPlan Resolve(string fxId, BackRoomFxIntensity setting, MotionLevel motion, FxGates gates,
        IReadOnlyList<string>? symbolKeys, BackRoomMediaDeal? deal, Random rng,
        BackRoomFxArgs? args = null, Func<string, string?>? spiralSource = null)
    {
        var intensity = EffectiveIntensity(setting, motion);
        var media = ResolveSymbols(symbolKeys, deal, rng);
        var recipe = Recipe(fxId, intensity, media.Words.Count, args);
        if (recipe == null)
            return new FxPlan(fxId, intensity, 0, Array.Empty<FxPlannedStep>(),
                new[] { new BackRoomFxSkip(fxId, BackRoomFxSkipReason.Unknown) });

        var skipped = new List<BackRoomFxSkip>();
        if (fxId == "fx.haze" && recipe.Steps.Count == 0)
            skipped.Add(new BackRoomFxSkip(WireName(FxPrim.Haze), BackRoomFxSkipReason.Calm));
        else if (intensity == BackRoomFxIntensity.Calm)
        {
            // Report what Calm left out, measured against the design-doc (Normal) recipe.
            var calmPrims = recipe.Steps.Select(s => s.Prim).ToHashSet();
            foreach (var p in Recipe(fxId, BackRoomFxIntensity.Normal, media.Words.Count)!.Steps
                         .Select(s => s.Prim).Distinct().Where(p => !calmPrims.Contains(p)))
                skipped.Add(new BackRoomFxSkip(WireName(p), BackRoomFxSkipReason.Calm));
        }

        var steps = new List<FxPlannedStep>();
        int wordCursor = 0;
        foreach (var authored in recipe.Steps)
        {
            var step = ApplyMotion(authored, motion, gates, out var motionSkip);
            if (step == null) { AddOnce(skipped, authored.Prim, motionSkip); continue; }

            // 10.14: a melt is the room's melt switch alone, the still blur it becomes at Off included.
            if (authored.Prim == FxPrim.BrainDrainMelt ? !gates.Melt : !ToggleAllows(step.Prim, gates))
            {
                AddOnce(skipped, authored.Prim, BackRoomFxSkipReason.Toggle);
                continue;
            }
            // The drip went, the blur stays (MotionLevel Off): report the melt itself, with the rule that took it.
            if (authored.Prim == FxPrim.BrainDrainMelt && step.Prim == FxPrim.BrainDrain)
                AddOnce(skipped, FxPrim.BrainDrainMelt, BackRoomFxSkipReason.Motion);

            IReadOnlyList<string> words = Array.Empty<string>();
            BackRoomGif? gif = null;
            string? spiral = null;
            if (IsWordPrim(step.Prim))
            {
                if (media.AllWords.Count == 0) { AddOnce(skipped, step.Prim, BackRoomFxSkipReason.Unknown); continue; }
                words = TakeWords(media, step.Prim == FxPrim.SubSingle ? 1 : step.Count, ref wordCursor);
            }
            else if (step.Prim is FxPrim.GifFull or FxPrim.GifFrom)
            {
                gif = media.Gifs.Count > 0 ? media.Gifs[0] : Pick(deal?.Gifs, rng);
                if (gif == null) { AddOnce(skipped, step.Prim, BackRoomFxSkipReason.Unknown); continue; }
            }
            else if (step.Prim == FxPrim.Wash)
            {
                // The picture is optional: only one the page named rides along.
                gif = media.Gifs.Count > 0 ? media.Gifs[0] : null;
            }
            else if (step.Prim is FxPrim.SpiralFull or FxPrim.SpiralLoom)
            {
                spiral = spiralSource?.Invoke(step.Look?.Preset ?? BackRoomSpiralSource.Screen);
                if (spiral == null) { AddOnce(skipped, step.Prim, BackRoomFxSkipReason.Unknown); continue; }
            }
            steps.Add(new FxPlannedStep(step, words, gif, spiral));
        }

        return new FxPlan(fxId, intensity, recipe.HeroMs, steps, skipped);
    }

    /// <summary>The per-primitive motion rules. Null means the primitive is dropped at this level.</summary>
    internal static FxStep? ApplyMotion(FxStep s, MotionLevel motion, FxGates gates, out BackRoomFxSkipReason why)
    {
        why = BackRoomFxSkipReason.Motion;
        bool off = motion == MotionLevel.Off;
        switch (s.Prim)
        {
            case FxPrim.FlashBurst:
                return off ? s with { Count = 1 } : s;
            case FxPrim.GifRain:
                return motion == MotionLevel.Full ? s : null;
            case FxPrim.GlitchBubbles:
                return off ? null : s;
            case FxPrim.SubSeq:
            case FxPrim.SubBurst9:
                // Reduced -> sub-seq 2. Off is at least as strict as Reduced.
                return motion == MotionLevel.Full ? s : s with { Prim = FxPrim.SubSeq, Count = Math.Min(2, s.Count) };
            case FxPrim.SpiralFull:
                if (!off) return s;
                return gates.SpiralStill ? s with { Still = true } : null;
            case FxPrim.BrainDrainMelt:
                return off ? s with { Prim = FxPrim.BrainDrain } : s;
            case FxPrim.GifFull:
            case FxPrim.GifFrom:     // Off: no growth, full size with a 300 ms fade, still frame
            case FxPrim.SpiralLoom:  // Off: the woven GIF's first frame
                return off ? s with { Still = true } : s;
            default:
                // Wash (not motion) and Haze (already the no-drip blur) play at every level.
                return s;
        }
    }

    internal static bool ToggleAllows(FxPrim p, FxGates g) => p switch
    {
        FxPrim.FlashBurst or FxPrim.GifRain or FxPrim.GlitchBubbles or FxPrim.GifFull or FxPrim.Wash or FxPrim.GifFrom => g.Flash,
        FxPrim.SubSingle or FxPrim.SubSeq or FxPrim.SubBurst9 => g.Subliminal,
        FxPrim.SpiralFull or FxPrim.SpiralLoom => g.Spiral,
        FxPrim.BrainDrainMelt => g.Melt,
        FxPrim.BrainDrain or FxPrim.Haze => g.BrainDrain,
        _ => false,
    };

    private static bool IsWordPrim(FxPrim p) => p is FxPrim.SubSingle or FxPrim.SubSeq or FxPrim.SubBurst9;

    private static void AddOnce(List<BackRoomFxSkip> list, FxPrim p, BackRoomFxSkipReason why)
    {
        var name = WireName(p);
        if (!list.Any(x => x.Prim == name)) list.Add(new BackRoomFxSkip(name, why));
    }

    // ---- symbols -------------------------------------------------------------------------------

    /// <summary>Media a plan draws on: the words and GIFs the page named (resolved through the deal),
    /// and the deal's full word list for runs longer than what was named.</summary>
    public sealed record FxMedia(IReadOnlyList<string> Words, IReadOnlyList<BackRoomGif> Gifs, IReadOnlyList<string> AllWords);

    /// <summary>
    /// Resolve symbol keys ONLY through the host's own deal. Accepts tape symbol ids (<c>gif0..gif12</c>,
    /// <c>sub0..sub3</c>) and deal keys (<c>g0..g12</c>, <c>s0</c>), one or two digits (10.13.C). Non-media symbols (<c>spiral*</c>,
    /// <c>emi*</c>, <c>melt</c>) are ignored. Anything else, including an index past the deal, is
    /// replaced with a random dealt item: nothing the page sends is ever used as text or a path.
    /// </summary>
    public static FxMedia ResolveSymbols(IReadOnlyList<string>? keys, BackRoomMediaDeal? deal, Random rng)
    {
        var words = new List<string>();
        var gifs = new List<BackRoomGif>();
        var dealWords = deal?.Words?.Where(w => w != null && !string.IsNullOrWhiteSpace(w.Text)).ToList() ?? new List<BackRoomWord>();
        var dealGifs = deal?.Gifs?.Where(g => g != null).ToList() ?? new List<BackRoomGif>();

        foreach (var raw in (keys ?? Array.Empty<string>()).Take(MaxSymbolKeys))
        {
            var key = raw ?? string.Empty;
            if (key.StartsWith("spiral", StringComparison.Ordinal) || key.StartsWith("emi", StringComparison.Ordinal) || key == "melt")
                continue;

            if (TryIndex(key, "sub", out int si) || TryIndex(key, "s", out si))
            {
                var w = si < dealWords.Count ? dealWords[si] : Pick(dealWords, rng);
                if (w != null) words.Add(w.Text);
            }
            else if (TryIndex(key, "gif", out int gi) || TryIndex(key, "g", out gi))
            {
                var g = gi < dealGifs.Count ? dealGifs[gi] : Pick(dealGifs, rng);
                if (g != null) gifs.Add(g);
            }
            else if (rng.Next(2) == 0 && dealWords.Count > 0)
            {
                words.Add(Pick(dealWords, rng)!.Text);
            }
            else
            {
                var g = Pick(dealGifs, rng);
                if (g != null) gifs.Add(g);
                else if (dealWords.Count > 0) words.Add(Pick(dealWords, rng)!.Text);
            }
        }
        return new FxMedia(words, gifs, dealWords.Select(w => w.Text).ToList());
    }

    /// <summary>One or two digits after the prefix, no leading zero on two (<c>g12</c> yes, <c>g05</c> no).</summary>
    internal static bool TryIndex(string key, string prefix, out int index)
    {
        index = -1;
        int digits = key.Length - prefix.Length;
        if (!key.StartsWith(prefix, StringComparison.Ordinal) || digits is < 1 or > 2) return false;
        int value = 0;
        for (int i = prefix.Length; i < key.Length; i++)
        {
            char c = key[i];
            if (c < '0' || c > '9') return false;
            value = value * 10 + (c - '0');
        }
        if (digits == 2 && key[prefix.Length] == '0') return false;
        index = value;
        return true;
    }

    private static T? Pick<T>(IReadOnlyList<T>? list, Random rng) where T : class
        => list == null || list.Count == 0 ? null : list[rng.Next(list.Count)];

    /// <summary>Named words first, then the deal's words in order, cycling. Consecutive runs in one
    /// fx continue where the last one stopped, so a double burst does not repeat itself.</summary>
    private static IReadOnlyList<string> TakeWords(FxMedia media, int count, ref int cursor)
    {
        var pool = media.Words.Concat(media.AllWords.Where(w => !media.Words.Contains(w))).ToList();
        if (pool.Count == 0) return Array.Empty<string>();
        var result = new List<string>(count);
        for (int i = 0; i < Math.Max(1, count); i++) result.Add(pool[(cursor + i) % pool.Count]);
        cursor += Math.Max(1, count);
        return result;
    }
}
