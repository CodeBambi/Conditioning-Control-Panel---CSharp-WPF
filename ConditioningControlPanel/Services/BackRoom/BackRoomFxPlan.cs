using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.BackRoom;

// THE BACK ROOM, effects half one: the pure recipe table (CONTRACT section 4). Nothing in this file
// touches a service, a window or a clock, so every rule the host applies to an fx id - intensity,
// reduced motion, symbol resolution - is decided here and pinned by BackRoomFxPlanTests. The
// executor (BackRoomFx) only walks the plan this produces.
//
// The Back Room is an AUTHORED show (owner direction, 2026-09-15): every fx id always plays its full
// recipe. The app's Flash / Subliminal / Spiral / Brain Drain toggles and the room's own switches no
// longer gate anything here. Exactly two safety lines remain, and neither one ever skips a step:
//   (a) reduced motion (MotionLevel below Full, which is also where the OS animation flag lands) caps
//       flash onsets at 3 Hz and plays the slower spiral variant;
//   (b) Calm intensity is "gentle": every opacity and strength is halved, every duration is kept.
// Full keeps the counts and stretches the durations by 1.3.

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
    GifFull,
    /// <summary>Hypno v3 (10.13.B): a soft colour wash over the virtual screen, optionally with a picture.</summary>
    Wash,
    /// <summary>Hypno v3: a dealt picture growing out of a page rect to cover the room window's screen.</summary>
    GifFrom,
    /// <summary>Hypno v3: a Loom-woven spiral GIF over the virtual screen. <see cref="SpiralFull"/> plays through it too.</summary>
    SpiralLoom,
    /// <summary>Hypno v3: the app's BrainDrain blur without the drip.</summary>
    Haze,
}

/// <summary>
/// One authored step. <paramref name="AtMs"/> is the offset from the fx start. <paramref name="Count"/>
/// is the flash amount, the rain's GIF count, the glitch pulse count or the word count.
/// <paramref name="Level"/> is the authored opacity (flash image, rain GIF, glitch pulse, word card,
/// spiral alpha, gif-full, the melt's peak alpha, the wash peak), already halved under Calm. Two
/// exceptions keep their older meaning: for <see cref="FxPrim.GifFrom"/> it is the backdrop dim factor
/// and for <see cref="FxPrim.Haze"/> the fraction of the user's own BrainDrain intensity.
/// <paramref name="Look"/> carries the rest of a Hypno v3 step's validated <c>args</c>.
/// </summary>
public sealed record FxStep(FxPrim Prim, int AtMs, int Count = 1, int DurationMs = 0, double Level = 1.0, FxLook? Look = null);

/// <summary>The validated <c>args</c> of one Hypno v3 step (CONTRACT 10.13.B, host validation column).</summary>
/// <param name="From">Null = grow from the centre (a missing or bad rect).</param>
/// <param name="Hold">Stays until <c>fx-release</c> or the 20 s cap.</param>
public sealed record FxLook(FxRgb Color, FxCssRect? From = null, double Scale = 1.0, string Preset = BackRoomSpiralSource.Screen, bool Hold = false);

/// <summary>A recipe: its steps and, for a hero, how long the hero window holds the stage.</summary>
public sealed record FxRecipe(int HeroMs, IReadOnlyList<FxStep> Steps)
{
    public bool IsHero => HeroMs > 0;
}

/// <summary>A resolved step with the media it will use (words for sub primitives, one GIF for gif-full and
/// gif-from, an optional picture for a wash, the woven file for spiral-full and spiral-loom).</summary>
public sealed record FxPlannedStep(FxStep Step, IReadOnlyList<string> Words, BackRoomGif? Gif, string? SpiralPath = null);

/// <summary>What one fx id will do, plus every skip for the ack (only <c>unknown</c> comes out of the resolver:
/// an id the host does not know, or media it does not have).</summary>
/// <param name="Reduced">Motion below Full: flash onsets are capped at 3 Hz and spirals play their slower variant.</param>
public sealed record FxPlan(string FxId, BackRoomFxIntensity Intensity, bool Reduced, int HeroMs,
    IReadOnlyList<FxPlannedStep> Steps, IReadOnlyList<BackRoomFxSkip> Skipped)
{
    public bool IsHero => HeroMs > 0;
    public IReadOnlyList<string> Fired => Steps.Select(s => BackRoomFxPlan.WireName(s.Step.Prim)).ToList();
}

