using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.BackRoom;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE BACK ROOM Hypno v3 recipes (CONTRACT 10.13.B) under the authored show: the four Hypno ids at Calm
/// (half strength, same durations) / Normal / Full, the host validation of <c>args</c>, the never-white
/// colour rule, two-digit symbol keys and the Loom-woven spiral source. All pure.
/// </summary>
public class BackRoomHypnoPlanTests
{
    /// <summary>A 13-GIF sit-down (cards), keys g0..g12.</summary>
    private static readonly BackRoomMediaDeal Deck = new(3,
        Enumerable.Range(0, 13).Select(i => new BackRoomGif("g" + i, $"https://ccp.assets/images/{i}.gif", 480, 270, "pool")).ToList(),
        new[] { new BackRoomWord("s0", "Drop", "preset") });

    private static FxPlan Plan(string fx, object? args = null, BackRoomFxIntensity i = BackRoomFxIntensity.Normal,
        MotionLevel m = MotionLevel.Full, Func<string, string?>? source = null, params string[] symbols)
        => BackRoomFxPlan.Resolve(fx, i, m, symbols, Deck, new Random(1),
            BackRoomFxArgs.Parse(args == null ? null : JObject.FromObject(args)), source ?? BackRoomFxPlanTests.Woven);

    private static FxStep Step(FxPlan p) => Assert.Single(p.Steps).Step;

    // ---- wash ------------------------------------------------------------------------------------

    [Fact]
    public void Wash_Normal_IsPeak042TimesStrength_AndGoneAt900()
    {
        var s = Step(Plan("fx.wash", new { color = "#5fffd0", strength = 0.55 }));
        Assert.Equal((FxPrim.Wash, 900), (s.Prim, s.DurationMs));
        Assert.Equal(0.42 * 0.55, s.Level, 6);
        Assert.Equal(0.42 * 0.7, Step(Plan("fx.wash")).Level, 6);                              // default strength
        Assert.Equal(0.42 * 0.1, Step(Plan("fx.wash", new { strength = -3 })).Level, 6);       // clamped
        Assert.Equal(0.42, Step(Plan("fx.wash", new { strength = 9 }, BackRoomFxIntensity.Full)).Level, 6);
    }

    [Fact]
    public void Wash_Calm_HalvesThePeak_KeepsTheLength_AndReducedMotionChangesNothing()
    {
        var calm = Step(Plan("fx.wash", new { strength = 1 }, BackRoomFxIntensity.Calm));
        Assert.Equal(0.42 * 0.5, calm.Level, 6);
        Assert.Equal(900, calm.DurationMs);
        var off = Plan("fx.wash", new { strength = 1 }, m: MotionLevel.Off);
        Assert.Equal(new[] { "wash" }, off.Fired);
        Assert.Equal(0.42, Step(off).Level, 6);   // nothing forces Calm any more
        Assert.True(off.Reduced);
        Assert.Empty(off.Skipped);
    }

    [Fact]
    public void Wash_CarriesAPictureOnlyWhenNamed_TwoDigitKeysResolve()
    {
        Assert.Null(Assert.Single(Plan("fx.wash").Steps).Gif);
        Assert.Equal("g11", Assert.Single(Plan("fx.wash", symbols: "g11").Steps).Gif!.Key);
        Assert.Equal("g12", Assert.Single(Plan("fx.gif_from", symbols: "gif12").Steps).Gif!.Key);
    }

    // ---- gif-from --------------------------------------------------------------------------------

