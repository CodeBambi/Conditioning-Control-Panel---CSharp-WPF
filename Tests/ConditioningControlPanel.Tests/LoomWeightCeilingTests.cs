using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE LOOM's size gate (v6.9.4). The loom weaves a spiral in a web worker, hands the GIF to
/// C# as base64, and <see cref="DtrhLoomStore"/> decides whether it may hang on the rack.
///
/// <para>The bug these tests pin: the base64 ceiling was written as the same literal as the
/// decoded ceiling (8MB both), but base64 costs 4 characters per 3 bytes - so the gate that
/// claimed to admit 8MB of gif admitted 6MB, and everything in between was answered "too heavy
/// to hang. simpler colors, fewer arms." The worker's own retry ladder aims at 6MB and settles
/// for anything under 8MB, so that band is exactly where its output lands. One of the eight
/// shipped patterns - "bambi haze" - lives there, which is how a first-time weaver saving an
/// untouched default pattern got told their rack was too heavy.</para>
///
/// <para>The numbers in <see cref="DefaultPatternWeights"/> were measured by running the real
/// encoder (<c>Resources/web/dtrh/engine/loomWorker.js</c>, WebGL field + gifenc, ANGLE
/// SwiftShader) over every entry of the <c>PRESETS</c> table in <c>game/loomStudio.js</c> at its
/// shipped defaults, 2026-09-11. They are a floor, not a promise: a different GPU's antialiasing
/// moves them by a few percent, which is why the headroom assertions below are the point rather
/// than the exact byte counts.</para>
/// </summary>
public class LoomWeightCeilingTests
{
    /// <summary>base64 length for a payload of <paramref name="bytes"/> bytes, padding included -
    /// the same arithmetic the page's btoa does on its way to the bridge.</summary>
    private static long Base64Len(long bytes) => ((bytes + 2) / 3) * 4;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string ReadLoomSource(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot(), "ConditioningControlPanel", "Resources", "web", "dtrh" }
            .Concat(parts).ToArray()));

    /// <summary>Pattern name -> weave size in bytes, as the shipped worker produces it.</summary>
    private static readonly (string Name, int Bytes)[] Weighed =
    {
        ("classic ccp", 3_053_418),
        ("bambi haze", 6_807_218),     // the one the underfed ceiling refused
        ("sissy swirl", 1_593_659),
        ("drone protocol", 2_265_466),
        ("locked in", 4_972_392),
        ("hypno teal", 1_070_116),
        ("candy tunnel", 2_783_640),
        ("void bloom", 3_900_414),
    };

    public static TheoryData<string, int> DefaultPatternWeights()
    {
        var data = new TheoryData<string, int>();
        foreach (var (name, bytes) in Weighed) data.Add(name, bytes);
        return data;
    }

    /// <summary>Every pattern the loom ships with must survive both halves of the gate - the
    /// base64 length check the payload meets first, and the decoded check after.</summary>
    [Theory]
    [MemberData(nameof(DefaultPatternWeights))]
    public void ShippedDefaultPatternsAllPassTheWeightCheck(string name, int bytes)
    {
        Assert.True(bytes <= DtrhLoomStore.MaxGifBytes,
            $"default pattern '{name}' weaves to {bytes} bytes, over the {DtrhLoomStore.MaxGifBytes}-byte decoded ceiling");
        Assert.True(Base64Len(bytes) <= DtrhLoomStore.MaxBase64Chars,
            $"default pattern '{name}' weaves to {bytes} bytes = {Base64Len(bytes)} base64 chars, over the " +
            $"{DtrhLoomStore.MaxBase64Chars}-char ceiling - it would come back 'too heavy to hang'");
    }

    /// <summary>The measured table has to cover the patterns that actually ship: adding a ninth
    /// preset without weighing it is how the next "bambi haze" gets in.</summary>
    [Fact]
    public void TheWeighedPatternsAreExactlyTheShippedOnes()
    {
        var src = ReadLoomSource("game", "loomStudio.js");
        var block = Regex.Match(src, @"const PRESETS = \[(?<body>[\s\S]*?)\n\];");
        Assert.True(block.Success, "could not find the PRESETS table in loomStudio.js");

        // one row per line: ['name', { ... }] - the inner '#rrggbb' colour lists must not count
        var shipped = Regex.Matches(block.Groups["body"].Value, @"^\s*\['(?<name>[^']+)',\s*\{", RegexOptions.Multiline)
            .Select(m => m.Groups["name"].Value).ToList();
        Assert.NotEmpty(shipped);
        Assert.Equal(shipped.OrderBy(s => s, StringComparer.Ordinal),
                     Weighed.Select(w => w.Name).OrderBy(s => s, StringComparer.Ordinal));
    }

    /// <summary>The base64 gate must admit a full decoded ceiling's worth of gif. This is the
    /// regression itself: with both constants written as 8MB literals the biggest gif that could
    /// reach the disk was 6MB.</summary>
    [Fact]
    public void TheBase64CeilingAdmitsAFullSizedGif()
    {
        Assert.True(DtrhLoomStore.MaxBase64Chars >= Base64Len(DtrhLoomStore.MaxGifBytes),
            $"{DtrhLoomStore.MaxBase64Chars} base64 chars only carries {DtrhLoomStore.MaxBase64Chars / 4 * 3} bytes, " +
            $"but the store claims to keep {DtrhLoomStore.MaxGifBytes}");
    }

    /// <summary>...and the worker must not be allowed to weave something the store will not take:
    /// its HARD_CAP is the promise, MaxGifBytes is the acceptance.</summary>
    [Fact]
    public void TheWorkerHardCapFitsInsideTheStoreCeiling()
    {
        var src = ReadLoomSource("engine", "loomWorker.js");
        var m = Regex.Match(src, @"HARD_CAP\s*=\s*(?<n>\d+)\s*\*\s*1024\s*\*\s*1024");
        Assert.True(m.Success, "could not read HARD_CAP out of loomWorker.js");

        var hardCap = int.Parse(m.Groups["n"].Value) * 1024 * 1024;
        Assert.True(hardCap <= DtrhLoomStore.MaxGifBytes,
            $"loomWorker.js emits gifs up to {hardCap} bytes but the store keeps only {DtrhLoomStore.MaxGifBytes}");
        Assert.True(Base64Len(hardCap) <= DtrhLoomStore.MaxBase64Chars,
            $"a HARD_CAP gif is {Base64Len(hardCap)} base64 chars, past the {DtrhLoomStore.MaxBase64Chars}-char gate");
    }

    /// <summary>An empty payload is a broken weave, not a heavy one. It used to answer "too-big",
    /// which the pane renders as "too heavy to hang. simpler colors, fewer arms." - advice for a
    /// pattern that never reached C# at all.</summary>
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void AnEmptyWeaveIsNotCalledHeavy(string? payload)
    {
        var (ok, _, error) = DtrhLoomStore.Save("weight test", payload, null, overwrite: false);
        Assert.False(ok);
        Assert.Equal("bad-gif", error);
    }

    /// <summary>A payload past the gate still gets the honest complaint.</summary>
    [Fact]
    public void AnOversizeWeaveIsStillRefusedAsTooBig()
    {
        var payload = new string('A', DtrhLoomStore.MaxBase64Chars + 1);
        var (ok, _, error) = DtrhLoomStore.Save("weight test", payload, null, overwrite: false);
        Assert.False(ok);
        Assert.Equal("too-big", error);
    }
}
