using CcpVerify;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// Floor guards over the IN-APP TOAST's four manifest checks
/// (<c>client/tools/verify/checks.json</c>), following the rack's, the session rack's and the
/// session lock's precedent: a pair is only evidence while it stays a PAIR and while neither half
/// can accept the other's colour.
///
/// <para><b>What these are and are not.</b> They are LEXICAL guards over a JSON file and a
/// PowerShell script on disk. They prove the four checks still exist, still claim the class a
/// headless frame cannot discharge, still separate the two accents, and still sample regions
/// <c>capture.ps1</c> proves against the measured layout before it reads a pixel. They prove
/// nothing about pixels — the captures do that, and the record of them is the pair
/// <c>artifacts/windows-toast-saved.png</c> / <c>-refused.png</c>: each accent check scored
/// 42/42 = 1.000 on its own capture, 0/42 = 0.000 on the other state's, and 0/230090 = 0.000 on a
/// real capture of the dashboard (exit 2 in all three failing directions).</para>
/// </summary>
public class ToastPresentationTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("repository root not found from the test binary");
    }

    private static string ManifestPath() =>
        Path.Combine(FindRepoRoot(), "client", "tools", "verify", "checks.json");

    private static string CaptureScript() =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), "client", "tools", "verify", "capture.ps1"));

    private static IReadOnlyList<ManifestCheck> ToastChecks() =>
        [.. CheckManifest.Load(ManifestPath())
            .Where(c => string.Equals(c.Surface, "toast", StringComparison.Ordinal))];

    /// <summary>
    /// BOTH states, both parts, and every one claiming <c>presentation-verified</c>. A surface
    /// checked in one state only cannot distinguish anything — the whole bite proof is that the
    /// other state's real capture fails it — and a check quietly demoted to <c>draw-verified</c>
    /// would let a headless run claim a composited pixel it never read.
    /// </summary>
    [Fact]
    public void TheToastIsCheckedInBothStates_AndEveryCheckClaimsPresentationVerified()
    {
        var checks = ToastChecks();
        Assert.Equal(
            ["toast-refused-accent", "toast-refused-plate", "toast-saved-accent", "toast-saved-plate"],
            checks.Select(c => c.Name).Order(StringComparer.Ordinal));

        foreach (var check in checks)
        {
            Assert.Equal(CheckManifest.EvidencePresentation, check.EvidenceClass);
        }

        // Exactly one accent check per state, and it is the accent that carries the outcome.
        Assert.Equal(
            ["toast/refused", "toast/saved"],
            checks.Where(c => c.Name.EndsWith("-accent", StringComparison.Ordinal))
                  .Select(c => $"{c.Surface}/{c.State}").Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Neither accent accepts the other's colour, nor the plate they are both drawn on, nor the
    /// shell ground behind the plate.
    ///
    /// <para>The arithmetic: upstream's Success <c>#4CAF50</c> and Error <c>#FF6B6B</c>
    /// (<c>Services/Notifications/NotificationService.cs:122,124</c>) are 179 apart on red, so at
    /// tolerance 8 neither can accept the other — measured 0/42 in both directions on the real
    /// captures. Widen either past the separation and the pair stops proving anything; this names
    /// both colours when that happens.</para>
    /// </summary>
    [Fact]
    public void NoToastCheckAcceptsAnotherColourThisSurfaceCanShow()
    {
        (string Name, byte R, byte G, byte B)[] neighbours =
        [
            ("the Success accent (NotificationService.cs:122)", 0x4C, 0xAF, 0x50),
            ("the Warning accent (NotificationService.cs:123)", 0xFF, 0xB3, 0x47),
            ("the Error accent (NotificationService.cs:124)", 0xFF, 0x6B, 0x6B),
            ("the Info accent (NotificationService.cs:125)", 0xFF, 0x69, 0xB4),
            ("the toast plate (Border.notice, MainWindow.axaml:128)", 0x24, 0x1E, 0x2A),
            ("the shell ground (MainWindow.axaml:6)", 0x14, 0x10, 0x18),
        ];

        var checks = ToastChecks();
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
                    + "check that cannot tell a refusal from a success is not evidence about either");
            }
        }

        // Every comparison skips exactly one neighbour — the check's own colour — so this
        // arithmetic holds only if all four checks expect a colour this list really names.
        Assert.Equal(checks.Count * (neighbours.Length - 1), compared);
    }

    /// <summary>
    /// <b>The regions in the manifest are the regions <c>capture.ps1</c> proves.</b> The script
    /// refuses at capture time unless the accent band really lies inside the measured accent bar
    /// and the plate band really lies clear of it — but it can only do that against the fractions
    /// it was written for. Move a fraction in <c>checks.json</c> alone and the proof would be about
    /// a different region than the one being sampled, which is the vacuous green a capture pass
    /// over an all-black image already cost this board once.
    /// </summary>
    [Fact]
    public void TheSampledRegionsAreTheOnesTheCaptureScriptProvesAgainstTheMeasuredLayout()
    {
        var checks = ToastChecks();
        Assert.Equal(4, checks.Count);

        // Both edges of both bands, derived from the capture width by the SCRIPT. Compared as a
        // set at the top level rather than one-per-loop-iteration, so a script that stopped
        // deriving them altogether fails here instead of passing an empty loop.
        var script = CaptureScript();
        string[] fractions = ["0.09", "0.18", "0.40", "0.90"];
        Assert.Equal(
            fractions,
            fractions.Where(f => script.Contains($"[int][math]::Round({f} * $capW)", StringComparison.Ordinal)));

        foreach (var check in checks)
        {
            var rect = check.Region.Rect;
            Assert.NotNull(rect);
            var left = Math.Round(rect!.X, 2);
            var right = Math.Round(rect.X + rect.W, 2);
            var expected = check.Name.EndsWith("-accent", StringComparison.Ordinal)
                ? (Left: 0.09, Right: 0.18)
                : (Left: 0.40, Right: 0.90);
            Assert.Equal(expected.Left, left);
            Assert.Equal(expected.Right, right);

            // Full height: the band capture.ps1 takes is already only the middle half of the
            // message's own line, so a y-fraction here would narrow a region that is already proved.
            Assert.Equal(0.0, rect.Y);
            Assert.Equal(1.0, rect.H);
        }
    }

    /// <summary>
    /// The two states are DRIVABLE. <c>capture.ps1</c> pairs each surface with the states it can
    /// really produce, and a manifest check whose state has no drive is decoration — this asserts
    /// the toast's pair specifically, on top of the manifest-wide guard
    /// <c>RackPresentationTests</c> already carries.
    /// </summary>
    [Fact]
    public void BothToastStatesAreOnesTheCaptureScriptCanDrive()
    {
        var script = CaptureScript();
        Assert.Contains("'toast' = @('saved', 'refused')", script, StringComparison.Ordinal);
        // The toast is the LAST entry in both ValidateSets, so the needles quote only its own tail.
        // Anchoring on the entry before it would red this fact every time an unrelated surface is
        // added between them, which is exactly what happened when companion-privacy and
        // companion-transcript landed - a guard that fails on somebody else's addition teaches
        // people to widen it.
        Assert.Contains("'toast')] [string]$Surface", script, StringComparison.Ordinal);
        Assert.Contains("'saved', 'refused')] [string]$State", script, StringComparison.Ordinal);

        // And the UIA gate that runs BEFORE any pixel is read. A capture whose only assertion is a
        // colour cannot tell one message from another, and these two states differ by their words
        // before they differ by their accent.
        Assert.Contains(
            @"$expected = '^Saved \d+ phrases?\.$'", script, StringComparison.Ordinal);
        Assert.Contains(
            "That file isn''t a phrase backup: the bytes are not JSON", script, StringComparison.Ordinal);
    }
}