    [Fact]
    public void GifFrom_Args_AreClamped_AndTheRectRidesAlong()
    {
        var s = Step(Plan("fx.gif_from", new { from = new { x = 612, y = 188, w = 60, h = 44 }, ms = 3400, scale = 1 }));
        Assert.Equal((3400, 1.0, 1.0), (s.DurationMs, s.Level, s.Look!.Scale));
        Assert.Equal(new FxCssRect(612, 188, 60, 44), s.Look.From);

        Assert.Equal(1500, Step(Plan("fx.gif_from", new { ms = 10 })).DurationMs);
        Assert.Equal(5000, Step(Plan("fx.gif_from", new { ms = 99999 })).DurationMs);
        Assert.Equal(3400, Step(Plan("fx.gif_from")).DurationMs);
        Assert.Equal(0.3, Step(Plan("fx.gif_from", new { scale = 0.01 })).Look!.Scale);
        Assert.Equal(0.46, Step(Plan("fx.gif_from", new { scale = 0.46 })).Look!.Scale);
        Assert.Null(Step(Plan("fx.gif_from", new { from = new { x = 1, y = 1, w = 7, h = 44 } })).Look!.From);   // too small
    }

    [Fact]
    public void GifFrom_Calm_KeepsTheLength_DimsHalf_MotionNeverStillsIt()
    {
        var calm = Step(Plan("fx.gif_from", new { ms = 4000 }, BackRoomFxIntensity.Calm));
        Assert.Equal((4000, 0.5), (calm.DurationMs, calm.Level));
        Assert.Equal(4000, Step(Plan("fx.gif_from", new { ms = 4000 }, BackRoomFxIntensity.Full)).DurationMs);   // the page's own ms, kept
        Assert.Equal(new[] { "gif-from" }, Plan("fx.gif_from", m: MotionLevel.Off).Fired);
        Assert.Equal(new[] { "gif-from" }, Plan("fx.gif_from", m: MotionLevel.Reduced).Fired);
    }

    [Fact]
    public void GifFrom_WithNoDealtGif_IsUnknown()
    {
        var p = BackRoomFxPlan.Resolve("fx.gif_from", BackRoomFxIntensity.Normal, MotionLevel.Full,
            new[] { "g0" }, new BackRoomMediaDeal(0, Array.Empty<BackRoomGif>(), Array.Empty<BackRoomWord>()), new Random(0));
        Assert.Equal(new BackRoomFxSkip("gif-from", BackRoomFxSkipReason.Unknown), Assert.Single(p.Skipped));
    }

    // ---- spiral-loom and spiral-full -------------------------------------------------------------

    [Fact]
    public void LoomSpiral_HoldIsTheCap_AlphaClamped_PresetPicked()
    {
        var hold = Assert.Single(Plan("fx.loom_spiral", new { preset = "wake", hold = true, alpha = 0.65, ms = 1200 }).Steps);
        Assert.Equal((20000, 0.65, true, "wake"), (hold.Step.DurationMs, hold.Step.Level, hold.Step.Look!.Hold, hold.Step.Look.Preset));
        Assert.Equal(@"C:\woven\wake.gif", hold.SpiralPath);

        var timed = Step(Plan("fx.loom_spiral", new { preset = "../../evil", ms = 4200, alpha = 0.99 }));
        Assert.Equal((4200, 0.9, "screen"), (timed.DurationMs, timed.Level, timed.Look!.Preset));
        Assert.Equal(0.85, Step(Plan("fx.loom_spiral")).Level);
        Assert.Equal(20000, Step(Plan("fx.loom_spiral", new { ms = 60000 })).DurationMs);
    }

    [Fact]
    public void LoomSpiral_Calm_HalvesAlpha_KeepsTheTime_AndTheHoldCap()
    {
        var calm = Step(Plan("fx.loom_spiral", new { ms = 4200, alpha = 0.9 }, BackRoomFxIntensity.Calm));
        Assert.Equal((4200, 0.45), (calm.DurationMs, calm.Level));
        Assert.Equal(20000, Step(Plan("fx.loom_spiral", new { hold = true }, BackRoomFxIntensity.Calm)).DurationMs);
        // Reduced motion plays the slow variant (the plan's flag), never a still.
        Assert.True(Plan("fx.loom_spiral", m: MotionLevel.Off).Reduced);
        Assert.Equal(new[] { "spiral-loom" }, Plan("fx.loom_spiral", m: MotionLevel.Off).Fired);
    }

