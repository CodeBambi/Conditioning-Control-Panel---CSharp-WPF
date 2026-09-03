using System;
using System.IO;
using System.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// DOES HER TEXTURE ACTUALLY MAKE A SOUND?
///
/// <para><c>EmiSfx</c> resolves each cue through an override-then-fallback chain and treats a
/// missing file as a silent no-op - which is the right runtime behaviour (a cue must never throw
/// on the UI thread) and a terrible failure mode for authoring: a typo in a filename ships as
/// "the pat has no sound" and nothing anywhere says why.</para>
///
/// <para>So the last link of every chain - the one that is supposed to be in the repo today - is
/// asserted here. The earlier links are the owner's future bespoke files and are expected to be
/// absent; only the backstop has to exist, and it has to be COPIED to the output too, because
/// <c>ModResourceResolver.ResolveAudioPath</c> reads off <c>AppContext.BaseDirectory</c> at
/// runtime, not out of the source tree.</para>
/// </summary>
public class EmiSfxAssetTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "ConditioningControlPanel.csproj")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string SoundsDir() =>
        Path.Combine(RepoRoot(), "Assets", "sounds");

    /// <summary>
    /// The chains, read straight off the service so the two cannot drift. Reading the real
    /// <c>AllChains()</c> rather than a copy is the whole point: a cue added without a backstop
    /// fails here on the day it is written.
    /// </summary>
    public static TheoryData<string[]> Chains()
    {
        var data = new TheoryData<string[]>();
        foreach (var chain in ConditioningControlPanel.Services.EmiDesk.EmiSfx.AllChains())
            data.Add(chain);
        return data;
    }

    [Theory]
    [MemberData(nameof(Chains))]
    public void EveryCueHasABackstopThatExists(string[] chain)
    {
        Assert.NotEmpty(chain);

        var backstop = chain[^1];
        var full = Path.Combine(SoundsDir(), backstop.Replace('/', Path.DirectorySeparatorChar));

        Assert.True(File.Exists(full),
            $"EmiSfx cue chain ends in '{backstop}', which is not in Resources/sounds. " +
            "The cue would be a permanent silent no-op. Ship the asset or point the last link " +
            "at one that is already there.");

        Assert.True(new FileInfo(full).Length > 0, backstop + " is a zero-byte file");
    }

    [Theory]
    [MemberData(nameof(Chains))]
    public void ABackstopIsShortEnoughToBeATexture(string[] chain)
    {
        // Not a duration check - nothing here decodes mp3 - but a 500 KB "one-shot" is a music
        // cue that somebody pasted into the wrong list, and it would talk over her voice.
        var backstop = chain[^1];
        var full = Path.Combine(SoundsDir(), backstop.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(full)) return;         // the assertion above owns that failure

        Assert.True(new FileInfo(full).Length < 250_000,
            $"{backstop} is {new FileInfo(full).Length / 1024} KB - too long to be a UI texture.");
    }

    /// <summary>Overrides come first, backstops last: a chain that leads with a file already in the
    /// repo can never be overridden, which defeats the pattern.</summary>
    [Fact]
    public void EveryChainLeadsWithAnEmiOverrideSlot()
    {
        foreach (var chain in ConditioningControlPanel.Services.EmiDesk.EmiSfx.AllChains())
        {
            Assert.True(chain.Length >= 2, "a cue with no fallback is just a hardcoded path");
            Assert.StartsWith("emi/", chain[0], StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(SoundsDir(), chain[0].Replace('/', Path.DirectorySeparatorChar))),
                chain[0] + " now exists, so it is a shipped asset, not an override slot - " +
                "move it and give the cue a new empty first link.");
        }
    }
}
