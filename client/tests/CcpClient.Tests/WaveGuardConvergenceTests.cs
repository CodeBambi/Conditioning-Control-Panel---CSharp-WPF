using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-136 (board row 32). Two guards that were supposed to be one implementation drifted, and a
/// packet could print <c>WAVE OK</c> at the pre-launch gate and RED the suite from the authoring
/// commit onward. It happened to BOTH packets of wave 60.
///
/// <para><b>THE DEFECT.</b> <c>client/tools/wave/validate-wave.mjs</c> check 4 asked whether a
/// packet's declared <c>fileScopeMustNotChange</c> scope COVERS <c>client/tests/floor/floor.json</c>,
/// glob-aware. <c>FloorWrapperGuardTests.cs:224</c> asked whether the row CONTAINS that literal
/// string. So <c>client/tests/floor/**</c> satisfied one and failed the other. The validator's own
/// header carried a MIRROR note saying "do not let these two drift"; that sentence was present the
/// whole time they drifted, which is the evidence that notes do not enforce.</para>
///
/// <para><b>THE MECHANISM THAT REPLACED THE NOTE, and why it is "by construction" rather than
/// "unlikely".</b> There is now ONE implementation of the decision, in
/// <c>validate-wave.mjs</c> (<c>declarationCoversChokepoint</c> / <c>chokepointVerdict</c>). The C#
/// guard holds none: it consumes the script's <c>--emit-packet-scopes</c> projection and applies
/// only its own grandfather IDs and message text. Two implementations cannot disagree when there
/// is one of them. What remains to be proven, and is proven here, is that the C# really consumes
/// it — <see cref="TheFloorGuard_RedsWhenONLYTheValidatorChanges"/> mutates the JS alone and
/// watches the C# guard's verdict change, which no shared CONSTANT could deliver.</para>
///
/// <para><b>WHAT THESE FACTS DO NOT PROVE.</b> Nothing here renders, composites, focuses a window,
/// plays audio or animates; they are file- and process-level facts about two guards. The lexical
/// anti-shadow fact is incomplete by construction and says so at its own doc comment. And the
/// semantics choice is a FORWARD-looking hardening, not a repair of a live failure: on today's
/// corpus every bound packet that declares <c>client/tests/floor/**</c> also carries the literal
/// in the same cell, so adopting coverage changes the verdict of zero packets. What it buys is
/// that the next packet declaring only the glob cannot print WAVE OK and red the suite.</para>
/// </summary>
public class WaveGuardConvergenceTests
{
    private const string GlobPacket = "SP-900-declares-a-covering-glob";
    private const string LiteralPacket = "SP-901-declares-the-literal-path";
    private const string SiblingPacket = "SP-902-declares-a-sibling-file";
    private const string BelowBoundPacket = "SP-010-below-the-delta-bound";
    private const string AboveBoundPacket = "SP-903-above-the-delta-bound";

    private const string GlobCell = "`client/tests/floor/**`, `client/src/**`";
    private const string LiteralCell = "`client/tests/floor/floor.json`, `client/src/**`";
    private const string SiblingCell = "`client/tests/floor/floor.json.bak`";
    private const string CoversNothingCell = "`client/docs/**`";

    /// <summary>
    /// The single call site of the shared coverage decision inside <c>validate-wave.mjs</c>, and
    /// the literal-only semantics it drifted into. Used to build a one-sided mutation. The
    /// replacement count is asserted, so a refactor that moves this call site reds the fact rather
    /// than silently mutating nothing and passing.
    /// </summary>
    private const string CoverageCallSite = "declaredValues.some((p) => patternCovers(p, chokepointPath))";

    private const string LiteralOnlyMutation = "declaredValues.some((p) => p === chokepointPath)";