    [Fact]
    public void Spirals_WithNoWeave_AreUnknown()
    {
        Assert.Equal(new BackRoomFxSkip("spiral-loom", BackRoomFxSkipReason.Unknown),
            Assert.Single(Plan("fx.loom_spiral", source: _ => null).Skipped));
        Assert.Equal(new BackRoomFxSkip("spiral-full", BackRoomFxSkipReason.Unknown),
            Assert.Single(Plan("fx.spiral_full", source: _ => null).Skipped));
    }

    [Fact]
    public void SpiralFull_PlaysTheScreenWeave()
        => Assert.Equal(@"C:\woven\screen.gif", Assert.Single(Plan("fx.spiral_full").Steps).SpiralPath);

    // ---- haze ------------------------------------------------------------------------------------

    [Theory]
    [InlineData(BackRoomFxIntensity.Calm, 0.25)]
    [InlineData(BackRoomFxIntensity.Normal, 0.5)]
    [InlineData(BackRoomFxIntensity.Full, 0.5)]
    public void Haze_PlaysAtEveryIntensity_HalfTheBlur_QuarterUnderCalm_HeldToTheCap(BackRoomFxIntensity i, double level)
    {
        var p = Plan("fx.haze", new { hold = true }, i);
        Assert.Equal(new[] { "haze" }, p.Fired);
        Assert.Empty(p.Skipped);
        var s = Step(p);
        Assert.Equal((FxPrim.Haze, 20000, true), (s.Prim, s.DurationMs, s.Look!.Hold));
        Assert.Equal(level, s.Level, 9);
        Assert.Equal(3000, Step(Plan("fx.haze", new { ms = 3000 }, i)).DurationMs);   // the page's ms, kept at every intensity
    }

    [Fact]
    public void NewIds_AreNeverHeroes()
    {
        foreach (var id in new[] { "fx.wash", "fx.gif_from", "fx.loom_spiral", "fx.haze" })
            foreach (BackRoomFxIntensity i in Enum.GetValues(typeof(BackRoomFxIntensity)))
                Assert.False(BackRoomFxPlan.Recipe(id, i)!.IsHero);
    }

    // ---- args, colour, keys, source --------------------------------------------------------------

    [Fact]
    public void Args_KeepOnlyWellTypedFiniteValues()
    {
        var a = BackRoomFxArgs.Parse(JObject.Parse(
            """{"color":7,"strength":"1","from":{"x":1,"y":2,"w":"60","h":44},"ms":3.6,"scale":null,"preset":["wake"],"hold":"true","alpha":0.5}"""));
        Assert.Equal(new BackRoomFxArgs(Ms: 4, Alpha: 0.5), a);
        // count: an integer only; wordsShown: a boolean only.
        Assert.Equal(new BackRoomFxArgs(Count: 3, WordsShown: true), BackRoomFxArgs.Parse(JObject.Parse("{\"count\":3,\"wordsShown\":true}")));
        Assert.Equal(BackRoomFxArgs.None, BackRoomFxArgs.Parse(JObject.Parse("{\"count\":\"3\",\"wordsShown\":\"true\"}")));
        Assert.Equal(BackRoomFxArgs.None, BackRoomFxArgs.Parse(JObject.Parse("{\"count\":2.5,\"wordsShown\":1}")));
        Assert.Equal(new BackRoomFxArgs(Count: -7), BackRoomFxArgs.Parse(JObject.Parse("{\"count\":-7,\"wordsShown\":false}")));   // the plan clamps
        Assert.Same(BackRoomFxArgs.None, BackRoomFxArgs.Parse(new JArray()));
        Assert.Same(BackRoomFxArgs.None, BackRoomFxArgs.Parse(null));
        Assert.Equal(new FxCssRect(-5, 0, 8, 8), BackRoomFxArgs.Parse(JObject.Parse("""{"from":{"x":-5,"y":0,"w":8,"h":8}}""")).From);
    }

