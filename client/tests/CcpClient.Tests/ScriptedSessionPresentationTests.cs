using CcpClient.Desktop.Session;
using CcpClient.Desktop.Views.Pages;
using CcpVerify;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The session rack's presentation checks, and the capture path that earns them.
///
/// <para><b>What this file is NOT.</b> It is not the evidence. The evidence is five real captures
/// of the running shell on a real Windows desktop, checked by <c>CcpVerify</c> against
/// <c>client/tools/verify/checks.json</c>: each of the four checks scored 1.0000 on its own capture
/// and 0.0000 on the other state's, and 0.0000 on a capture of the whole dashboard — a photograph
/// that CONTAINS both surfaces and still cannot pass either check. A headless assembly cannot
/// photograph anything and no fact here claims to.</para>
///
/// <para><b>What it IS.</b> The things that rot silently between headed runs: a check demoted out
/// of the class a headless frame can never discharge, a surface or state the script can no longer
/// be asked for (those two live in <see cref="RackPresentationTests"/> and already cover every
/// check in the manifest), a tolerance widened past the colour it exists to reject, a threshold
/// that has stopped biting, and — the one that is specific to this surface — a capture path that
/// stopped confirming the session is really RUNNING before it reads pixels.</para>
///
/// <para><b>Why the last one matters more here than anywhere else on the manifest.</b> Every other
/// surface in this harness is a style or a selection: it is either painted or it is not. This one
/// has a state that only exists because a user asked for it, and the failure mode is specific —
/// a capture taken after a click that silently did nothing would photograph a perfectly plausible
/// pink button and pass the <c>idle</c> check. The gates are what forbid that, and this pins that
/// they are still there.</para>
/// </summary>
public class ScriptedSessionPresentationTests
{
    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];

    /// <summary>Every session check, by name. The manifest is read from disk, never restated
    /// here.</summary>
    private static IReadOnlyList<ManifestCheck> SessionChecks() =>
        [.. CheckManifest.Load(ManifestPath()).Where(c => c.Surface.StartsWith("session-", StringComparison.Ordinal))];

    private static string ManifestPath() =>
        Path.Combine(FindRepoRoot(), "client", "tools", "verify", "checks.json");

    private static string CaptureScriptCode() =>
        string.Join(
            '\n',
            File.ReadAllLines(Path.Combine(FindRepoRoot(), "client", "tools", "verify", "capture.ps1"))
                .Where(line => !line.TrimStart().StartsWith('#')));

    [Fact]
    public void EverySessionCheckClaimsPresentationVerified()
    {
        var checks = SessionChecks();
        Assert.Equal(6, checks.Count);
        Assert.All(checks, check => Assert.Equal(CheckManifest.EvidencePresentation, check.EvidenceClass));
    }

    /// <summary>
    /// Both surfaces in BOTH of their states. A surface checked in one state only cannot
    /// distinguish anything: the whole bite proof is that the other state's real capture fails it,
    /// and there has to BE another state for that to mean something.
    /// </summary>
    [Fact]
    public void BothSessionSurfacesAreCheckedInBothStates()
    {
        var pairs = SessionChecks()
            .Select(c => $"{c.Surface}/{c.State}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "session-history/kept", "session-history/not-kept",
                "session-row/easy", "session-row/hard",
                "session-start/idle", "session-start/running",
            ],
            pairs);
    }

    /// <summary>
    /// THE TOLERANCE RULE, MECHANICAL, and on this surface it has a named victim. The idle button
    /// is <c>#FFD05CE8</c> and the rail door's SELECTED border is <c>#FFE066FF</c> — 23 apart on
    /// their widest channel and 10 on their narrowest. At the rail door's own precedent of 32 the
    /// idle check would pass on a photograph of a selected rail door, which is a different control
    /// on a different part of the shell.
    ///
    /// <para>So this asserts the property rather than the constants: no session check may accept a
    /// colour this app declares somewhere else. Widen a tolerance past a neighbour and this names
    /// both.</para>
    /// </summary>
    [Fact]
    public void NoSessionCheckAcceptsTheColourOfAnotherDeclaredState()
    {
        (string Name, byte R, byte G, byte B)[] neighbours =
        [
            ("the Easy stripe (Colors.xaml:191 SessionDiffEasy)", 0x57, 0xD9, 0xA3),
            ("the Medium stripe (Colors.xaml:193 SessionDiffMedium)", 0xF5, 0xC2, 0x42),
            ("the Hard stripe (Colors.xaml:195 SessionDiffHard)", 0xFF, 0x8A, 0x4C),
            ("the Extreme stripe (Colors.xaml:197 SessionDiffExtreme)", 0xF2, 0x35, 0x57),
            ("the idle session button (Button.session-start, MainWindow.axaml:352)", 0xD0, 0x5C, 0xE8),
            ("the running session button (session-start.running, :364)", 0xFF, 0x6B, 0x6B),
            ("the selected rail door (RadioButton.door:checked, :69)", 0xE0, 0x66, 0xFF),
            ("the module panel ground (Border.module, :122)", 0x1B, 0x16, 0x22),
            ("the notice ground (Border.notice, :129) and the history plate", 0x24, 0x1E, 0x2A),
            ("the rack ground (Border.rack, :117)", 0x19, 0x14, 0x1F),
            ("the selected rack row and the history row card (:102, Button.history-row)", 0x2A, 0x21, 0x30),
        ];

        var checks = SessionChecks();
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
                    + "check that cannot tell two of this app's surfaces apart cannot fail on either of them");
            }
        }

        // Every comparison above SKIPS exactly one neighbour — the check's own colour — so this
        // arithmetic holds only if every session check expects a colour this app really declares.
        // A check expecting a colour nothing paints is a check nothing can satisfy.
        Assert.NotEmpty(checks);
        Assert.Equal(checks.Count * (neighbours.Length - 1), compared);
    }

    /// <summary>
    /// Each threshold sits strictly between the fractions its OWN real captures produced, with the
    /// clearance required on the WRONG side — the rack's rule, and the numbers here are the ones
    /// the five headed captures really scored.
    /// </summary>
    [Fact]
    public void EverySessionThresholdSitsBetweenTheFractionsItsOwnCapturesProduced()
    {
        // (check, fraction on the OTHER state's real capture, fraction on its OWN)
        (string Name, double Wrong, double Pass)[] measured =
        [
            ("session-row-easy-stripe", 0.000, 1.000),
            ("session-row-hard-stripe", 0.000, 1.000),
            ("session-start-idle-fill", 0.000, 1.000),
            ("session-start-running-fill", 0.000, 1.000),
            // Measured 2026-08-24 on the two real history captures: each scored 2352/2352 EXACT on
            // its own and 0/2352 on the other, where the only difference between the two runs is
            // that one session was left running for 34 seconds and the other for 0.
            ("session-history-kept-row-fill", 0.000, 1.000),
            ("session-history-not-kept-plate", 0.000, 1.000),
        ];

        var checks = SessionChecks();
        var pinned = 0;
        foreach (var (name, wrong, pass) in measured)
        {
            var threshold = checks.Single(c => c.Name == name).MinPixelFraction;
            pinned++;
            Assert.True(threshold > wrong && threshold < pass,
                $"check '{name}' has minPixelFraction {threshold}, which is not strictly between the fractions "
                + $"its own real captures produced: {wrong} on the wrong state, {pass} on its own");
            Assert.True(threshold - wrong >= 0.1,
                $"check '{name}' has minPixelFraction {threshold}, only {threshold - wrong:F3} above the {wrong} "
                + "its WRONG state really scores — a check on the edge of not biting");
        }

        Assert.Equal(checks.Count, pinned);
    }

    /// <summary>
    /// The bite, on synthetic pixels: every session check rejects the colour of its surface's OTHER
    /// state, and every one of them rejects an ALL-BLACK buffer.
    ///
    /// <para><b>The black case is in here because this board has already been burned by it</b> —
    /// <c>capture-wslg.sh</c> printed CAPTURE PASS over an all-black image. A check that cannot
    /// fail on black is a check that would have called that a pass.</para>
    /// </summary>
    [Fact]
    public void EverySessionCheckRejectsTheOtherStatesColour_AndBlack()
    {
        (string Name, byte R, byte G, byte B)[] wrong =
        [
            ("session-row-easy-stripe", 0xFF, 0x8A, 0x4C),      // the Hard row's stripe
            ("session-row-hard-stripe", 0x57, 0xD9, 0xA3),      // the Easy row's stripe
            ("session-start-idle-fill", 0xFF, 0x6B, 0x6B),      // the button while a session runs
            ("session-start-running-fill", 0xD0, 0x5C, 0xE8),   // the button before one does
            ("session-history-kept-row-fill", 0x24, 0x1E, 0x2A),     // the empty plate, where the row is not
            ("session-history-not-kept-plate", 0x2A, 0x21, 0x30),    // the row card, where the plate is not
        ];

        // The cardinality pin the vacuous-shape ledger asks for: a loop is the only assertion in
        // this fact, so an empty table would make it pass while proving nothing.
        Assert.Equal(SessionChecks().Count, wrong.Length);

        foreach (var (name, r, g, b) in wrong)
        {
            var check = SessionChecks().Single(c => c.Name == name);
            Assert.False(CheckEvaluator.Evaluate(check, Solid(64, 64, (r, g, b))).Passed);
            Assert.False(CheckEvaluator.Evaluate(check, Solid(64, 64, (0, 0, 0))).Passed);
        }
    }

    /// <summary>And the other half, which is not redundant: a check nobody can PASS is as useless
    /// as one nobody can fail, and a region typo produces exactly that.</summary>
    [Fact]
    public void EverySessionCheckAcceptsItsOwnStatesColour()
    {
        (string Name, byte R, byte G, byte B)[] own =
        [
            ("session-row-easy-stripe", 0x57, 0xD9, 0xA3),
            ("session-row-hard-stripe", 0xFF, 0x8A, 0x4C),
            ("session-start-idle-fill", 0xD0, 0x5C, 0xE8),
            ("session-start-running-fill", 0xFF, 0x6B, 0x6B),
            ("session-history-kept-row-fill", 0x2A, 0x21, 0x30),
            ("session-history-not-kept-plate", 0x24, 0x1E, 0x2A),
        ];

        // The same cardinality pin, for the same reason.
        Assert.Equal(SessionChecks().Count, own.Length);

        foreach (var (name, r, g, b) in own)
        {
            var check = SessionChecks().Single(c => c.Name == name);
            Assert.True(CheckEvaluator.Evaluate(check, Solid(64, 64, (r, g, b))).Passed);
        }
    }

    /// <summary>
    /// THE GATES. Before either <c>session-start</c> capture reads a pixel, the script confirms the
    /// surface is really there and really in the state the check names — by reading the product's
    /// own controls through UIA: the rack's four session names, the confirmation's promise, the
    /// readout's phase and countdown, and the button's own caption. Without those a capture taken
    /// after a click that silently did nothing photographs a perfectly plausible pink button and
    /// passes the idle check.
    ///
    /// <para><b>Honest limit: this is LEXICAL.</b> It proves the gates have not been DELETED. What
    /// proves they work is the run that failed by name when the first draft demanded
    /// <c>00:00 elapsed</c> and the real desktop had already reached <c>00:01 elapsed,
    /// 29:58 remaining</c> — a real session's clock really running, caught by the gate rather than
    /// by a pixel.</para>
    /// </summary>
    [Fact]
    public void TheCaptureScriptConfirmsTheSessionIsRunningBeforeItReadsAnyPixel()
    {
        var script = CaptureScriptCode();
        foreach (var needle in new[]
        {
            "ScriptedSessionConfirmTitle", "ScriptedSessionConfirmDetail", "ScriptedSessionConfirmPromise",
            "ScriptedSessionPhaseState", "ScriptedSessionProgressState", "ScriptedSessionStartButton",
            "ScriptedSessionConfirmButton", "RowScriptedSession", "SessionRowMorningDrift",
        })
        {
            Assert.Contains(needle, script, StringComparison.Ordinal);
        }

        // The ceremony is CONFIRMED, not stepped around: the script asserts nothing has started
        // while the confirmation is up, and only then presses its Start Session.
        Assert.Contains("a session started before the confirmation was answered", script, StringComparison.Ordinal);
        Assert.Contains("restored when the session ends", script, StringComparison.Ordinal);

        // Every gate is read BEFORE the one screen read in this script.
        var read = script.IndexOf("CopyFromScreen", StringComparison.Ordinal);
        Assert.InRange(read, 0, script.Length);
        Assert.InRange(script.IndexOf("run gate:", StringComparison.Ordinal), 0, read);
        Assert.InRange(script.IndexOf("confirm gate:", StringComparison.Ordinal), 0, read);
        Assert.InRange(script.IndexOf("rack gate:", StringComparison.Ordinal), 0, read);
    }

    /// <summary>
    /// THE HISTORY PAIR'S OWN GATES, and they carry more than the others because the two captures
    /// are the same window in the same place: what makes them different is a RULE, and the rule is
    /// read off the running product before any pixel.
    ///
    /// <para>The retention gate parses the elapsed time out of the stop confirmation's own line and
    /// refuses a <c>kept</c> capture whose run was under 30 seconds — or a <c>not-kept</c> capture
    /// whose run was over it. Without it, a machine slow enough to spend 30 seconds between two
    /// clicks would photograph a row and call it the empty state.</para>
    ///
    /// <para>The recap gate is the only evidence in this repository that the Session Complete
    /// window really opens on a real desktop: it reads the window's headline, the session it names,
    /// its MM:SS duration cell, both of its refusals and its empty-media line, out of a SECOND
    /// top-level window that a headless frame cannot produce.</para>
    ///
    /// <para><b>Honest limit, the same one the run gate has: this is LEXICAL.</b> It proves the
    /// gates have not been deleted. What proves they work is the run that failed by name when the
    /// recap could not be clicked — the shell was HWND_TOPMOST, so the press landed on the shell —
    /// and the run before that, which found an owned Avalonia window is a UIA DESCENDANT of its
    /// owner rather than a sibling of it.</para>
    /// </summary>
    [Fact]
    public void TheHistoryCapturesReadTheRetentionRuleAndTheRecapBeforeAnyPixel()
    {
        var script = CaptureScriptCode();
        foreach (var needle in new[]
        {
            "ScriptedSessionHistoryButton", "SessionRecapHeadline", "SessionRecapDuration",
            "SessionRecapNamesNotice", "SessionRecapAwardsNotice", "SessionRecapNoMedia",
            "SessionHistoryCount", "SessionHistoryEmpty", "SessionHistoryStatus0", "SessionHistoryRow0",
            "SessionHistoryHeader",
        })
        {
            Assert.Contains(needle, script, StringComparison.Ordinal);
        }

        // The two refusals are read off the WINDOW, not restated by the script.
        Assert.Contains("never a name or a path", script, StringComparison.Ordinal);
        Assert.Contains("No XP", script, StringComparison.Ordinal);

        // The rule itself: each state refuses the wrong side of the floor by name.
        Assert.Contains("would not be kept", script, StringComparison.Ordinal);
        Assert.Contains("would be kept", script, StringComparison.Ordinal);
        Assert.Contains(
            $"-lt {(int)ScriptedSessionLogStore.PersistenceMinDuration.TotalSeconds}",
            script,
            StringComparison.Ordinal);

        var read = script.IndexOf("CopyFromScreen", StringComparison.Ordinal);
        Assert.InRange(script.IndexOf("retention gate:", StringComparison.Ordinal), 0, read);
        Assert.InRange(script.IndexOf("recap gate:", StringComparison.Ordinal), 0, read);
        Assert.InRange(script.IndexOf("history gate:", StringComparison.Ordinal), 0, read);

        // And the derived cell is cross-checked against the one state that has a real row.
        Assert.Contains("the history list does not start where the plate says it does", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// THE DERIVATION AND THE PRODUCT AGREE. The stripe cell is 7x35 pixels at scale 1.75 and is
    /// derived, not searched for: a <see cref="Border"/> has no automation peer (harness surprise
    /// #1), so the capture computes the cell from the row's rect and the meta cell's rect using the
    /// row's own geometry constants. If the product changes one of them and the script does not,
    /// the capture aims 14 pixels away from the thing it names — and a stripe check that
    /// photographs the row's background would simply fail, which looks like a regression in the
    /// product rather than in the harness.
    ///
    /// <para>The script's own cross-check ("the row grid does not close") catches this at capture
    /// time, and it really did: it refused a stripe 43 DIP short of the trailing edge and that
    /// found a real layout defect in the row. This catches it on every floor run instead.</para>
    /// </summary>
    [Fact]
    public void TheCaptureScriptDerivesTheStripeCellFromTheProductsOwnGeometry()
    {
        var script = CaptureScriptCode();
        Assert.Equal(4d, StudioPage.StripeWidth);
        Assert.Equal(20d, StudioPage.StripeHeight);
        Assert.Equal(10d, StudioPage.RowGutter);

        Assert.Contains(
            $"$stripeW = [int][math]::Round({StudioPage.StripeWidth} * $scale)", script, StringComparison.Ordinal);
        Assert.Contains(
            $"$stripeH = [int][math]::Round({StudioPage.StripeHeight} * $scale)", script, StringComparison.Ordinal);
        Assert.Contains(
            $"$pad = [int][math]::Round({StudioPage.RowGutter} * $scale)", script, StringComparison.Ordinal);

        // And the cross-check itself is still there: a derivation with no proof that the grid
        // closes is a derivation that can aim anywhere and say nothing.
        Assert.Contains("the session row grid does not close", script, StringComparison.Ordinal);
    }

    private static DecodedImage Solid(int width, int height, (byte R, byte G, byte B) color)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++)
        {
            pixels[(i * 4) + 0] = color.B;
            pixels[(i * 4) + 1] = color.G;
            pixels[(i * 4) + 2] = color.R;
            pixels[(i * 4) + 3] = 0xFF;
        }

        return new DecodedImage(width, height, pixels);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, RepoAnchorParts[0], RepoAnchorParts[1])))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("repository root not found from " + AppContext.BaseDirectory);
    }
}
