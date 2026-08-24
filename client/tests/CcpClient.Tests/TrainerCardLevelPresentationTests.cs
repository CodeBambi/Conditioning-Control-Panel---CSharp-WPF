using System.Text.RegularExpressions;
using CcpVerify;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The Trainer Card level bar's presentation checks, and the capture path that earns them.
///
/// <para><b>What this file is NOT.</b> It is not the evidence. The evidence is three real captures
/// of the running shell on a real Windows desktop at scale 1.75, checked by <c>CcpVerify</c> against
/// <c>client/tools/verify/checks.json</c>: <c>trainer-card-level-earned-fill</c> scored 1176/1176
/// (1.0000) on its own capture, 0/1176 on the other state's, and 0/167912 on a capture of the whole
/// Trainer Card — an image that CONTAINS this bar and still cannot pass the check;
/// <c>trainer-card-level-fresh-track</c> scored the same three ways round. A headless assembly
/// cannot photograph anything and no fact here claims to.</para>
///
/// <para><b>What it IS.</b> The things that rot silently between headed runs. This surface's claim
/// is unusual and so is what can rot: both states paint the SAME TWO COLOURS in the same place, and
/// what separates them is how much of the track the fill covers. So the load-bearing facts are (a)
/// the two checks cannot both be satisfied by one flat colour, (b) neither tolerance accepts another
/// colour this shell declares, and (c) THE SAMPLED BAND STILL SITS WELL INSIDE THE EARNED FILL. That
/// last one is this file's real job, and it is the pop quiz lesson written down: a floor or a band
/// placed near a value the product moves reds a good capture, so the margin is asserted as a RATIO
/// rather than trusted as a number.</para>
/// </summary>
public class TrainerCardLevelPresentationTests
{
    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];

    /// <summary>The level bar's checks, read from the manifest on disk and never restated here.</summary>
    private static IReadOnlyList<ManifestCheck> LevelChecks() =>
        [.. CheckManifest.Load(ManifestPath()).Where(c => c.Surface == "trainer-card-level")];

    private static string ManifestPath() =>
        Path.Combine(FindRepoRoot(), "client", "tools", "verify", "checks.json");

    private static string CaptureScript() =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), "client", "tools", "verify", "capture.ps1"));

    [Fact]
    public void TheBarIsCheckedInBothStates_AndBothClaimPresentationVerified()
    {
        // A surface checked in one state only cannot distinguish anything: the whole bite proof is
        // that the other state's real capture fails it, and there has to BE another state for that
        // to mean something. A headed gate is also never dischargeable by a headless frame, so the
        // class is asserted rather than assumed.
        var checks = LevelChecks();
        Assert.Equal(2, checks.Count);
        Assert.All(checks, c => Assert.Equal(CheckManifest.EvidencePresentation, c.EvidenceClass));

        Assert.Equal(
            ["trainer-card-level/earned", "trainer-card-level/fresh"],
            checks.Select(c => $"{c.Surface}/{c.State}").OrderBy(p => p, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void BothStatesSampleTheSameRegion_BecauseTheClaimIsAFractionAndNotAColour()
    {
        // The inversion this surface rests on. If the two checks read DIFFERENT regions, each could
        // pass on its own capture for a reason that has nothing to do with the ledger — one aimed at
        // permanent fill and the other at permanent track. Reading one region in two states is what
        // makes "the bar moved because the number moved" the only explanation.
        var regions = LevelChecks()
            .Select(c => c.Region.Rect!)
            .Select(r => (r.X, r.Y, r.W, r.H))
            .Distinct()
            .ToArray();

        Assert.Single(regions);
    }

    [Fact]
    public void NoFlatColourPassesBothChecks_AndNoDeclaredNeighbourPassesEither()
    {
        // (a) Two bands that overlapped on every channel would both be satisfied by one uniform
        // capture — the failure this repository has already had once, when capture-wslg.sh printed
        // CAPTURE PASS over an all-black image.
        var checks = LevelChecks();
        var (ar, ag, ab) = CheckManifest.ParseColor(checks[0].ExpectedColor, $"check '{checks[0].Name}':");
        var (br, bg, bb) = CheckManifest.ParseColor(checks[1].ExpectedColor, $"check '{checks[1].Name}':");
        var reach = checks[0].Tolerance + checks[1].Tolerance;
        Assert.True(
            Math.Abs(ar - br) > reach || Math.Abs(ag - bg) > reach || Math.Abs(ab - bb) > reach,
            $"'{checks[0].Name}' and '{checks[1].Name}' overlap on every channel, so one flat colour "
            + "satisfies both and a capture of an empty rectangle would pass this surface");

        // (b) And neither may accept a colour this shell paints somewhere ELSE. The named victim
        // here is the track: #2A2130 is only 6/3/6 from the notice ground #241E2A, so at the
        // dashboard's tolerance of 8 the `fresh` check would pass on a photograph of a notice panel.
        (string Name, byte R, byte G, byte B)[] neighbours =
        [
            ("the module panel ground (Border.module, MainWindow.axaml:122)", 0x1B, 0x16, 0x22),
            ("the notice ground (Border.notice, :129)", 0x24, 0x1E, 0x2A),
            ("the rack ground (Border.rack, :117)", 0x19, 0x14, 0x1F),
            ("the page ground behind the card (Window Background)", 0x14, 0x10, 0x18),
            ("the selected rail door (RadioButton.door:checked, :69)", 0xE0, 0x66, 0xFF),
            ("the running session button (session-start.running, :364)", 0xFF, 0x6B, 0x6B),
            ("the module title's ink (TextBlock.module-title, :320)", 0xE8, 0xE0, 0xEE),
        ];

        var compared = 0;
        foreach (var check in checks)
        {
            var (r, g, b) = CheckManifest.ParseColor(check.ExpectedColor, $"check '{check.Name}':");
            foreach (var neighbour in neighbours)
            {
                compared++;
                var distance = Math.Max(
                    Math.Abs(neighbour.R - r), Math.Max(Math.Abs(neighbour.G - g), Math.Abs(neighbour.B - b)));
                Assert.True(check.Tolerance < distance,
                    $"check '{check.Name}' expects {check.ExpectedColor} with tolerance {check.Tolerance}, "
                    + $"which also ACCEPTS {neighbour.Name} — they are only {distance} apart on the widest "
                    + "channel. A check that cannot tell two of this app's surfaces apart cannot fail on either");
            }
        }

        // Every neighbour above is a DIFFERENT colour from both checks, so nothing was skipped and
        // the arithmetic really ran over the whole list.
        Assert.Equal(checks.Count * neighbours.Length, compared);
    }

    [Fact]
    public void TheSampledBandStaysWellInsideTheEarnedFill_AndTheMarginIsARatioRatherThanANumber()
    {
        // THE POP QUIZ LESSON, WRITTEN DOWN. That surface's ink floor was set near the MEAN of a
        // value the product varies and it redded a perfectly good capture on its second run. The
        // value this surface varies is the FILL'S RIGHT EDGE: it moves with the seeded level, with
        // the curve band that level sits in, and with the bar's declared width. So the band is not
        // merely "inside the fill", it is inside it by a margin, and the margin is what is asserted.
        //
        // The fill reaches 1000.5 / Math.Round(800 + 41 x 1700/79) = 1000.5/1682 = 0.5949 of the
        // bar — derived from upstream's own first band (ProgressionService.cs:301-305) rather than
        // read back from XpCurve, which would pass against a wrong curve.
        const double earnedFraction = 1000.5 / 1682.0;
        var rect = LevelChecks().Single(c => c.State == "earned").Region.Rect!;
        var bandRight = rect.X + rect.W;

        Assert.True(bandRight < earnedFraction * 0.75,
            $"the sampled band's right edge is at {bandRight} of the bar and the earned fill reaches "
            + $"{earnedFraction:0.0000}. The band must sit well inside the fill rather than near its edge — a "
            + "floor or a boundary set near a value the product moves reds a good capture, which is exactly "
            + "how the pop quiz card's ink check first failed");

        // capture.ps1 asserts the SAME ratio against the measured rect before it reads the screen,
        // so the rule cannot be quietly relaxed in one file and left in the other.
        Assert.Contains("$fillBand[1] -ge ($earnedFraction * 0.75)", CaptureScript(), StringComparison.Ordinal);

        // And the band the manifest samples is the band capture.ps1 proves — on both axes.
        var proved = Band(CaptureScript(), "fillBand");
        WithinBand(rect.X, proved, "fillBand", "the sampled band's left edge");
        WithinBand(bandRight, proved, "fillBand", "the sampled band's right edge");

        var provedY = Band(CaptureScript(), "inkBandY");
        WithinBand(rect.Y, provedY, "inkBandY", "the sampled band's top edge");
        WithinBand(rect.Y + rect.H, provedY, "inkBandY", "the sampled band's bottom edge");
    }

    [Fact]
    public void TheCapturePathStillReadsTheLevelOffUiaBeforeAnyPixel()
    {
        // The gate that makes the two pixel checks mean anything, and the reason it cannot be
        // inferred from them: TrainerCardXpTrack is a Border and has no automation peer, so the
        // bar's rect is DERIVED — and a derived rect aimed at a page showing a different level
        // photographs perfectly plausible pixels of the wrong claim. Demonstrated, not asserted: a
        // run with the seed changed to level 7 refused reading "the Trainer Card's level line reads
        // 'LVL 7', not 'LVL 42'" before it took a single pixel.
        var script = CaptureScript();

        foreach (var needle in new[]
                 {
                     "TrainerCardLevelLine", "TrainerCardRankLine", "TrainerCardXpLine",
                     "TrainerCardLevelUnknownNote",
                     "LVL 42", "DUMB AIRHEAD", "1000 / 1682 XP",
                     "LVL 1", "BASIC BIMBO", "0 / 800 XP",
                 })
        {
            Assert.Contains(needle, script, StringComparison.Ordinal);
        }

        // The ledger is in the deterministic-start set, so a run cannot inherit a level from the
        // previous one — which is what makes `fresh` a state rather than whatever was left behind.
        Assert.Contains(
            CcpClient.Desktop.Features.Progression.ProgressionDocument.FileName,
            script,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// An edge of the manifest's region against the band <c>capture.ps1</c> proves, with binary
    /// slack and nothing else. <c>Assert.InRange</c> is exact and these are sums of decimal
    /// fractions no <c>double</c> holds: the manifest's 0.10 + 0.20 is 0.30000000000000004, which is
    /// outside a band ending at 0.30 by four parts in 10^17. The epsilon is that representation gap
    /// and it is deliberately far too small to hide a real widening — one ten-millionth of the bar
    /// is a fifth of a thousandth of a pixel at this scale.
    /// </summary>
    private static void WithinBand(double edge, (double Lo, double Hi) band, string name, string what)
    {
        const double slack = 1e-9;
        Assert.True(edge >= band.Lo - slack && edge <= band.Hi + slack,
            $"{what} is at {edge:0.######} of the capture, and capture.ps1's ${name} proves only "
            + $"{band.Lo:0.######}..{band.Hi:0.######} against the measured layout. A fraction of a capture is "
            + "evidence only if the script checked that the thing it names is really at that fraction");
    }

    /// <summary>One <c>$name = @(lo, hi)</c> band out of <c>capture.ps1</c>.</summary>
    private static (double Lo, double Hi) Band(string script, string name)
    {
        var match = Regex.Match(script, @"\$" + name + @" = @\((?<lo>[\d.]+), (?<hi>[\d.]+)\)");
        Assert.True(match.Success,
            $"capture.ps1 no longer declares ${name}, so nothing proves the manifest region it bounds is "
            + "the region the script measured before it read the screen");
        // INVARIANT, never the current culture. On a machine whose decimal separator is a comma,
        // double.Parse strips the '.' as a group separator and "0.10" becomes 10 — which is not a
        // hypothetical: this fact failed exactly that way on its first run, reporting a band of
        // (10 - 30).
        return (double.Parse(match.Groups["lo"].Value, System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(match.Groups["hi"].Value, System.Globalization.CultureInfo.InvariantCulture));
    }

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
}