    [Theory]
    [InlineData("#ffffff")]
    [InlineData("#FFFFF0")]
    [InlineData("#e8c27a")]
    [InlineData("#5fffd0")]
    [InlineData("#808080")]
    [InlineData("#000000")]
    public void Colour_IsNeverWhite_NorGrey(string hex)
    {
        var c = FxColor.Safe(hex);
        var (l, s) = Hsl(c);
        Assert.True(l <= FxColor.MaxLightness + 0.01, $"{hex} -> {c.Hex} lightness {l}");
        Assert.True(s >= FxColor.MinSaturation - 0.01 || l < 0.01, $"{hex} -> {c.Hex} saturation {s}");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("url(C:/x)")]
    public void Colour_BadInput_IsTheVioletDefault(string? hex)
        => Assert.Equal(FxColor.Safe("#9b6bff"), FxColor.Safe(hex));

    [Fact]
    public void Colour_AlreadySafe_IsUnchanged()
    {
        Assert.Equal("#9b6bff", FxColor.Safe("#9b6bff").Hex);
        Assert.Equal("#ff5fa2", FxColor.Safe("#FF5FA2").Hex);
    }

    [Theory]
    [InlineData("g0", "g", 0)]
    [InlineData("g12", "g", 12)]
    [InlineData("gif12", "gif", 12)]
    [InlineData("s3", "s", 3)]
    [InlineData("g05", "g", -1)]
    [InlineData("g123", "g", -1)]
    [InlineData("g", "g", -1)]
    [InlineData("gx", "g", -1)]
    public void SymbolKeys_TakeOneOrTwoDigits(string key, string prefix, int want)
    {
        bool ok = BackRoomFxPlan.TryIndex(key, prefix, out int got);
        Assert.Equal(want >= 0, ok);
        if (ok) Assert.Equal(want, got);
    }

    [Fact]
    public void SpiralSource_PlayersOwnWeaveFirst_ThenTheBundledPreset()
    {
        var spirals = Path.Combine(Path.GetTempPath(), "ccp-spirals");
        var web = Path.Combine(Path.GetTempPath(), "ccp-web");
        var mine = Path.Combine(spirals, "loom_bambi-haze.gif");
        var files = new[] { mine, Path.Combine(web, "backroom", "shared", "hypno", "spirals", "wake.gif") }
            .Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool Exists(string p) => files.Contains(Path.GetFullPath(p));

        Assert.Equal(Path.GetFullPath(mine), BackRoomSpiralSource.Resolve("screen", mine, spirals, web, Exists));
        // Not a loom weave, or not in the library: the bundled file for the preset, or nothing.
        Assert.Null(BackRoomSpiralSource.Resolve("screen", Path.Combine(spirals, "spiral.gif"), spirals, web, Exists));
        Assert.Null(BackRoomSpiralSource.Resolve("screen", Path.Combine(web, "loom_bambi-haze.gif"), spirals, web, Exists));
        Assert.EndsWith(Path.Combine("spirals", "wake.gif"),
            BackRoomSpiralSource.Resolve("wake", Path.Combine(spirals, "..", "loom_x.gif"), spirals, web, Exists));
        Assert.Null(BackRoomSpiralSource.Resolve("screen", null, spirals, web, Exists));
        Assert.Null(BackRoomSpiralSource.Resolve("screen", Path.Combine(spirals, "loom_missing.gif"), spirals, null, Exists));
    }

    private static (double L, double S) Hsl(FxRgb c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), l = (max + min) / 2, d = max - min;
        double s = d < 1e-9 ? 0 : l > 0.5 ? d / (2 - max - min) : d / (max + min);
        return (l, s);
    }
}
