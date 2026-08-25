using CcpVerify;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The Studio rack's presentation checks, and the capture path that earns them.
///
/// <para><b>What this file is NOT.</b> It is not the evidence. The evidence is four real captures
/// of the running shell on a real Windows desktop, checked by <c>CcpVerify</c> against
/// <c>client/tools/verify/checks.json</c>, with every check shown failing on a real capture of the
/// opposite state and on a real seeded regression in <c>MainWindow.axaml</c>. A headless assembly
/// cannot photograph anything and no fact here claims to.</para>
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
    /// DERIVE THE SURFACE SET, NEVER RESTATE IT — the audit's rule, applied to the manifest
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
    /// The surface guard above covers <c>-Surface</c> only, and a surface is half a capture:
    /// <c>capture.ps1</c> pairs each surface with the states it can actually DRIVE
    /// (<c>$statesFor</c>), and refuses an unpaired combination by name because it would otherwise
    /// capture whatever the last state happened to leave behind. A manifest check whose STATE is
    /// undrivable is the same decoration as one whose surface is.
    /// </summary>
    [Fact]
    public void EveryManifestSurfaceStatePairIsOneTheCaptureScriptCanDrive()
    {
        var script = CaptureScriptCode();
        var start = script.IndexOf("$statesFor = @{", StringComparison.Ordinal);
        Assert.InRange(start, 0, script.Length);
        var end = script.IndexOf("\n}", start, StringComparison.Ordinal);
        Assert.InRange(end, start, script.Length);

        var drivable = new HashSet<string>(StringComparer.Ordinal);
        foreach (System.Text.RegularExpressions.Match row in System.Text.RegularExpressions.Regex.Matches(
            script[start..end], @"'(?<surface>[a-z-]+)'\s*=\s*@\((?<states>[^)]*)\)"))
        {
            foreach (System.Text.RegularExpressions.Match state in
                System.Text.RegularExpressions.Regex.Matches(row.Groups["states"].Value, "'(?<s>[a-z-]+)'"))
            {
                drivable.Add($"{row.Groups["surface"].Value}/{state.Groups["s"].Value}");
            }
        }

        Assert.NotEmpty(drivable);
        foreach (var check in CheckManifest.Load(ManifestPath()))
        {
            Assert.Contains($"{check.Surface}/{check.State}", drivable);
        }
    }

    /// <summary>
    /// THE TOLERANCE RULE, MECHANICAL. A tolerance is the size of the defect it hides, and here the
    /// defect has a name and a number: <c>RadioButton.rack-row:pointerover</c> is
    /// <c>ElevatedSurface</c> (<c>MainWindow.axaml:98-100</c>), which is 22/22/45 from the rack's
    /// <c>SurfaceBg</c> ground under the CCP Default theme, and the checked <c>PanelAccent</c> fill
    /// is 18/18/4 from the hovered row. At the rail-door precedent of 24 the fill check would still
    /// pass on a row the mouse is merely RESTING on.
    ///
    /// <para>So this asserts the property rather than the constant: no rack check may accept the
    /// colour of a DIFFERENT rack state. Widen a tolerance past a neighbour and this names both.</para>
    ///
    /// <para><b>And one neighbour that is not a rack state at all.</b> It was added when the
    /// palette moved onto the shipping product's values, and it is the tighter of the two
    /// constraints on this surface — see the second half of the fact.</para>
    /// </summary>
    [Fact]
    public void NoRackCheckAcceptsTheColourOfAnotherRackState()
    {
        // THE FIVE LIVERIES, AS THE THEME PAINTS THEM. Three of these five are mod-supplied and
        // moved when CCP Default was applied over the token seed; the hovered row (ElevatedSurface)
        // and the armed dot (TextDim) are supplied by no mod theme and did not.
        (string Name, byte R, byte G, byte B)[] neighbours =
        [
            ("the rack ground (Border.rack, SurfaceBg #FF0C0C13)", 0x0C, 0x0C, 0x13),
            ("the checked row fill (rack-row:checked, PanelAccent #FF34343C)", 0x34, 0x34, 0x3C),
            ("the hovered row (rack-row:pointerover, ElevatedSurface #FF222240)", 0x22, 0x22, 0x40),
            ("the armed dot (Ellipse.dot.armed Fill, TextDim #FF7A7A94)", 0x7A, 0x7A, 0x94),
            ("the selection marker (rack-row:checked, ShellAccentBright #FFFF6FB5)", 0xFF, 0x6F, 0xB5),
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

        // Not bookkeeping, and not only the vacuity pin the shape guard wants: every
        // comparison above SKIPS exactly one neighbour, the check's own colour. So this arithmetic
        // holds only if every rack check expects one of the rack's five named liveries — and a
        // check expecting a colour the rack does not paint is a check nothing can ever satisfy.
        Assert.NotEmpty(checks);
        Assert.Equal(checks.Count * (neighbours.Length - 1), compared);

        // THE NEIGHBOUR THAT IS NOT A RACK STATE, and it is why the rack's ground carries the
        // tightest tolerance in the manifest. The rack's ground is SurfaceBg and the pop quiz
        // card's GDI ground is upstream's own COLORREF 0x002E1A1A = #1A1A2E
        // (Input/Win32InputPresence). When both products were on the SEED dictionary those two
        // surfaces were 2/2/2 apart and no tolerance above 1 could tell a photograph of the rack
        // from a photograph of that card. Under the CCP Default theme SurfaceBg is the mod's own
        // #0C0C13 and the pair is 14/14/27 apart, so the ceiling RELAXED to 14 - and the tolerance
        // stayed at 1, because a tolerance is never widened back because it became comfortable.
        //
        // THE DISTANCE IS DERIVED HERE RATHER THAN WRITTEN DOWN, which is what let this survive the
        // theme without anybody remembering to look at it: the assertion reads the manifest's own
        // colour and recomputes. It is asserted HERE as well as in PopQuizCardPresentationTests
        // because that guard only sizes the CARD's tolerances - with nothing on this side, the
        // rack's ground could be widened back to its pre-flip 6 and nothing at all would go red.
        (byte R, byte G, byte B) popQuizCardGround = (0x1A, 0x1A, 0x2E);
        var rackGround = checks.Single(c => c.Name == "rack-row-unselected-ground");
        var (gr, gg, gb) = CheckManifest.ParseColor(rackGround.ExpectedColor, "check 'rack-row-unselected-ground':");
        var toCard = Math.Max(
            Math.Abs(popQuizCardGround.R - gr),
            Math.Max(Math.Abs(popQuizCardGround.G - gg), Math.Abs(popQuizCardGround.B - gb)));
        Assert.True(rackGround.Tolerance < toCard,
            $"check 'rack-row-unselected-ground' expects {rackGround.ExpectedColor} with tolerance "
            + $"{rackGround.Tolerance}, which also ACCEPTS the pop quiz card's own ground #1A1A2E - they are only "
            + $"{toCard} apart on the widest channel, so a photograph of that card would pass a check that exists "
            + "to say the Studio rack was on the screen");
    }

    /// <summary>
    /// <b>THE THRESHOLD IS THE WHOLE BITE OF THE NARROWEST CHECK, AND NOTHING PINNED IT.</b>
    /// <c>Ellipse.dot</c> strokes <c>#FF7A7A94</c> and <c>Ellipse.dot.armed</c> FILLS AND STROKES
    /// the same colour (<c>MainWindow.axaml:386-389</c>), so <c>rack-row-dot-armed</c> separates a
    /// filled disc from a hollow ring by AREA alone — 0.714 against 0.224. Drop its
    /// <c>minPixelFraction</c> from 0.5 to 0.2 and it passes on the real <c>off</c> capture while
    /// every other fact in this file stays green: the tolerance guard reads only
    /// <c>Tolerance</c>, and the synthetic buffers below are solid colours that score 0.000 either
    /// way. A green floor would then be equally consistent with a check that no longer bites.
    ///
    /// <para>So each threshold is pinned STRICTLY BETWEEN the two fractions its own real captures
    /// produced (<c>record.md</c> §3). The extra 0.1 clearance is required on the WRONG side only,
    /// and the asymmetry is deliberate: a threshold creeping down toward the wrong-state fraction
    /// is a check going quietly blind, whereas a threshold close under a pass fraction of exactly
    /// 1.000 is what a flat, unantialiased fill legitimately earns.</para>
    /// </summary>
    [Fact]
    public void EveryRackThresholdSitsBetweenTheFractionsItsOwnCapturesProduced()
    {
        // (check, fraction on the WRONG state's real capture, fraction on its OWN real capture)
        (string Name, double Wrong, double Pass)[] measured =
        [
            ("rack-row-unselected-ground", 0.000, 1.000),
            ("rack-row-selected-marker", 0.000, 0.862),
            ("rack-row-selected-fill", 0.000, 1.000),
            ("rack-row-dot-armed", 0.224, 0.714),
            ("rack-row-dot-off", 0.000, 1.000),
        ];

        var checks = RackChecks();
        var pinned = 0;
        foreach (var (name, wrong, pass) in measured)
        {
            var threshold = checks.Single(c => c.Name == name).MinPixelFraction;
            pinned++;
            Assert.True(threshold > wrong && threshold < pass,
                $"check '{name}' has minPixelFraction {threshold}, which is not strictly between the "
                + $"fractions its own real captures produced: {wrong} on the wrong state, {pass} on its "
                + "own. Outside that window the check either cannot fail or cannot pass");
            Assert.True(threshold - wrong >= 0.1,
                $"check '{name}' has minPixelFraction {threshold}, only {threshold - wrong:F3} above the "
                + $"{wrong} its WRONG state really scores. That is a check on the edge of not biting; "
                + "if the states are genuinely that close, the check is measuring the wrong thing");
        }

        // Every rack check is covered by the table, and the table names no check that has gone —
        // otherwise a new check could ship with an unpinned threshold and this would not notice.
        Assert.Equal(checks.Count, pinned);
    }

    /// <summary>
    /// The bite, on synthetic pixels: each rack check rejects a surface painted entirely in the
    /// colour the OTHER state of that surface really is. Note what this can and cannot stand in
    /// for — a solid buffer scores 0.000 for every one of these, INCLUDING
    /// <c>rack-row-dot-armed</c>, whose real wrong-state capture scores 0.224 because a hollow ring
    /// is not a solid ground. The 0.224 is pinned by
    /// <see cref="EveryRackThresholdSitsBetweenTheFractionsItsOwnCapturesProduced"/>, not here.
    /// </summary>
    [Theory]
    [InlineData("rack-row-unselected-ground", 0x34, 0x34, 0x3C)]   // the row is OPEN
    [InlineData("rack-row-selected-marker", 0x0C, 0x0C, 0x13)]     // no marker: rack ground in the band
    [InlineData("rack-row-selected-fill", 0x0C, 0x0C, 0x13)]       // the row is CLOSED
    [InlineData("rack-row-dot-armed", 0x34, 0x34, 0x3C)]           // no disc: fill showing through
    [InlineData("rack-row-dot-off", 0x7A, 0x7A, 0x94)]             // a filled disc, not a ring
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
    [InlineData("rack-row-unselected-ground", 0x0C, 0x0C, 0x13)]
    [InlineData("rack-row-selected-marker", 0xFF, 0x6F, 0xB5)]
    [InlineData("rack-row-selected-fill", 0x34, 0x34, 0x3C)]
    [InlineData("rack-row-dot-armed", 0x7A, 0x7A, 0x94)]
    [InlineData("rack-row-dot-off", 0x34, 0x34, 0x3C)]
    public void EachRackCheckAcceptsItsOwnStatesColour(string name, byte r, byte g, byte b)
    {
        var check = RackChecks().Single(c => c.Name == name);
        Assert.True(CheckEvaluator.Evaluate(check, Solid(64, 64, (r, g, b))).Passed);
    }

    /// <summary>
    /// The real-desktop hole, closed and kept closed. <c>capture.ps1</c> puts a top-most window on the
    /// interactive desktop and reads that desktop back, exactly as <c>RealDesktopCollection</c>'s
    /// fixtures do, and it ran OUTSIDE their lease until this packet. Every element of the contract is
    /// asserted because dropping any one of them silently breaks a different half of it: the wrong
    /// path contends with nobody, the wrong share mode excludes nobody, and a <c>StreamWriter</c>'s BOM
    /// makes <c>HolderProcessId</c> read "no readable holder".
    ///
    /// <para><b>THE CALL SITE IS MATCHED LINE-EXACTLY, and the first version of this guard was
    /// INERT for want of that.</b> It searched for <c>"Take-Lease"</c> starting one character past
    /// <c>"function Take-Lease"</c>, which finds the substring eight characters later — still
    /// inside the DEFINITION header, never the call. Review deleted the call line outright and
    /// every one of the six assertions stayed green: the script could run entirely unleased with
    /// this test passing. A definition is not an invocation, and a guard over this packet's #1
    /// correctness requirement has to know the difference.</para>
    ///
    /// <para><b>AND THE SHARE-MODE PIN WENT INERT A SECOND TIME, THE SAME WAY, MEASURED 2026-08-25.</b>
    /// This paragraph used to say <c>FileShare.None</c> "makes the holder unnameable", and on the day it
    /// was written that was TRUE: the holder's pid went into the lease file itself, which an exclusive
    /// lock makes unreadable. <see cref="RealDesktopLease"/> has since split the two — the lock on the
    /// lease file, the identity in a <c>.holder</c> sidecar nothing holds open — so <c>FileShare.None</c>
    /// is now both the only mode that excludes on Unix AND fully nameable, and this script has been
    /// corrected to it. The pin did not notice EITHER WAY, because the needle it used
    /// (<c>...::FileShare]::Read</c>) is a PREFIX of the sidecar open's <c>...::ReadWrite</c> and matched
    /// that line instead. Measured: with the drift deliberately put back on the lease open, this test
    /// stayed GREEN; it only reddened when the sidecar open was changed too. The needles below are
    /// therefore anchored to the variable AND the closing parenthesis, so each one can match exactly one
    /// open. The broader contract — that the harness leases at all, on RealDesktopLease's values — is
    /// <see cref="HarnessLeaseGuardTests"/>, which covers <c>capture-wslg.sh</c> as well.</para>
    /// </summary>
    [Fact]
    public void TheCaptureScriptTakesTheMachineWideRealDesktopLease()
    {
        var script = CaptureScriptCode();
        Assert.Contains("ccp-real-desktop.lease", script, StringComparison.Ordinal);
        Assert.Contains(
            "$script:leasePath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)",
            script, StringComparison.Ordinal);
        Assert.Contains(
            "$script:holderPath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::ReadWrite)",
            script, StringComparison.Ordinal);
        Assert.Contains("[Text.Encoding]::UTF8.GetBytes(\"pid=$PID\")", script, StringComparison.Ordinal);
        Assert.DoesNotContain("StreamWriter", script, StringComparison.Ordinal);

        // A whole line that is exactly `Take-Lease` is a CALL. Nothing else in PowerShell is.
        var call = System.Text.RegularExpressions.Regex.Match(
            script, @"^Take-Lease[ \t]*\r?$", System.Text.RegularExpressions.RegexOptions.Multiline);
        Assert.True(call.Success,
            "capture.ps1 defines Take-Lease but never CALLS it at top level — the script would run "
            + "unleased, which is exactly the contended-desktop condition this guard exists to prevent");

        // Taken before the app is launched, released after it exits: the WINDOW is the thing that
        // must not contend, not merely the pixel read.
        var launch = script.IndexOf("Process]::Start($exe)", StringComparison.Ordinal);
        Assert.InRange(launch, 0, script.Length);
        Assert.InRange(call.Index, 0, launch);

        // Released on EVERY path, not just the happy one: once from Fail (indented, inside the
        // function) and once at top level after the process has exited. A single call site means a
        // failed capture wedges the machine for whoever runs next.
        var releases = System.Text.RegularExpressions.Regex.Matches(
            script, @"^[ \t]*Release-Lease[ \t]*\r?$", System.Text.RegularExpressions.RegexOptions.Multiline);
        Assert.True(releases.Count >= 2,
            $"capture.ps1 calls Release-Lease {releases.Count} time(s); it must release on the failure "
            + "path as well as the success path, or a failed capture leaves the lease held");
        Assert.InRange(releases[^1].Index, launch, script.Length);
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
    /// The rule is that an unfenced screen read is a defect, not a flake — 34 misses in 1200 unfenced
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
