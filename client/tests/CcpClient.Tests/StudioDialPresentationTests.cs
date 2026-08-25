using CcpVerify;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// Floor guards over the SESSION FEATURE LOCK's two manifest checks
/// (<c>client/tools/verify/checks.json</c>), following the rack's and the session rack's
/// precedent: the pair is only evidence while it stays a PAIR and while neither half can accept
/// the other's colour.
///
/// <para><b>What these are and are not.</b> They are LEXICAL guards over a JSON file on disk. They
/// prove the two checks still exist, still claim the class a headless frame cannot discharge, and
/// still separate the two liveries. They prove nothing about pixels — the captures do that, and
/// the record of them is the pair
/// <c>artifacts/windows-studio-dial-live.png</c> / <c>-locked.png</c>, each scoring 0.222 on its
/// own check and 0.000 on the other's.</para>
/// </summary>
public class StudioDialPresentationTests
{
    private static string ManifestPath() =>
        Path.Combine(FindRepoRoot(), "client", "tools", "verify", "checks.json");

    private static IReadOnlyList<ManifestCheck> DialChecks() =>
        [.. CheckManifest.Load(ManifestPath())
            .Where(c => string.Equals(c.Surface, "studio-dial", StringComparison.Ordinal))];

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("repository root not found from the test binary");
    }

    /// <summary>
    /// BOTH states, and both claiming <c>presentation-verified</c>. A surface checked in one state
    /// only cannot distinguish anything — the whole bite proof is that the other state's real
    /// capture fails it — and a check quietly demoted to <c>draw-verified</c> would let a headless
    /// run claim a composited pixel it never read.
    /// </summary>
    [Fact]
    public void TheLockIsCheckedInBothOfItsStates_AndBothClaimPresentationVerified()
    {
        var checks = DialChecks();
        Assert.Equal(
            ["studio-dial/live", "studio-dial/locked"],
            checks.Select(c => $"{c.Surface}/{c.State}").Order(StringComparer.Ordinal));

        foreach (var check in checks)
        {
            Assert.Equal(CheckManifest.EvidencePresentation, check.EvidenceClass);
        }
    }

    /// <summary>
    /// Neither half accepts the other's colour, nor the ground they are both drawn on.
    ///
    /// <para>The measurement behind the numbers: the sampled band is 936 px and holds exactly three
    /// colours across the two captures — the module panel's ground at 728 px in both, Fluent's
    /// slider accent at 208 px when the dial is the user's, and its disabled track <c>#333333</c>
    /// at 208 px when the session owns it. Widen either tolerance past the separation and the pair
    /// stops proving anything; this names both colours when that happens.</para>
    ///
    /// <para><b>THE ENABLED TRACK WAS <c>#0078D4</c> IN THIS TABLE AND IN THE MANIFEST, AND IT HAD
    /// STOPPED BEING TRUE.</b> That is the Windows personalisation blue Fluent used to resolve
    /// <c>SystemAccentColor</c> from; the token layer shadowed all seven of Fluent's accent keys
    /// with the product's own pink and neither this table nor <c>checks.json</c> moved with it. The
    /// floor stayed green the whole time, because everything here asserts that the manifest is
    /// CONSISTENT and a manifest can be perfectly consistent about a colour nothing paints. Only a
    /// headed capture could catch that, and one did: re-measured 2026-08-25 on a real
    /// <c>studio-dial/live</c> capture at scale 1.75, the band is exactly 728 x <c>#11111A</c> plus
    /// 208 x <c>#E84393</c> — the theme's panel colour and the theme's accent, at the geometry this
    /// pair always had.</para>
    /// </summary>
    [Fact]
    public void NeitherLockCheckAcceptsTheOtherLiveryOrTheGroundBehindIt()
    {
        (string Name, byte R, byte G, byte B)[] neighbours =
        [
            ("the enabled slider track (Fluent's accent, the theme's #FFE84393)", 0xE8, 0x43, 0x93),
            ("the disabled slider track (Fluent SliderTrackValueFillDisabled)", 0x33, 0x33, 0x33),
            ("the module panel ground (Border.module, MainWindow.axaml:122, PanelBg)", 0x11, 0x11, 0x1A),
        ];

        var checks = DialChecks();
        Assert.NotEmpty(checks);

        var compared = 0;
        foreach (var check in checks)
        {
            var (r, g, b) = CheckManifest.ParseColor(check.ExpectedColor, $"check '{check.Name}':");
            foreach (var neighbour in neighbours)
            {
                if (neighbour.R == r && neighbour.G == g && neighbour.B == b)
                {
                    continue; // this IS the colour the check is for
                }

                compared++;
                var distance = Math.Max(
                    Math.Abs(neighbour.R - r), Math.Max(Math.Abs(neighbour.G - g), Math.Abs(neighbour.B - b)));
                Assert.True(check.Tolerance < distance,
                    $"check '{check.Name}' expects {check.ExpectedColor} with tolerance {check.Tolerance}, which "
                    + $"also ACCEPTS {neighbour.Name} — they are only {distance} apart on the widest channel. A "
                    + "check that cannot tell the locked dial from the live one is not evidence about the lock");
            }
        }

        // Every comparison skips exactly one neighbour — the check's own colour — so this arithmetic
        // holds only if BOTH checks expect a colour this list really names.
        Assert.Equal(checks.Count * (neighbours.Length - 1), compared);
    }
}
