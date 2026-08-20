using CcpVerify;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-122 — the Studio rack's presentation checks, and the capture path that earns them.
///
/// <para><b>What this file is NOT.</b> It is not the evidence. The evidence is four real captures
/// of the running shell on a real Windows desktop, checked by <c>CcpVerify</c> against
/// <c>client/tools/verify/checks.json</c>, with every check shown failing on a real capture of the
/// opposite state and on a real seeded regression in <c>MainWindow.axaml</c>
/// (<c>spine-tasks/SP-122-rack-presentation/record.md</c>). A headless assembly cannot photograph
/// anything and no fact here claims to.</para>
///
/// <para><b>What it IS.</b> The things that can rot silently between headed runs: a rack check
/// that stops being <c>presentation-verified</c>, a surface the capture script can no longer be
/// asked for, a tolerance widened past the colour it exists to reject, and the two properties the
/// capture path must keep — it takes the machine-wide real-desktop lease, and it fences the screen
/// read. The headed run happens on demand; these run on every floor run.</para>
///
/// <para><b>Honest limit on the last two.</b> They are LEXICAL: they prove the lease acquisition
/// and the compositor fence have not been DELETED. They cannot prove the mechanism still works —
/// a commented-out lease would keep them green. What proves the mechanism is the transcript in
/// <c>record.md</c> §4: a foreign holder took the lease, <c>capture.ps1</c> waited 22.1 s for it,
/// and with the deadline seeded down it refused BY NAME, reporting the holder's pid.</para>
/// </summary>
public class RackPresentationTests
{
    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];

    /// <summary>Every rack check, by name. The manifest is read from disk, never restated here.</summary>
    private static IReadOnlyList<ManifestCheck> RackChecks() =>
        [.. CheckManifest.Load(ManifestPath()).Where(c => c.Surface.StartsWith("rack-", StringComparison.Ordinal))];

    private static string ManifestPath() =>
        Path.Combine(FindRepoRoot(), "client", "tools", "verify", "checks.json");

    private static string CaptureScript() =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), "client", "tools", "verify", "capture.ps1"));

    /// <summary>
    /// <see cref="CaptureScript"/> with its comment lines removed. Necessary rather than tidy: the
    /// script EXPLAINS in a comment why it does not use a <c>StreamWriter</c>, and a naive search
    /// for that word finds the explanation and reports the defect it warns about.
    /// </summary>
    private static string CaptureScriptCode() =>
        string.Join('\n', CaptureScript().Split('\n').Where(line => !line.TrimStart().StartsWith('#')));

    /// <summary>
    /// The rack is on the manifest at all, and every one of its checks claims the class a headless
    /// frame can never discharge. A rack check quietly demoted to <c>draw-verified</c> would let a
    /// headless run claim it, which is the exact substitution the evidence classes exist to stop.
    /// </summary>
    [Fact]
    public void EveryRackCheckClaimsPresentationVerified()
    {
        var checks = RackChecks();
        Assert.NotEmpty(checks);
        foreach (var check in checks)
        {
            Assert.Equal(CheckManifest.EvidencePresentation, check.EvidenceClass);
        }
    }

    /// <summary>
    /// Both rack surfaces are covered in BOTH of their states. A surface checked in one state only
    /// is a check that cannot distinguish anything: the whole bite proof is that the other state's
    /// real capture fails it, and there has to BE another state for that to mean something.
    /// </summary>
    [Fact]
    public void BothRackSurfacesAreCheckedInBothStates()
    {
        var byPair = RackChecks()
            .Select(c => $"{c.Surface}/{c.State}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["rack-row-dot/armed", "rack-row-dot/off", "rack-row/selected", "rack-row/unselected"],
            byPair);
    }

    /// <summary>
    /// DERIVE THE SURFACE SET, NEVER RESTATE IT — the SP-094 audit's rule, applied to the manifest
    /// side. A check naming a surface <c>capture.ps1</c> cannot produce is a check nothing will
    /// ever run, and it would sit there looking like coverage. Both sides are read from disk: the
    /// script's own <c>ValidateSet</c>, and the manifest's own surfaces.
    /// </summary>
    [Fact]
    public void EveryManifestSurfaceIsOneTheCaptureScriptAccepts()
    {
        var script = CaptureScriptCode();
        var match = System.Text.RegularExpressions.Regex.Match(
            script, @"ValidateSet\((?<set>[^)]*)\)\]\s*\[string\]\$Surface");
        Assert.True(match.Success, "capture.ps1 no longer declares a ValidateSet for -Surface");

        var accepted = System.Text.RegularExpressions.Regex.Matches(match.Groups["set"].Value, "'(?<s>[^']+)'")
            .Select(m => m.Groups["s"].Value)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var check in CheckManifest.Load(ManifestPath()))
        {
            Assert.True(accepted.Contains(check.Surface),
                $"checks.json names surface '{check.Surface}' but capture.ps1 accepts only "
                + $"[{string.Join(", ", accepted.OrderBy(s => s, StringComparer.Ordinal))}] — no capture of that "
                + "surface can ever be produced, so the check is decoration");
        }
    }

    /// <summary>
    /// THE TOLERANCE RULE, MECHANICAL. A tolerance is the size of the defect it hides, and here the
    /// defect has a name and a number: <c>RadioButton.rack-row:pointerover</c> is <c>#FF241E2A</c>
    /// (<c>MainWindow.axaml:98-100</c>), which is 11/10/11 from the rack's <c>#FF19141F</c> ground,
    /// and the checked fill <c>#FF2A2130</c> is 17/13/17 from it. At the rail-door precedent of 24
    /// the ground check would pass with the mouse merely RESTING on the row, and the fill check
    /// would pass on a row that is not open.
    ///
    /// <para>So this asserts the property rather than the constant: no rack check may accept the
    /// colour of a DIFFERENT rack state. Widen a tolerance past a neighbour and this names both.</para>
    /// </summary>
    [Fact]
    public void NoRackCheckAcceptsTheColourOfAnotherRackState()
    {
        (string Name, byte R, byte G, byte B)[] neighbours =
        [
            ("the rack ground (Border.rack Background #FF19141F)", 0x19, 0x14, 0x1F),
            ("the checked row fill (rack-row:checked Background #FF2A2130)", 0x2A, 0x21, 0x30),
            ("the hovered row (rack-row:pointerover Background #FF241E2A)", 0x24, 0x1E, 0x2A),
            ("the armed dot (Ellipse.dot.armed Fill #FF6B5B73)", 0x6B, 0x5B, 0x73),
            ("the selection marker (rack-row:checked BorderBrush #FFE066FF)", 0xE0, 0x66, 0xFF),
        ];

        var checks = RackChecks();
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
                var distance = Math.Max(Math.Abs(neighbour.R - r), Math.Max(Math.Abs(neighbour.G - g), Math.Abs(neighbour.B - b)));
                Assert.True(check.Tolerance < distance,
                    $"check '{check.Name}' expects {check.ExpectedColor} with tolerance {check.Tolerance}, which "
                    + $"also ACCEPTS {neighbour.Name} — they are only {distance} apart on the widest channel. A "
                    + "check that cannot tell two rack states apart cannot fail on either of them");
            }
        }

        // Not bookkeeping, and not only the vacuity pin the shape guard wants (SP-066): every
        // comparison above SKIPS exactly one neighbour, the check's own colour. So this arithmetic
        // holds only if every rack check expects one of the rack's five named liveries — and a
        // check expecting a colour the rack does not paint is a check nothing can ever satisfy.
        Assert.NotEmpty(checks);
        Assert.Equal(checks.Count * (neighbours.Length - 1), compared);
    }

    /// <summary>
    /// The bite, on synthetic pixels: each rack check rejects a surface painted entirely in the
    /// colour the OTHER state of that surface really is. These are the same rejections the real
    /// captures produced (0.000, 0.000, 0.000, 0.224, 0.000 — record.md §3), reproduced here so a
    /// widened threshold reddens the floor rather than waiting for the next headed run.
    /// </summary>
    [Theory]
    [InlineData("rack-row-unselected-ground", 0x2A, 0x21, 0x30)]   // the row is OPEN
    [InlineData("rack-row-selected-marker", 0x19, 0x14, 0x1F)]     // no marker: rack ground in the band
    [InlineData("rack-row-selected-fill", 0x19, 0x14, 0x1F)]       // the row is CLOSED
    [InlineData("rack-row-dot-armed", 0x2A, 0x21, 0x30)]           // no disc: fill showing through
    [InlineData("rack-row-dot-off", 0x6B, 0x5B, 0x73)]             // a filled disc, not a ring
    public void EachRackCheckRejectsTheOtherStatesColour(string name, byte r, byte g, byte b)
    {
        var check = RackChecks().Single(c => c.Name == name);
        var result = CheckEvaluator.Evaluate(check, Solid(64, 64, (r, g, b)));
        Assert.False(result.Passed);
        Assert.Equal(name, result.Name);
    }

    /// <summary>And the other half, which is not redundant: a check nobody can PASS is as useless
    /// as one nobody can fail, and a region shape typo produces exactly that.</summary>
    [Theory]
    [InlineData("rack-row-unselected-ground", 0x19, 0x14, 0x1F)]
    [InlineData("rack-row-selected-marker", 0xE0, 0x66, 0xFF)]
    [InlineData("rack-row-selected-fill", 0x2A, 0x21, 0x30)]
    [InlineData("rack-row-dot-armed", 0x6B, 0x5B, 0x73)]
    [InlineData("rack-row-dot-off", 0x2A, 0x21, 0x30)]
    public void EachRackCheckAcceptsItsOwnStatesColour(string name, byte r, byte g, byte b)
    {
        var check = RackChecks().Single(c => c.Name == name);
        Assert.True(CheckEvaluator.Evaluate(check, Solid(64, 64, (r, g, b))).Passed);
    }

    /// <summary>
    /// SP-107's hole, closed and kept closed. <c>capture.ps1</c> puts a top-most window on the
    /// interactive desktop and reads that desktop back, exactly as <c>RealDesktopCollection</c>'s
    /// fixtures do, and it ran OUTSIDE their lease until SP-122. Every element of the contract is
    /// asserted because dropping any one of them silently breaks a different half of it: the wrong
    /// path contends with nobody, <c>FileShare.None</c> makes the holder unnameable, and a
    /// <c>StreamWriter</c>'s BOM makes <c>HolderProcessId</c> read "no readable holder".
    /// </summary>
    [Fact]
    public void TheCaptureScriptTakesTheMachineWideRealDesktopLease()
    {
        var script = CaptureScriptCode();
        Assert.Contains("ccp-real-desktop.lease", script, StringComparison.Ordinal);
        Assert.Contains("[IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::Read", script, StringComparison.Ordinal);
        Assert.Contains("[Text.Encoding]::UTF8.GetBytes(\"pid=$PID\")", script, StringComparison.Ordinal);
        Assert.DoesNotContain("StreamWriter", script, StringComparison.Ordinal);

        // Taken before the app is launched, released after it exits: the WINDOW is the thing that
        // must not contend, not merely the pixel read.
        var take = script.IndexOf("Take-Lease", script.IndexOf("function Take-Lease", StringComparison.Ordinal) + 1, StringComparison.Ordinal);
        var launch = script.IndexOf("Process]::Start($exe)", StringComparison.Ordinal);
        var release = script.LastIndexOf("Release-Lease", StringComparison.Ordinal);
        Assert.InRange(take, 0, launch);
        Assert.InRange(release, launch, script.Length);
    }

    /// <summary>
    /// THE DETERMINISTIC START HAS TO COVER THE STORE THE RACK ACTUALLY WRITES TO, and it did not.
    /// <c>capture.ps1</c> deleted <c>settings.json</c> only; the rack rows' module dials live in
    /// <see cref="CcpClient.Desktop.Persistence.SessionPresetDocument.FileName"/> beside it
    /// (<c>SessionParticipant.cs:96</c>), so an <c>-State off</c> capture's right-click quick-toggle
    /// leaked into the NEXT run and made an <c>armed</c> capture read "Switched off.". Measured, on
    /// a real desktop, on the already-committed harness — not reasoned about.
    ///
    /// <para>The file name comes from the PRODUCT constant rather than being restated here, so
    /// renaming the store reddens this instead of silently un-covering the harness.</para>
    /// </summary>
    [Fact]
    public void TheCaptureScriptClearsThePresetStoreTheRackWritesTo()
    {
        var script = CaptureScriptCode();
        Assert.Contains(CcpClient.Desktop.Persistence.SessionPresetDocument.FileName, script, StringComparison.Ordinal);

        // Cleared BEFORE the app starts, or the app reads the stale file on the way up.
        var clear = script.IndexOf(CcpClient.Desktop.Persistence.SessionPresetDocument.FileName, StringComparison.Ordinal);
        var launch = script.IndexOf("Process]::Start($exe)", StringComparison.Ordinal);
        Assert.InRange(clear, 0, launch);
    }

    /// <summary>
    /// SP-116's rule: an unfenced screen read is a defect, not a flake — 34 misses in 1200 unfenced
    /// reads, 0 in 1500 fenced. The fence must be taken BEFORE the read, which is the only ordering
    /// that means anything, and the harness must fail rather than continue when it cannot be taken.
    /// </summary>
    [Fact]
    public void TheCaptureScriptFencesTheScreenReadBeforeTakingIt()
    {
        var script = CaptureScriptCode();
        var fence = script.IndexOf("[VerifyNative]::DwmFlush()", StringComparison.Ordinal);
        var read = script.IndexOf("$g.CopyFromScreen(", StringComparison.Ordinal);
        Assert.InRange(fence, 0, read);
        Assert.Contains("this read would be unfenced", script, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(script, @"\$g\.CopyFromScreen\("));
    }

    private static DecodedImage Solid(int width, int height, (byte R, byte G, byte B) colour)
    {
        var bgra = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++)
        {
            bgra[i * 4] = colour.B;
            bgra[i * 4 + 1] = colour.G;
            bgra[i * 4 + 2] = colour.R;
            bgra[i * 4 + 3] = 255;
        }

        return new DecodedImage(width, height, bgra);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine([directory.FullName, .. RepoAnchorParts])))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new Xunit.Sdk.XunitException(
            $"repo root not found above {AppContext.BaseDirectory} (anchor: {string.Join('/', RepoAnchorParts)}) — "
            + "the rack presentation guard refuses to skip");
    }
}