public static class BackRoomFxPlan
{
    // ---- the word envelope: a word is VISIBLE, then FADES (never a blink) ----
    /// <summary>A word card fades in over this.</summary>
    public const int WordFadeInMs = 80;
    /// <summary>...holds this long at its opacity...</summary>
    public const int WordHoldMs = 400;
    /// <summary>...and fades out over this.</summary>
    public const int WordFadeOutMs = 350;
    /// <summary>One word's whole envelope on screen.</summary>
    public const int WordMs = WordFadeInMs + WordHoldMs + WordFadeOutMs;
    /// <summary>Word onsets in a single and a pair (2 Hz).</summary>
    public const int WordGapMs = 500;
    /// <summary>Word onsets in a nine-word burst (under 3 Hz).</summary>
    public const int BurstGapMs = 350;

    /// <summary>One fullscreen glitch pulse: this long on the overlay, at <see cref="GlitchOpacity"/>. Pulses in a
    /// step come this far apart.</summary>
    public const int GlitchPulseMs = 600;
    public const double GlitchOpacity = 0.35;

    /// <summary>A flash-burst's images come this far apart (the burst passes the gap to FlashService), so a burst of
    /// n images is n onsets spread over <c>(n - 1) * FlashImageGapMs</c>.</summary>
    public const int FlashImageGapMs = 300;
    /// <summary><c>fx.gif_burst</c> and <c>fx.gif_storm</c>: this many flashes; a burst's <c>args.count</c> may ask for 1..<see cref="MaxBurstFlashes"/>.</summary>
    public const int BurstFlashes = 5, MaxBurstFlashes = 8;
    /// <summary>Safety line (a): under reduced motion flash onsets are capped at 3 Hz.</summary>
    public const int ReducedFlashGapMs = 334;
    /// <summary>The flash image size the room asks for: FlashService's ImageScale percent at which an image is
    /// 40% of the monitor (medium, whatever the user's own slider says).</summary>
    public const int FlashSize = 100;
    /// <summary>Flash images play at full opacity, whatever the user's own slider says.</summary>
    public const double FlashOpacity = 1.0;

    /// <summary>The rain's per-GIF opacity.</summary>
    public const double RainOpacity = 0.9;
    /// <summary>Rain density: 14 GIFs over 3 s (<c>fx.gif_storm</c>); the jackpot's 4 s rain keeps the density.</summary>
    public const int StormRainGifs = 14, StormRainMs = 3000, JackpotRainGifs = 19, JackpotRainMs = 4000;

    /// <summary>Spiral alphas: the brief one (and the pair's tail) and the full one.</summary>
    public const double SpiralBriefAlpha = 0.55, SpiralFullAlpha = 0.7;
    public const int SpiralBriefMs = 1500, SpiralFullMs = 4000;

    /// <summary>gif-full: the cascade's tail and the jackpot's picture.</summary>
    public const double GifFullOpacity = 0.8;

    /// <summary>The melt: its alpha ramps from 0 to this over the step, then lets go.</summary>
    public const double MeltAlpha = 0.8;
    public const int MeltMs = 6000;

    /// <summary>The jackpot hero: the spiral alone for this long, then everything else at once.</summary>
    public const int JackpotHeroMs = 4000;
    public const int JackpotFlashes = 8, JackpotGlitches = 3;

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
    /// <summary>Safety line (b), Calm is gentle: every opacity and strength x0.5, every duration kept.</summary>
    public const double CalmStrength = 0.5;
    /// <summary>Full: the same counts, every authored duration x1.3.</summary>
    public const double FullDuration = 1.3;
    /// <summary>A page rect narrower or shorter than this reads as no rect.</summary>
    public const double MinFromPx = 8;

