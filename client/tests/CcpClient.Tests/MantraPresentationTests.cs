using CcpClient.Desktop.Features.Mantra;
using CcpVerify;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The typed mantra game's presentation checks, and the capture path that earns them.
///
/// <para><b>What this file is NOT.</b> It is not the evidence. The evidence is two real captures of
/// the running game on a real Windows desktop at scale 1.75, taken through a door a user can press,
/// and checked by <c>CcpVerify</c> against <c>client/tools/verify/checks.json</c>:
/// <c>mantra-window-fresh-dim</c> scored 0.281 on its own capture, 0.003 on the other state's and
/// 21/2560250 (0.000) on a capture of the whole dashboard - ALL THREE AT TOLERANCE 12, which is
/// what that check carried until the palette flip onto upstream's values put PanelAccent #FF2E2E4A
/// 7/7/6 from the dim ink and forced it to 6. THE CCP DEFAULT THEME THEN MOVED PanelAccent AWAY
/// AGAIN - it is derived from the mod's panel colour now and lands on #FF34343C, 1/1/20 from the
/// dim ink - so the ceiling went 7 to 19 and the tolerance STAYED at 6, because a tolerance is
/// never widened back because it became comfortable. THE DIM CHECK HAS NOW BEEN RE-MEASURED HEADED
/// AT 6 AND ON THE THEMED PALETTE - 0.276 on a real <c>fresh</c> capture (62542/226380), against
/// the 0.281 it scored at 12 on the seed's - so the "not re-measured" caveat this line used to
/// carry is discharged. The floor it is pinned against is 0.01, an order of magnitude under the
/// lowest of those; <c>mantra-window-typed-lit</c> scored
/// 0.273 on its own and 0.000 EXACT on both of the others. TWO PAIRS were taken, and the mantra is
/// drawn at random: the first drew different sentences in the two states ('I am deeply relaxed'
/// over 2450x473 and 'My mind is open and receptive' over 2450x307), the second drew the same
/// sentence twice over the same band to the pixel, and the checks inverted identically both times.
/// A headless assembly cannot photograph anything and no fact here claims to.</para>
///
/// <para><b>What it IS.</b> The things that rot silently between headed runs. This surface's claim
/// is the per-character feedback that IS the game — a matched character is painted
/// <see cref="MantraIntensity.ColdHighlight"/> and an untyped one
/// <see cref="MantraIntensity.Dim"/> — so what can rot is the link between those two product
/// colours and the two hex strings in a JSON file, and the tolerance that keeps each check off the
/// other's colour AND off the glow the product draws around both.</para>
/// </summary>
public class MantraPresentationTests
{
    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];

    private static IReadOnlyList<ManifestCheck> MantraChecks() =>
        [.. CheckManifest.Load(ManifestPath()).Where(c => c.Surface == "mantra-window")];

    private static string ManifestPath() =>
        Path.Combine(FindRepoRoot(), "client", "tools", "verify", "checks.json");

    private static string Hex(MantraColour colour) =>
        $"#{colour.R:X2}{colour.G:X2}{colour.B:X2}";

    private static int PerChannel((byte R, byte G, byte B) a, (byte R, byte G, byte B) b) =>
        Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine([dir.FullName, .. RepoAnchorParts])))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("the repository root (client/CcpClient.sln) was not found above the test binary");
    }

    [Fact]
    public void BothStatesAreChecked_AndEachNamesTheColourTheProductReallyPaints()
    {
        // A surface checked in one state only cannot distinguish anything: the bite proof is that
        // the OTHER state's real capture fails it, and there has to BE another state for that to
        // mean something. The class is asserted rather than assumed, because a headed gate is never
        // dischargeable by a headless frame.
        var checks = MantraChecks();
        Assert.Equal(2, checks.Count);
        Assert.All(checks, c => Assert.Equal(CheckManifest.EvidencePresentation, c.EvidenceClass));
        Assert.Equal(
            ["mantra-window/fresh", "mantra-window/typed"],
            checks.Select(c => $"{c.Surface}/{c.State}").OrderBy(p => p, StringComparer.Ordinal).ToArray());

        // THE LINK THAT ROTS. The two hex strings are not opinions about the game's palette: they
        // are the two colours MantraSession.StateOf chooses between, taken from the type that owns
        // them, so a repaint cannot leave the manifest describing the old game. The lit colour is
        // the ramp's COLD end because the capture is taken at streak 0 — a run that had banked a
        // repetition would be somewhere else on the ramp, and capture.ps1 refuses if the counters
        // have moved.
        var fresh = checks.Single(c => c.State == "fresh");
        var typed = checks.Single(c => c.State == "typed");
        Assert.Equal(Hex(MantraIntensity.Dim), fresh.ExpectedColor);
        Assert.Equal(Hex(MantraIntensity.For(0).Highlight), typed.ExpectedColor);
        Assert.Equal(Hex(MantraIntensity.ColdHighlight), typed.ExpectedColor);

        // Both read the WHOLE captured band, because capture.ps1 already cut that band out of the
        // mantra line's own UIA rect. The mantra is drawn at random, so a fixed sub-rect here would
        // be aiming at a line whose width changes with every draw.
        Assert.All(checks, c =>
        {
            Assert.Equal(CheckManifest.KindRegionColor, c.Kind);
            Assert.Equal(0.0, c.Region.Rect!.X);
            Assert.Equal(0.0, c.Region.Rect!.Y);
            Assert.Equal(1.0, c.Region.Rect!.W);
            Assert.Equal(1.0, c.Region.Rect!.H);
        });
    }

    [Fact]
    public void NeitherToleranceAcceptsTheOthersColour_TheGlowAroundThem_OrAnotherDeclaredCheck()
    {
        var all = CheckManifest.Load(ManifestPath());
        var mantra = MantraChecks();
        Assert.NotEmpty(mantra);

        // Non-vacuity over the whole manifest: a check compared against nothing would pass loudly
        // and mean nothing.
        Assert.True(all.Count - mantra.Count >= 3,
            $"only {all.Count - mantra.Count} non-mantra check(s) in the manifest — this guard would be nearly vacuous");

        foreach (var check in mantra)
        {
            var expected = CheckManifest.ParseColor(check.ExpectedColor, $"check '{check.Name}':");

            // THE NEAREST HAZARD IS NOT IN THE MANIFEST AT ALL, and that is why this guard is not
            // just the standard cross-surface one. The mantra line carries a DropShadow whose
            // colour at streak 0 is MantraIntensity.GlowColour, and it is drawn around EVERY
            // character — dim ones included. A tolerance that reached it would let the halo around
            // an UNTYPED line pass the check that exists to say a character was TYPED, and the
            // inversion would be a fiction.
            var glow = MantraIntensity.For(0).GlowColour;
            var toGlow = PerChannel((expected.R, expected.G, expected.B), (glow.R, glow.G, glow.B));
            Assert.True(check.Tolerance < toGlow,
                $"'{check.Name}' ({check.ExpectedColor}, tolerance {check.Tolerance}) accepts the mantra's own "
                + $"glow {Hex(glow)} — they are {toGlow} apart per channel, so the halo around dim text would "
                + "satisfy a check about typed text");

            // And the standard rule: SURFACE+STATE, never the surface alone, so the two mantra
            // states are compared against each other rather than exempted as siblings.
            var others = all.Where(c => c.Surface != check.Surface || c.State != check.State);
            foreach (var other in others)
            {
                var otherColour = CheckManifest.ParseColor(other.ExpectedColor, $"check '{other.Name}':");
                var separation = PerChannel(
                    (expected.R, expected.G, expected.B), (otherColour.R, otherColour.G, otherColour.B));
                Assert.True(check.Tolerance < separation,
                    $"'{check.Name}' ({check.ExpectedColor}, tolerance {check.Tolerance}) accepts "
                    + $"'{other.Name}' ({other.ExpectedColor}) — they are {separation} apart per channel, so a "
                    + $"capture of {other.Surface}/{other.State} could pass a check that exists to say the mantra "
                    + "game's own line was on the screen");
            }
        }
    }
}