    /// <summary>
    /// The verdicts the shared decision must return, pinned HERE, in C#, deliberately not read out
    /// of the projection. If both sides of this comparison came from <c>validate-wave.mjs</c> the
    /// fact would self-certify and a lockstep edit to the script and its own fixture would sail
    /// through it. These are the third anchor.
    /// </summary>
    private static readonly (string Id, bool RowFound, bool Covered)[] PinnedCoverageVerdicts =
    [
        ("literal-only", true, true),
        ("covering-glob-floor-dir", true, true),
        ("covering-glob-tests-tree", true, true),
        ("covering-glob-client-tree", true, true),
        ("directory-prefix-without-a-glob", true, true),
        ("sibling-file-that-is-not-the-pin", true, false),
        ("near-miss-directory", true, false),
        ("unrelated-tree", true, false),
        ("prose-mention-not-backticked", true, false),
        ("windows-separators", true, true),
        ("case-only-difference", true, true),
        ("second-row-carries-it", true, true),
        ("no-scope-row-at-all", false, false),
    ];

    /// <summary>
    /// Predicate shapes that would put chokepoint-coverage logic back on the C# side. Named
    /// routes only — see the fact's own doc comment for what this cannot see.
    /// </summary>
    private static readonly string[] BannedCoveragePredicates =
    [
        "Contains(SharedFloorPin",
        "IndexOf(SharedFloorPin",
        "StartsWith(SharedFloorPin",
        "EndsWith(SharedFloorPin",
        "patternCovers",
        "PatternCovers",
    ];

    [Fact]
    public async Task AGlobAndALiteralDeclaration_AreAcceptedIdentically_ByBothGuards()
    {
        // Row 32's stated acceptance, as a FACT and not a comment, end to end through both real
        // guards. The glob packet's scope cell is byte-for-byte the shape both wave-60 packets
        // carried when the validator printed WAVE OK and the suite went red.
        var spine = BuildSyntheticSpineTasks((GlobPacket, GlobCell), (LiteralPacket, LiteralCell));
        try
        {
            var projection = await WaveScopeOracle.LoadAsync(WaveScopeOracle.ValidatorPath(), spine);
            var guardViolations = FloorWrapperGuardTests.ComputeChokepointViolations(spine, projection);
            var validatorViolations = projection.Packets.SelectMany(p => p.Violations).ToArray();
            var glob = projection.Packets.Single(p => p.Dir == GlobPacket);
            var literal = projection.Packets.Single(p => p.Dir == LiteralPacket);

            Assert.Equal(2, projection.Packets.Length);
            Assert.True(validatorViolations.Length == 0,
                "the wave validator rejected a packet pair that differs only in glob-versus-literal:"
                + Environment.NewLine + string.Join(Environment.NewLine, validatorViolations));
            Assert.True(guardViolations.Count == 0,
                "the floor guard rejected a packet pair that differs only in glob-versus-literal — this is the "
                + "SP-136 defect, and it is what reddened the base from the wave-60 authoring commit:"
                + Environment.NewLine + string.Join(Environment.NewLine, guardViolations));
            Assert.True(glob.FloorPin.Covered, $"`{GlobCell}` must cover the shared floor pin — it forbids the pin and more");
            Assert.True(literal.FloorPin.Covered, $"`{LiteralCell}` must cover the shared floor pin");
            Assert.Equal(literal.FloorPin.Covered, glob.FloorPin.Covered);
        }
        finally
        {
            DeleteTree(SyntheticRootOf(spine));
        }
    }

    [Fact]
    public async Task TheSharedDecision_AgreesWithTheVerdictsPinnedHere_OnEveryCase()
    {
        var projection = await WaveScopeOracle.DefaultAsync();
        var byId = projection.Cases.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var violations = new List<string>();

        foreach (var (id, rowFound, covered) in PinnedCoverageVerdicts)
        {
            if (!byId.TryGetValue(id, out var emitted))
            {
                violations.Add($"case '{id}' is pinned in WaveGuardConvergenceTests but is absent from the shared "
                    + "fixture in validate-wave.mjs — a case cannot be removed on one side only");
                continue;
            }

            if (emitted.Actual.RowFound != rowFound || emitted.Actual.Covered != covered)
            {
                violations.Add($"case '{id}': the shared chokepoint decision returned "
                    + $"{{rowFound:{emitted.Actual.RowFound}, covered:{emitted.Actual.Covered}}} but this test pins "
                    + $"{{rowFound:{rowFound}, covered:{covered}}} — {emitted.Why}");
            }

            if (emitted.Expect.RowFound != rowFound || emitted.Expect.Covered != covered)
            {
                violations.Add($"case '{id}': validate-wave.mjs's own fixture expects "
                    + $"{{rowFound:{emitted.Expect.RowFound}, covered:{emitted.Expect.Covered}}} but this test pins "
                    + $"{{rowFound:{rowFound}, covered:{covered}}} — the two sides of the convergence disagree about "
                    + "what the rule IS, which is the SP-136 class one level up");
            }
        }

        var pinnedIds = PinnedCoverageVerdicts.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var emitted in projection.Cases)
        {
            if (!pinnedIds.Contains(emitted.Id))
            {
                violations.Add($"case '{emitted.Id}' exists in validate-wave.mjs's fixture but is not pinned here — "
                    + "a case added on one side only is unwatched, which is how the fixture stops being a fixture");
            }
        }