    /// <summary>The onset gap of a word primitive.</summary>
    public static int WordGap(FxPrim p) => p == FxPrim.SubBurst9 ? BurstGapMs : WordGapMs;

    /// <summary>Length of a word run, used for the "then" in a recipe: the last onset plus the envelope.</summary>
    public static int RunMs(int words, int gapMs) => words <= 0 ? 0 : (words - 1) * gapMs + WordMs;

    /// <summary>How long one step keeps the screen busy from its onset (the dev rig's grab schedule).</summary>
    public static int StepMs(FxStep s) => s.Prim switch
    {
        FxPrim.SubSingle or FxPrim.SubSeq or FxPrim.SubBurst9 => RunMs(Math.Max(1, s.Count), WordGap(s.Prim)),
        FxPrim.GlitchBubbles => Math.Max(1, s.Count) * GlitchPulseMs,
        FxPrim.FlashBurst => (Math.Max(1, s.Count) - 1) * FlashImageGapMs,
        _ => s.DurationMs,
    };

    /// <summary>The whole recipe's length from its start.</summary>
    public static int LengthMs(FxRecipe r) => r.Steps.Count == 0 ? 0 : r.Steps.Max(s => s.AtMs + StepMs(s));

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
        FxPrim.GifFull => "gif-full",
        FxPrim.Wash => "wash",
        FxPrim.GifFrom => "gif-from",
        FxPrim.SpiralLoom => "spiral-loom",
        FxPrim.Haze => "haze",
        _ => "unknown",
    };

    private static FxRecipe R(params FxStep[] steps) => new(0, steps);

    /// <summary>
    /// The table (CONTRACT section 4, authored 2026-09-15). "+" is the same start, "then" is after the
    /// previous piece. <paramref name="wordCount"/> only feeds <c>fx.sub_single</c> (one word per subliminal
    /// showing). <paramref name="args"/>: <c>count</c> for <c>fx.gif_burst</c>, <c>wordsShown</c> for the three
    /// word ids (the page drew the words itself: the word steps are left out, the rest keeps its authored
    /// offset), and the Hypno v3 fields (10.13.B), which the page always sends at Normal values. One authored
    /// row per id: Calm halves every <c>Level</c>, Full stretches every duration (word onsets, flash gaps and
    /// glitch pulses are pacing, not durations, and never stretch; the Hypno v3 durations are the page's own
    /// and are kept as sent).
    /// </summary>
    public static FxRecipe? Recipe(string fxId, BackRoomFxIntensity intensity, int wordCount = 1, BackRoomFxArgs? args = null)
    {
        var a = args ?? BackRoomFxArgs.None;
        bool calm = intensity == BackRoomFxIntensity.Calm, full = intensity == BackRoomFxIntensity.Full;
        int D(int ms) => full ? (int)Math.Round(ms * FullDuration) : ms;
        double L(double level) => calm ? level * CalmStrength : level;
        FxStep S(FxPrim p, int at, int count = 1, int ms = 0, double level = 1.0) => new(p, at, count, ms, L(level));

        switch (fxId)
        {
            case "fx.jackpot":
            {
                int t = D(JackpotHeroMs);
                return new FxRecipe(t, new[]
                {
                    S(FxPrim.SpiralFull, 0, ms: t, level: SpiralFullAlpha),
                    S(FxPrim.FlashBurst, t, JackpotFlashes, level: FlashOpacity),
                    S(FxPrim.GifRain, t, JackpotRainGifs, D(JackpotRainMs), RainOpacity),
                    S(FxPrim.GlitchBubbles, t, JackpotGlitches, level: GlitchOpacity),
                    S(FxPrim.SubBurst9, t, 9),
                    S(FxPrim.SubBurst9, t + 9 * BurstGapMs, 9),
                    S(FxPrim.GifFull, t, ms: D(2000), level: GifFullOpacity),
                });
            }
            case "fx.gif_storm":
                return R(S(FxPrim.FlashBurst, 0, BurstFlashes, level: FlashOpacity),
                    S(FxPrim.GifRain, 0, StormRainGifs, D(StormRainMs), RainOpacity),
                    S(FxPrim.GlitchBubbles, 0, 1, level: GlitchOpacity));
            case "fx.sub_cascade":
            {
                var tail = S(FxPrim.GifFull, RunMs(9, BurstGapMs), ms: D(1500), level: GifFullOpacity);
                return a.WordsShown ? R(tail) : R(S(FxPrim.SubBurst9, 0, 9), tail);
            }
            case "fx.spiral_full":
                return R(S(FxPrim.SpiralFull, 0, ms: D(SpiralFullMs), level: SpiralFullAlpha));
            case "fx.spiral_brief":
                return R(S(FxPrim.SpiralFull, 0, ms: D(SpiralBriefMs), level: SpiralBriefAlpha));
            case "fx.gif_burst":
                return R(S(FxPrim.FlashBurst, 0, Math.Clamp(a.Count ?? BurstFlashes, 1, MaxBurstFlashes), level: FlashOpacity));
            case "fx.sub_pair":
            {
                var tail = S(FxPrim.SpiralFull, RunMs(2, WordGapMs), ms: D(SpiralBriefMs), level: SpiralBriefAlpha);
                return a.WordsShown ? R(tail) : R(S(FxPrim.SubSeq, 0, 2), tail);
            }
            case "fx.sub_single":
                // "sub-single per word", the same at every intensity. Each word is its own step so
                // the word gap is visible in the plan (and in the 6 Hz test). Nothing at all when the
                // page drew the words itself.
                if (a.WordsShown) return R();
                return R(Enumerable.Range(0, Math.Max(1, wordCount))
                    .Select(i => S(FxPrim.SubSingle, i * WordGapMs)).ToArray());
            case "fx.melt":
                return R(S(FxPrim.BrainDrainMelt, 0, ms: D(MeltMs), level: MeltAlpha));
            case "fx.wash":
            {
                double strength = Math.Clamp(a.Strength ?? DefaultWashStrength, 0.1, 1.0);
                return R(new FxStep(FxPrim.Wash, 0, DurationMs: WashMs, Level: L(WashPeak * strength),
                    Look: new FxLook(FxColor.Safe(a.Color))));
            }
            case "fx.gif_from":
            {
                int ms = Math.Clamp(a.Ms ?? DefaultGifFromMs, MinGifFromMs, MaxGifFromMs);
                return R(new FxStep(FxPrim.GifFrom, 0, DurationMs: ms, Level: L(1.0),
                    Look: new FxLook(FxColor.Safe(null), a.From, Math.Clamp(a.Scale ?? 1.0, 0.3, 1.0))));
            }
            case "fx.loom_spiral":
            {
                int ms = a.Hold ? HoldCapMs : Math.Clamp(a.Ms ?? DefaultSpiralLoomMs, MinHoldableMs, HoldCapMs);
                double alpha = L(Math.Clamp(a.Alpha ?? DefaultSpiralAlpha, 0.3, 0.9));
                return R(new FxStep(FxPrim.SpiralLoom, 0, DurationMs: ms, Level: alpha,
                    Look: new FxLook(FxColor.Safe(null), Preset: BackRoomSpiralSource.Preset(a.Preset), Hold: a.Hold)));
            }
            case "fx.haze":
                return R(new FxStep(FxPrim.Haze, 0,
                    DurationMs: a.Hold ? HoldCapMs : Math.Clamp(a.Ms ?? DefaultSpiralLoomMs, MinHoldableMs, HoldCapMs),
                    Level: L(HazeLevel), Look: new FxLook(FxColor.Safe(null), Hold: a.Hold)));
            default:
                return null;
        }
    }

    /// <summary>
    /// Resolve <paramref name="fxId"/> into a plan. Nothing is ever skipped for a setting: the only skips are
    /// <c>unknown</c> (an id this host does not know, a word primitive with no dealt words, a picture with no
    /// dealt GIF, a spiral with no weave). Motion below Full only marks the plan <see cref="FxPlan.Reduced"/>.
    /// </summary>
    /// <param name="spiralSource">Preset -> the woven spiral file (<see cref="BackRoomSpiralSource"/>), null
    /// when none exists. Null resolver = no weave anywhere, so spiral-full and spiral-loom skip <c>unknown</c>.</param>
    public static FxPlan Resolve(string fxId, BackRoomFxIntensity intensity, MotionLevel motion,
        IReadOnlyList<string>? symbolKeys, BackRoomMediaDeal? deal, Random rng,
        BackRoomFxArgs? args = null, Func<string, string?>? spiralSource = null)
    {
        bool reduced = motion != MotionLevel.Full;
        var media = ResolveSymbols(symbolKeys, deal, rng);
        var recipe = Recipe(fxId, intensity, media.Words.Count, args);
        if (recipe == null)
            return new FxPlan(fxId, intensity, reduced, 0, Array.Empty<FxPlannedStep>(),
                new[] { new BackRoomFxSkip(fxId, BackRoomFxSkipReason.Unknown) });

        var skipped = new List<BackRoomFxSkip>();
        var steps = new List<FxPlannedStep>();
        int wordCursor = 0;
        foreach (var step in recipe.Steps)
        {
            IReadOnlyList<string> words = Array.Empty<string>();
            BackRoomGif? gif = null;
            string? spiral = null;
            if (IsWordPrim(step.Prim))
            {
                if (media.AllWords.Count == 0) { AddOnce(skipped, step.Prim); continue; }
                words = TakeWords(media, step.Prim == FxPrim.SubSingle ? 1 : step.Count, ref wordCursor);
            }
            else if (step.Prim is FxPrim.GifFull or FxPrim.GifFrom)
            {
                gif = media.Gifs.Count > 0 ? media.Gifs[0] : Pick(deal?.Gifs, rng);
                if (gif == null) { AddOnce(skipped, step.Prim); continue; }
            }
            else if (step.Prim == FxPrim.Wash)
            {
                // The picture is optional: only one the page named rides along.
                gif = media.Gifs.Count > 0 ? media.Gifs[0] : null;
            }
            else if (step.Prim is FxPrim.SpiralFull or FxPrim.SpiralLoom)
            {
                spiral = spiralSource?.Invoke(step.Look?.Preset ?? BackRoomSpiralSource.Screen);
                if (spiral == null) { AddOnce(skipped, step.Prim); continue; }
            }
            steps.Add(new FxPlannedStep(step, words, gif, spiral));
        }

        return new FxPlan(fxId, intensity, reduced, recipe.HeroMs, steps, skipped);
    }

    private static bool IsWordPrim(FxPrim p) => p is FxPrim.SubSingle or FxPrim.SubSeq or FxPrim.SubBurst9;

    private static void AddOnce(List<BackRoomFxSkip> list, FxPrim p)
    {
        var name = WireName(p);
        if (!list.Any(x => x.Prim == name)) list.Add(new BackRoomFxSkip(name, BackRoomFxSkipReason.Unknown));
    }

    // ---- symbols -------------------------------------------------------------------------------

    /// <summary>Media a plan draws on: the words and GIFs the page named (resolved through the deal),
    /// and the deal's full word list for runs longer than what was named.</summary>
    public sealed record FxMedia(IReadOnlyList<string> Words, IReadOnlyList<BackRoomGif> Gifs, IReadOnlyList<string> AllWords);

    /// <summary>
    /// Resolve symbol keys ONLY through the host's own deal. Accepts tape symbol ids (<c>gif0..gif12</c>,
    /// <c>sub0..sub3</c>) and deal keys (<c>g0..g12</c>, <c>s0</c>), one or two digits (10.13.C). Non-media symbols (<c>spiral*</c>,
    /// <c>emi*</c>, <c>melt</c>) are ignored. A GIF index past the deal cycles (<c>gifs[i % n]</c>, 10.14: the player's
    /// own GIFs are cycled, never padded), exactly as the pages draw it. Anything else, including a word index past the
    /// deal, is replaced with a random dealt item: nothing the page sends is ever used as text or a path.
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
                var g = dealGifs.Count > 0 ? dealGifs[gi % dealGifs.Count] : null;
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