        Assert.NotEmpty(PinnedCoverageVerdicts);
        Assert.NotEmpty(projection.Cases);
        Assert.True(violations.Count == 0,
            "shared chokepoint decision does not match the verdicts pinned in this test:"
            + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public async Task TheFloorGuard_RedsWhenONLYTheValidatorChanges()
    {
        // THE WHOLE CLAIM OF "BY CONSTRUCTION". A shared constant could not do this: change the
        // validator's semantics alone, and the C# guard's verdict must change with it, because the
        // C# guard has no opinion of its own to hold steady. The mutation restores the exact
        // literal-only semantics FloorWrapperGuardTests.cs:224 used to carry.
        var spine = BuildSyntheticSpineTasks((GlobPacket, GlobCell), (LiteralPacket, LiteralCell));
        var mutated = MutateValidator(CoverageCallSite, LiteralOnlyMutation);
        try
        {
            Assert.Equal(1, mutated.Replacements);

            var clean = await WaveScopeOracle.LoadAsync(WaveScopeOracle.ValidatorPath(), spine);
            var cleanViolations = FloorWrapperGuardTests.ComputeChokepointViolations(spine, clean);

            var drifted = await WaveScopeOracle.LoadAsync(mutated.Path, spine);
            var driftedViolations = FloorWrapperGuardTests.ComputeChokepointViolations(spine, drifted);

            Assert.True(cleanViolations.Count == 0,
                "the unmutated validator must accept both packets:"
                + Environment.NewLine + string.Join(Environment.NewLine, cleanViolations));
            Assert.True(driftedViolations.Count == 1,
                "a ONE-SIDED change to validate-wave.mjs must change this guard's verdict — it did not, so the C# side "
                + "is holding a shadow implementation and the convergence is decorative. Violations: "
                + Environment.NewLine + string.Join(Environment.NewLine, driftedViolations));
            Assert.Contains(GlobPacket, driftedViolations[0], StringComparison.Ordinal);
            Assert.Contains("does not cover", driftedViolations[0], StringComparison.Ordinal);
        }
        finally
        {
            DeleteTree(SyntheticRootOf(spine));
            DeleteTree(Path.GetDirectoryName(mutated.Path)!);
        }
    }

    /// <summary>
    /// The anti-shadow guard, and it is LEXICAL and therefore incomplete — stated rather than
    /// implied. It closes the named routes by which the old predicate could return (the literal
    /// substring test against <c>SharedFloorPin</c>, or a hand-rolled <c>patternCovers</c>). It
    /// does NOT prove no shadow exists: a fresh predicate over <c>MustNotChangeRows</c> mentioning
    /// none of these tokens would pass here. That route is closed by a different fact —
    /// <see cref="TheFloorGuard_RedsWhenONLYTheValidatorChanges"/>, which a shadow implementation
    /// makes fail, because the guard's answer would stop tracking the script's. Precedent for
    /// reading a source file as text: <c>ExecutionCensusTests.CensusGenerator_HoldsNoShapeLiteralOfItsOwn</c>.
    /// </summary>
    [Fact]
    public void TheFloorGuard_HoldsNoChokepointCoverageLogicOfItsOwn()
    {
        var guardSource = ReadRepoText("client", "tests", "CcpClient.Tests", "FloorWrapperGuardTests.cs");
        var violations = new List<string>();

        foreach (var banned in BannedCoveragePredicates)
        {
            if (guardSource.Contains(banned, StringComparison.Ordinal))
            {
                violations.Add($"FloorWrapperGuardTests.cs carries `{banned}` — chokepoint coverage is decided ONCE, in "
                    + "client/tools/wave/validate-wave.mjs, and consumed here. A second implementation is the SP-136 "
                    + "drift returning: the two agreed on the day it was written and diverged afterwards");
            }
        }

        Assert.NotEmpty(BannedCoveragePredicates);
        Assert.NotEmpty(guardSource);
        Assert.True(violations.Count == 0,
            "the floor guard has grown coverage logic of its own:"
            + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public async Task TheHistoricalLiteralPredicate_IsDetectedAsDrift()
    {
        // The defect replayed as a fact rather than told as a story. The predicate below is the
        // one FloorWrapperGuardTests.cs:224 carried, verbatim in behaviour, and it must be shown
        // to disagree with the surviving decision in BOTH directions — it REJECTS a covering glob
        // that genuinely forbids the pin, and it ACCEPTS a sibling file the lane may freely edit.
        var spine = BuildSyntheticSpineTasks((GlobPacket, GlobCell), (SiblingPacket, SiblingCell));
        try
        {
            var projection = await WaveScopeOracle.LoadAsync(WaveScopeOracle.ValidatorPath(), spine);
            var glob = projection.Packets.Single(p => p.Dir == GlobPacket);
            var sibling = projection.Packets.Single(p => p.Dir == SiblingPacket);

            Assert.True(glob.FloorPin.Covered, "the surviving decision covers a glob that forbids the pin");
            Assert.False(DriftedLiteralCoverage(GlobCell),
                "the historical literal predicate must be shown REJECTING the covering glob — if it now accepts it, "
                + "this fact has stopped watching the defect it was written for");

            Assert.False(sibling.FloorPin.Covered, "the surviving decision refuses a sibling file that is not the pin");
            Assert.True(DriftedLiteralCoverage(SiblingCell),
                "the historical literal predicate must be shown ACCEPTING `client/tests/floor/floor.json.bak` — this is "
                + "the direction in which the literal rule was not merely narrower but wrong, and it is why the literal "
                + "was not the semantics kept");

            Assert.NotEqual(glob.FloorPin.Covered, DriftedLiteralCoverage(GlobCell));
            Assert.NotEqual(sibling.FloorPin.Covered, DriftedLiteralCoverage(SiblingCell));
        }
        finally
        {
            DeleteTree(SyntheticRootOf(spine));
        }
    }

    [Fact]
    public async Task EveryPacketTheValidatorAccepts_TheFloorGuardAlsoAccepts()
    {
        // The incident stated directly: WAVE OK printed at the pre-launch gate while the suite was
        // red. Over the LIVE corpus. The converse is deliberately NOT asserted — the validator
        // legitimately raises more than the floor guard does (row cardinality, ID reuse, File Scope
        // disjointness), and it binds packets the guard grandfathers away. That asymmetry is
        // declared, not accidental.
        var spineTasks = RepoSpineTasks();
        var projection = await WaveScopeOracle.DefaultAsync();

        var guardViolations = new List<string>();
        guardViolations.AddRange(FloorWrapperGuardTests.ComputeWrapperViolations(spineTasks, projection));
        guardViolations.AddRange(FloorWrapperGuardTests.ComputeChokepointViolations(spineTasks, projection));

        var accepted = projection.Packets.Where(p => p.Violations.Length == 0).Select(p => p.Dir).ToArray();
        var violations = new List<string>();

        foreach (var dir in accepted)
        {
            var marker = $"/{dir}/PROMPT.md:";
            var mine = guardViolations.Where(v => v.Contains(marker, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (mine.Length > 0)
            {
                violations.Add($"packet {dir} is ACCEPTED by validate-wave.mjs (it would print WAVE OK) and REJECTED by "
                    + "the floor guard — that combination is the SP-136 incident exactly:"
                    + Environment.NewLine + string.Join(Environment.NewLine, mine));
            }
        }

        var unattributed = guardViolations
            .Where(v => !projection.Packets.Any(p => v.Contains($"/{p.Dir}/PROMPT.md:", StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        foreach (var orphan in unattributed)
        {
            violations.Add($"a floor-guard violation belongs to no packet in the projection, so it cannot be reconciled "
                + $"against the wave gate at all: {orphan}");
        }

        Assert.NotEmpty(projection.Packets);
        Assert.NotEmpty(accepted);
        Assert.True(violations.Count == 0,
            "the wave gate and the suite guard disagree about a packet:"
            + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public async Task TheValidatorAndTheFloorGuard_EnumerateTheSamePackets()
    {
        // The other half of what the MIRROR note used to assert by comment. A packet visible to one
        // walk and not the other is bound by one guard and invisible to the other, which is the
        // failure mode both guards exist to prevent.
        var spineTasks = RepoSpineTasks();
        var projection = await WaveScopeOracle.DefaultAsync();
        var enumerationProblems = new List<string>();
        var mine = FloorWrapperGuardTests.EnumeratePackets(spineTasks, enumerationProblems)
            .Select(p => p.PacketDir)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var theirs = projection.Packets.Select(p => p.Dir).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var onlyMine = mine.Except(theirs, StringComparer.OrdinalIgnoreCase).OrderBy(d => d, StringComparer.Ordinal).ToArray();
        var onlyTheirs = theirs.Except(mine, StringComparer.OrdinalIgnoreCase).OrderBy(d => d, StringComparer.Ordinal).ToArray();

        Assert.True(enumerationProblems.Count == 0,
            "this guard's own packet walk reported problems:"
            + Environment.NewLine + string.Join(Environment.NewLine, enumerationProblems));
        Assert.NotEmpty(mine);
        Assert.True(onlyMine.Length == 0,
            "packets the floor guard enumerates and validate-wave.mjs does not: " + string.Join(", ", onlyMine));
        Assert.True(onlyTheirs.Length == 0,
            "packets validate-wave.mjs enumerates and the floor guard does not: " + string.Join(", ", onlyTheirs));
        Assert.Equal(mine.Count, theirs.Count);
    }

    [Fact]
    public async Task PacketsBelowSp073_StayGrandfathered_EvenWhenTheirScopeCoversNothing()
    {
        // THE SECOND AXIS OF DIVERGENCE, and it bites the mechanism rather than the semantics. The
        // two guards bind DIFFERENT POPULATIONS on purpose: validate-wave.mjs applies check 4 with
        // no packet-number condition at all, so 60 of the 128 packets on disk violate it there,
        // while FloorWrapperGuardTests grandfathers everything below SP-073. Consuming the
        // validator's verdicts therefore REQUIRES re-applying that grandfather afterwards. If a
        // later refactor drops it, sixty packets go red at once and it looks like a corpus problem
        // rather than a guard problem — so it is pinned here.
        var spine = BuildSyntheticSpineTasks(
            (BelowBoundPacket, CoversNothingCell),
            (AboveBoundPacket, CoversNothingCell));
        try
        {
            var projection = await WaveScopeOracle.LoadAsync(WaveScopeOracle.ValidatorPath(), spine);
            var guardViolations = FloorWrapperGuardTests.ComputeChokepointViolations(spine, projection);
            var below = projection.Packets.Single(p => p.Dir == BelowBoundPacket);
            var above = projection.Packets.Single(p => p.Dir == AboveBoundPacket);

            // The validator binds BOTH: it has no grandfather on this check.
            Assert.Contains(below.Violations, v => v.StartsWith("[check 4]", StringComparison.Ordinal));
            Assert.Contains(above.Violations, v => v.StartsWith("[check 4]", StringComparison.Ordinal));

            // The floor guard binds only the one at or above SP-073.
            var belowHits = guardViolations.Where(v => v.Contains(BelowBoundPacket, StringComparison.Ordinal)).ToArray();
            var aboveHits = guardViolations.Where(v => v.Contains(AboveBoundPacket, StringComparison.Ordinal)).ToArray();

            Assert.True(belowHits.Length == 0,
                $"packet {BelowBoundPacket} is below SP-073 and must stay grandfathered even though its scope covers "
                + "nothing — the grandfather is re-applied AFTER consuming the projection, and dropping it would newly "
                + "red sixty packets: " + Environment.NewLine + string.Join(Environment.NewLine, belowHits));
            Assert.True(aboveHits.Length == 1,
                $"packet {AboveBoundPacket} is at or above SP-073 and must be rejected for the same scope row: "
                + Environment.NewLine + string.Join(Environment.NewLine, aboveHits));
            Assert.Contains("does not cover", aboveHits[0], StringComparison.Ordinal);
        }
        finally
        {
            DeleteTree(SyntheticRootOf(spine));
        }
    }

    [Fact]
    public async Task TheProjection_ExitsZeroOnAViolatingCorpus_AndFailsClosedOnAbnormalTermination()
    {
        // The exit-code contract, because the consumer's correctness rests on it in both
        // directions. If a violating corpus exited non-zero, the C# side would either have to bind
        // the validator's whole rule set (including check 8's ID-reuse rule, which fires for every
        // packet already on disk) or ignore exit codes and go blind. And if a FAILED projection
        // read as an empty corpus, every guard consuming it would be vacuously green.
        var spine = BuildSyntheticSpineTasks((SiblingPacket, CoversNothingCell));
        var junkRoot = string.Empty;
        try
        {
            var run = await WaveScopeOracle.RunAsync(WaveScopeOracle.ValidatorPath(), "--emit-packet-scopes", spine);
            var projection = await WaveScopeOracle.LoadAsync(WaveScopeOracle.ValidatorPath(), spine);

            Assert.Equal(0, run.ExitCode);
            Assert.NotEmpty(run.StdOut);
            Assert.NotEmpty(projection.Packets.Single().Violations);

            var absent = Path.Combine(Path.GetTempPath(), $"ccp-sp136-absent-{Guid.NewGuid():N}", "spine-tasks");
            var refused = await WaveScopeOracle.RunAsync(WaveScopeOracle.ValidatorPath(), "--emit-packet-scopes", absent);
            Assert.Equal(2, refused.ExitCode);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => WaveScopeOracle.LoadAsync(WaveScopeOracle.ValidatorPath(), absent));

            var junk = WriteStandInValidator("process.stdout.write(\"this is not json\\n\");");
            junkRoot = Path.GetDirectoryName(junk)!;
            await Assert.ThrowsAsync<InvalidOperationException>(() => WaveScopeOracle.LoadAsync(junk, spine));

            var wrongShape = WriteStandInValidator("process.stdout.write(JSON.stringify({ schema: \"something-else\" }));");
            await Assert.ThrowsAsync<InvalidOperationException>(() => WaveScopeOracle.LoadAsync(wrongShape, spine));

            var silent = WriteStandInValidator("process.exitCode = 0;");
            await Assert.ThrowsAsync<InvalidOperationException>(() => WaveScopeOracle.LoadAsync(silent, spine));
        }
        finally
        {
            DeleteTree(SyntheticRootOf(spine));
            DeleteTree(junkRoot);
        }
    }

    // -----------------------------------------------------------------------------------------
    // Helpers. Filesystem probes live here and never in a [Fact] body: the vacuous-shape detector
    // is lexical, and client/tests/floor/vacuous-shape-ledger.json is a shared chokepoint this
    // packet may not edit, so these facts must carry no silencing shape at all.
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// The predicate <c>FloorWrapperGuardTests.cs:224</c> carried before SP-136, kept alive here
    /// as the drifted implementation this convergence exists to detect.
    /// </summary>
    private static bool DriftedLiteralCoverage(string scopeCellText) =>
        scopeCellText.Replace('\\', '/').Contains("client/tests/floor/floor.json", StringComparison.OrdinalIgnoreCase);

    private static string RepoSpineTasks() => Path.Combine(FloorWrapperGuardTests.FindRepoRoot(), "spine-tasks");

    private static string ReadRepoText(params string[] parts) =>
        File.ReadAllText(Path.Combine([FloorWrapperGuardTests.FindRepoRoot(), .. parts]));

    /// <summary>
    /// A synthetic packet corpus under a TEMP root. Never under the repository's own
    /// <c>spine-tasks/</c>: a fixture packet written there would be enumerated by the live guards
    /// and would red the corpus-wide facts at the same moment it was meant to prove something. The
    /// leaf directory is named <c>spine-tasks</c> because that is the anchor both packet walks use.
    /// </summary>
    private static string BuildSyntheticSpineTasks(params (string Dir, string ScopeCell)[] packets)
    {
        var root = Path.Combine(Path.GetTempPath(), $"ccp-sp136-{Guid.NewGuid():N}");
        var spine = Path.Combine(root, "spine-tasks");
        Directory.CreateDirectory(spine);

        foreach (var (dir, scopeCell) in packets)
        {
            var packetDir = Path.Combine(spine, dir);
            Directory.CreateDirectory(packetDir);
            File.WriteAllText(Path.Combine(packetDir, "PROMPT.md"), SyntheticPrompt(dir, scopeCell));
        }

        return spine;
    }

    /// <summary>
    /// A minimal but COMPLETE packet contract, so the only thing that varies between fixture
    /// packets is the scope cell under test. Anything else would let an unrelated violation stand
    /// in for the one the fact is watching.
    /// </summary>
    private static string SyntheticPrompt(string dir, string scopeCell) =>
        $"""
         # {dir} — SP-136 fixture packet

         | Field | Value |
         |---|---|
         | testCommand | `node client/tests/floor/check-floor.mjs` |
         | floorDelta | `spine-tasks/{dir}/floor-delta.json` |
         | fileScopeMustChange | `client/src/CcpClient.Desktop/Fixture{dir}.cs` |
         | fileScopeMustNotChange | {scopeCell} |
         """;

    private sealed record MutatedValidator(string Path, int Replacements);

    /// <summary>
    /// A copy of <c>validate-wave.mjs</c> with one textual substitution, written to a temp
    /// directory. The copy imports only node builtins and <c>--emit-packet-scopes</c> takes an
    /// explicit directory, so it runs correctly from outside the repository — which is what makes
    /// the one-sided-update demonstration possible without writing into the tree.
    /// </summary>
    private static MutatedValidator MutateValidator(string find, string replaceWith)
    {
        var source = File.ReadAllText(WaveScopeOracle.ValidatorPath());
        var replacements = CountOccurrences(source, find);
        var mutatedRoot = Path.Combine(Path.GetTempPath(), $"ccp-sp136-mutant-{Guid.NewGuid():N}");
        Directory.CreateDirectory(mutatedRoot);
        var mutatedPath = Path.Combine(mutatedRoot, "validate-wave.mutated.mjs");
        File.WriteAllText(mutatedPath, source.Replace(find, replaceWith, StringComparison.Ordinal));
        return new MutatedValidator(mutatedPath, replacements);
    }

    /// <summary>A stand-in "validator" that emits whatever the caller wants, for the fail-closed fact.</summary>
    private static string WriteStandInValidator(string body)
    {
        var root = Path.Combine(Path.GetTempPath(), $"ccp-sp136-standin-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var scriptPath = Path.Combine(root, "stand-in.mjs");
        File.WriteAllText(scriptPath, body + Environment.NewLine);
        return scriptPath;
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var at = haystack.IndexOf(needle, StringComparison.Ordinal);
        while (at >= 0)
        {
            count++;
            at = haystack.IndexOf(needle, at + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }

    private static string SyntheticRootOf(string spineDir) => Path.GetDirectoryName(spineDir)!;

    private static void DeleteTree(string root)
    {
        if (root.Length == 0)
        {
            return;
        }

        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Never created, or already gone. Cleanup is housekeeping and must never decide a verdict.
        }
        catch (IOException)
        {
            // A transient handle on Windows. The temp root is disposable; masking the fact's real
            // outcome with a cleanup failure would be strictly worse.
        }
        catch (UnauthorizedAccessException)
        {
            // Same disposition as IOException above.
        }
    }
}
