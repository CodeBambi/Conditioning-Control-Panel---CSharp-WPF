using System.Text;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-120 — the pin under <c>client/docs/haptic-limb-census.md</c>.
///
/// <para><b>Why this exists.</b> The enumeration of upstream haptic command sites has been corrected
/// three times (8 → 13 → 14 → 18) and every correction came from WIDENING THE SEARCH, never from
/// re-reading the same lines. A guard that only checked the document against itself would have passed
/// on all four numbers. So the document is the DATA and this file is the LOGIC
/// (<c>UpstreamPayloadInventoryTests</c>'s rule): the needle set and the file universe live HERE, and
/// the candidate line set is RE-DERIVED from the shipping bytes on every run. Editing the census
/// document can never shrink the search.</para>
///
/// <para><b>Non-vacuous design.</b> The repo root is anchored on <c>client/CcpClient.sln</c> (present
/// in every checkout and in a worktree); root-not-found and a missing census document are hard
/// FAILURES, never skips. "Unreachable reference tree" means exactly one thing: the repo has no
/// <c>ConditioningControlPanel/</c> directory at all. A HALF-present reference — the directory exists
/// but a family directory does not — is a corrupt checkout and FAILS, and the branch taken is written
/// to the test output so a permanently-unreachable guard is visible in the TRX. The unreachable
/// branch still enforces the document's whole shape, so a gutted census cannot pass either branch.
/// The fixture facts at the bottom pin the parser and the comparer against temp-dir repositories, so
/// none of this rests on today's tree happening to agree.</para>
/// </summary>
public sealed class HapticSiteCensusTests
{
    private const string CensusRelativePath = "client/docs/haptic-limb-census.md";

    /// <summary>Command sites in the family. The number this packet re-derived; a change to it is a
    /// change to this file, which is the review friction that stops a fourth silent drift.</summary>
    private const int ExpectedCommandSites = 18;

    /// <summary>Every candidate line the needle set finds in the family. 18 commands + 62 accounted.</summary>
    private const int ExpectedCandidateLines = 80;

    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];
    private static readonly string[] CensusParts = ["client", "docs", "haptic-limb-census.md"];
    private static readonly string[] WpfRootParts = ["ConditioningControlPanel"];
    private static readonly string[] PortRootParts = ["client", "src", "CcpClient.Desktop"];

    /// <summary>The family: three directories, searched whole and recursively. NOT a file list —
    /// a file list is exactly how <c>VideoService.Browser.cs</c> and <c>BouncingTextService.cs</c>
    /// were missed twice.</summary>
    private static readonly string[] FamilyDirectories =
    [
        "Services/Flash",
        "Services/Video",
        "Services/Subliminal",
    ];

    /// <summary>Needles that cannot mean anything but haptics, matched case-insensitively. The
    /// over-match needles (<c>toy</c>, <c>pulse</c>) are deliberately absent: they found no site and
    /// pinning them would red this guard on unrelated animation edits.</summary>
    private static readonly string[] Needles =
    [
        "haptic", "vibe", "vibrat", "buzz", "lovense", "buttplug", "funscript",
        "setlayer", "hapticlayer", "toyinput",
    ];

    private static readonly string[] Verdicts =
        ["present", "collapsed", "absent-by-decision", "absent-unexplained"];

    private static readonly string[] Buckets =
        ["adjacent", "inbound", "settings", "noise"];

    private readonly ITestOutputHelper _output;

    public HapticSiteCensusTests(ITestOutputHelper output) => _output = output;

    // ==================================================================
    // The guard, against the real repository this test runs from.
    // ==================================================================

    [Fact]
    public void EveryHapticLineInTheFamilyIsAccountedFor_RederivedFromTheShippingBytesAndNotFromTheDocument()
    {
        var report = RunAgainstRealRepo();
        _output.WriteLine(report.Describe());

        Assert.Empty(report.UnaccountedLines);
        Assert.Empty(report.PhantomRows);
        Assert.Equal(ExpectedCommandSites, report.CommandRows);
        Assert.Equal(ExpectedCandidateLines, report.AccountedRows + report.CommandRows);
    }

    [Fact]
    public void EveryCitedUpstreamLineStillCarriesItsRecordedNeedle()
    {
        var report = RunAgainstRealRepo();
        _output.WriteLine(report.Describe());

        Assert.Empty(report.RottedUpstreamCitations);
        Assert.Equal(ExpectedCandidateLines, report.AccountedRows + report.CommandRows);
    }

    [Fact]
    public void EveryCitedPortLineStillCarriesItsRecordedNeedle_SoATriggerPointCannotRotIntoAnotherStatement()
    {
        var report = RunAgainstRealRepo();
        _output.WriteLine(report.Describe());

        Assert.Empty(report.RottedPortCitations);
        Assert.Equal(5, report.PortTriggerPoints);
    }

    [Fact]
    public void TheVerdictVocabularyIsClosed_AndEveryMappedSiteCitesBothSides()
    {
        var report = RunAgainstRealRepo();
        _output.WriteLine(report.Describe());

        Assert.Empty(report.VocabularyViolations);
        Assert.Equal(0, report.UnexplainedAbsences);
    }

    [Fact]
    public void EveryAbsenceQuotesADecisionAndTheQuoteIsReallyAtThatPortLine()
    {
        var report = RunAgainstRealRepo();
        _output.WriteLine(report.Describe());

        Assert.Empty(report.DecisionViolations);
        Assert.True(report.DecisionRows >= report.AbsentSites,
            $"{report.AbsentSites} absences and only {report.DecisionRows} decision rows");
    }

    /// <summary>
    /// Confirm-or-refute, arithmetically, the discrepancy the census records in §8:
    /// <c>Services/Haptics/HapticService.cs:817</c> says the flash decay ladder "decays over ~2s" and
    /// the arithmetic at <c>:838-843</c> does not agree with its own comment. Eight rungs at 450 ms
    /// spacing put the last rung's ATTACK at 3150 ms before its envelope has even begun.
    /// <b>Code wins</b>, and a port copies 3503 ms rather than the sentence.
    /// </summary>
    [Fact]
    public void TheDecayLadderNeverSpansTwoSeconds_InAnyMode_SoTheSourcesOwnCommentIsRefuted()
    {
        var spans = Enum.GetValues<LadderMode>().ToDictionary(m => m, RenderedLadderSpanMs);
        _output.WriteLine(string.Join("; ", spans.Select(kv => $"{kv.Key}={kv.Value}ms")));

        Assert.Equal(3503, spans[LadderMode.Constant]);
        Assert.Equal(3308, spans.Values.Min());
        Assert.All(spans, kv => Assert.True(kv.Value > 2000,
            $"{kv.Key} spans {kv.Value} ms, which would make the '~2s' comment defensible"));
    }

    [Fact]
    public void TheLadderConstantsAreStillAtTheLinesTheCensusCites()
    {
        var report = RunAgainstRealRepo();
        _output.WriteLine(report.Describe());

        Assert.Empty(report.LadderConstantViolations);
        Assert.Equal(ExpectedCommandSites, report.CommandRows);
    }

    // ==================================================================
    // Fixture facts: the machinery, pinned against temp-dir repositories
    // so none of the above rests on today's tree agreeing.
    // ==================================================================

    [Fact]
    public void AnUnaccountedHapticLineFailsTheGuard()
    {
        using var repo = new TempRepo();
        SeedConsistentRepo(repo);
        repo.Append("ConditioningControlPanel/Services/Video/VideoService.cs",
            "        _ = App.Haptics?.StartVideoBackgroundVibeAsync();");

        var report = Run(repo.Root);
        _output.WriteLine(report.Describe());

        Assert.NotEmpty(report.UnaccountedLines);
        Assert.Contains("VideoService.cs", string.Join(" ", report.UnaccountedLines), StringComparison.Ordinal);
    }

    [Fact]
    public void ARowWhoseCitedLineNoLongerCarriesItsNeedleFailsTheGuard()
    {
        using var repo = new TempRepo();
        SeedConsistentRepo(repo);
        repo.Replace("ConditioningControlPanel/Services/Flash/FlashService.cs", 2,
            "        _ = App.Haptics?.SomethingElseEntirely();");

        var report = Run(repo.Root);
        _output.WriteLine(report.Describe());

        Assert.NotEmpty(report.RottedUpstreamCitations);
    }

    [Fact]
    public void AHalfPresentReferenceTreeIsAFailure_NotTheUnreachableBranch()
    {
        using var repo = new TempRepo();
        SeedConsistentRepo(repo);
        repo.RemoveDirectory("ConditioningControlPanel/Services/Subliminal");

        var report = Run(repo.Root);
        _output.WriteLine(report.Describe());

        Assert.Equal(GuardBranch.Reachable, report.Branch);
        Assert.NotEmpty(report.StructureViolations);
    }

    [Fact]
    public void TheUnreachableBranchStillEnforcesTheDocumentsShape()
    {
        using var intact = new TempRepo();
        SeedConsistentRepo(intact);
        intact.RemoveDirectory("ConditioningControlPanel");
        var clean = Run(intact.Root);

        using var gutted = new TempRepo();
        SeedConsistentRepo(gutted);
        gutted.RemoveDirectory("ConditioningControlPanel");
        gutted.Replace("client/docs/haptic-limb-census.md", FixtureVerdictLineNumber, FixtureRowWithBadVerdict);
        var broken = Run(gutted.Root);

        _output.WriteLine($"unreachable+clean: {clean.Describe()}");
        _output.WriteLine($"unreachable+gutted: {broken.Describe()}");

        Assert.Equal(GuardBranch.Unreachable, clean.Branch);
        Assert.Empty(clean.VocabularyViolations);
        Assert.Equal(GuardBranch.Unreachable, broken.Branch);
        Assert.NotEmpty(broken.VocabularyViolations);
    }

    [Fact]
    public void AnAbsenceWithoutAQuotedDecisionFailsTheGuard()
    {
        using var repo = new TempRepo();
        SeedConsistentRepo(repo, includeDecisionRow: false);

        var report = Run(repo.Root);
        _output.WriteLine(report.Describe());

        Assert.NotEmpty(report.DecisionViolations);
    }

    [Fact]
    public void ADecisionQuoteThatIsNotAtTheCitedPortLineFailsTheGuard()
    {
        using var repo = new TempRepo();
        SeedConsistentRepo(repo);
        repo.Replace("client/src/CcpClient.Desktop/Effects/FlashSurfacePresenter.cs", 4,
            "        // a comment that no longer says what the census quotes");

        var report = Run(repo.Root);
        _output.WriteLine(report.Describe());

        Assert.NotEmpty(report.DecisionViolations);
    }

    // ==================================================================
    // Machinery.
    // ==================================================================

    private enum GuardBranch
    {
        Reachable,
        Unreachable,
    }

    private sealed record SiteRow(
        string Number, string Upstream, string Needle, string Verdict,
        string PortTrigger, string PortNeedle);

    private sealed record AccountedRow(string Upstream, string Needle, string Bucket);

    private sealed record DecisionRow(string Site, string Decision, string Quote);

    private sealed record Report(
        GuardBranch Branch,
        int CommandRows,
        int AccountedRows,
        int DecisionRows,
        int AbsentSites,
        int UnexplainedAbsences,
        int PortTriggerPoints,
        IReadOnlyList<string> UnaccountedLines,
        IReadOnlyList<string> PhantomRows,
        IReadOnlyList<string> RottedUpstreamCitations,
        IReadOnlyList<string> RottedPortCitations,
        IReadOnlyList<string> VocabularyViolations,
        IReadOnlyList<string> DecisionViolations,
        IReadOnlyList<string> StructureViolations,
        IReadOnlyList<string> LadderConstantViolations)
    {
        public string Describe()
        {
            var sb = new StringBuilder();
            sb.Append($"branch={Branch} commands={CommandRows} accounted={AccountedRows} ");
            sb.Append($"decisions={DecisionRows} absences={AbsentSites} triggers={PortTriggerPoints}");
            Append(sb, "unaccounted", UnaccountedLines);
            Append(sb, "phantom", PhantomRows);
            Append(sb, "rotted-upstream", RottedUpstreamCitations);
            Append(sb, "rotted-port", RottedPortCitations);
            Append(sb, "vocabulary", VocabularyViolations);
            Append(sb, "decision", DecisionViolations);
            Append(sb, "structure", StructureViolations);
            Append(sb, "ladder-constants", LadderConstantViolations);
            return sb.ToString();
        }

        private static void Append(StringBuilder sb, string label, IReadOnlyList<string> items)
        {
            if (items.Count == 0)
            {
                return;
            }

            sb.Append($"{Environment.NewLine}  {label} ({items.Count}): {string.Join(" | ", items)}");
        }
    }

    private Report RunAgainstRealRepo() => Run(FindRepoRoot());

    private static Report Run(string root)
    {
        var censusPath = Path.Combine([root, .. CensusParts]);
        if (!File.Exists(censusPath))
        {
            throw new InvalidOperationException(
                $"{CensusRelativePath} not found at {censusPath} — this guard refuses to skip");
        }

        var text = File.ReadAllText(censusPath);
        var sites = ParseSites(text);
        var accounted = ParseAccounted(text);
        var decisions = ParseDecisions(text);

        var structure = new List<string>();
        if (sites.Count == 0) structure.Add("the sites table is empty or unparseable");
        if (accounted.Count == 0) structure.Add("the accounted table is empty or unparseable");
        if (decisions.Count == 0) structure.Add("the decisions table is empty or unparseable");

        var vocabulary = new List<string>();
        foreach (var site in sites)
        {
            if (!Verdicts.Contains(site.Verdict, StringComparer.Ordinal))
            {
                vocabulary.Add($"site {site.Number}: verdict '{site.Verdict}' is not one of {string.Join("/", Verdicts)}");
            }

            var mapped = site.Verdict is "present" or "collapsed";
            var cited = site.PortTrigger.Contains(".cs:", StringComparison.Ordinal);
            if (mapped && !cited)
            {
                vocabulary.Add($"site {site.Number}: '{site.Verdict}' with no port citation");
            }

            if (!mapped && cited)
            {
                vocabulary.Add($"site {site.Number}: '{site.Verdict}' but a port trigger is cited");
            }

            if (!site.Upstream.Contains(".cs:", StringComparison.Ordinal))
            {
                vocabulary.Add($"site {site.Number}: no upstream citation");
            }
        }

        foreach (var row in accounted)
        {
            if (!Buckets.Contains(row.Bucket, StringComparer.Ordinal))
            {
                vocabulary.Add($"{row.Upstream}: bucket '{row.Bucket}' is not one of {string.Join("/", Buckets)}");
            }
        }

        var absent = sites.Where(s => s.Verdict == "absent-by-decision").ToList();
        var unexplained = sites.Count(s => s.Verdict == "absent-unexplained");

        var decisionViolations = new List<string>();
        foreach (var site in absent)
        {
            if (!decisions.Any(d => d.Site == site.Number))
            {
                decisionViolations.Add($"site {site.Number} is absent-by-decision and no decision is quoted for it");
            }
        }

        foreach (var decision in decisions)
        {
            if (!sites.Any(s => s.Number == decision.Site && s.Verdict == "absent-by-decision"))
            {
                decisionViolations.Add($"decision row cites site {decision.Site}, which is not an absent-by-decision site");
                continue;
            }

            if (decision.Quote.Length < 8)
            {
                decisionViolations.Add($"site {decision.Site}: the quote '{decision.Quote}' is too short to be a decision's own words");
                continue;
            }

            var probe = ProbePortCitation(root, decision.Decision, decision.Quote);
            if (probe is not null)
            {
                decisionViolations.Add($"site {decision.Site}: {probe}");
            }
        }

        var rottedPort = new List<string>();
        foreach (var site in sites.Where(s => s.PortTrigger.Contains(".cs:", StringComparison.Ordinal)))
        {
            var probe = ProbePortCitation(root, site.PortTrigger, site.PortNeedle);
            if (probe is not null)
            {
                rottedPort.Add($"site {site.Number}: {probe}");
            }
        }

        var wpfRoot = Path.Combine([root, .. WpfRootParts]);
        if (!Directory.Exists(wpfRoot))
        {
            return new Report(
                GuardBranch.Unreachable, sites.Count, accounted.Count, decisions.Count,
                absent.Count, unexplained,
                sites.Select(s => s.PortTrigger).Where(t => t.Contains(".cs:", StringComparison.Ordinal))
                     .Distinct(StringComparer.Ordinal).Count(),
                [], [], [], rottedPort, vocabulary, decisionViolations, structure, []);
        }

        foreach (var directory in FamilyDirectories)
        {
            var full = Path.Combine(wpfRoot, directory.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(full))
            {
                structure.Add(
                    $"{directory} is missing from a PRESENT ConditioningControlPanel/ — a half-present " +
                    "reference tree is a corrupt checkout, not an unreachable one");
            }
        }

        var candidates = structure.Count == 0
            ? CandidateLines(wpfRoot)
            : new Dictionary<string, string>(StringComparer.Ordinal);

        var recorded = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var site in sites)
        {
            recorded[site.Upstream] = site.Needle;
        }

        foreach (var row in accounted)
        {
            recorded[row.Upstream] = row.Needle;
        }

        var unaccounted = candidates.Keys
            .Where(k => !recorded.ContainsKey(k))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        var phantom = recorded.Keys
            .Where(k => !candidates.ContainsKey(k))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        var rottedUpstream = new List<string>();
        foreach (var (citation, needle) in recorded.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (!candidates.TryGetValue(citation, out var line))
            {
                continue; // already reported as phantom
            }

            if (line.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0)
            {
                rottedUpstream.Add($"{citation} no longer carries '{needle}'");
            }
        }

        return new Report(
            GuardBranch.Reachable, sites.Count, accounted.Count, decisions.Count,
            absent.Count, unexplained,
            sites.Select(s => s.PortTrigger).Where(t => t.Contains(".cs:", StringComparison.Ordinal))
                 .Distinct(StringComparer.Ordinal).Count(),
            unaccounted, phantom, rottedUpstream, rottedPort, vocabulary, decisionViolations, structure,
            LadderConstants(wpfRoot));
    }

    /// <summary>Null when the citation resolves and the needle is on that line; a sentence otherwise.</summary>
    private static string? ProbePortCitation(string root, string citation, string needle)
    {
        var (relative, number) = SplitCitation(citation);
        if (relative is null || number <= 0)
        {
            return $"'{citation}' is not a File.cs:line citation";
        }

        var full = Path.Combine([root, .. PortRootParts, .. relative.Split('/')]);
        if (!File.Exists(full))
        {
            return $"'{citation}' names a file that does not exist";
        }

        var lines = File.ReadAllLines(full);
        if (number > lines.Length)
        {
            return $"'{citation}' is past the end of a {lines.Length}-line file";
        }

        return lines[number - 1].Contains(needle, StringComparison.Ordinal)
            ? null
            : $"'{citation}' no longer carries \"{needle}\"";
    }

    private static (string? Relative, int Line) SplitCitation(string citation)
    {
        var at = citation.LastIndexOf(':');
        if (at <= 0 || !int.TryParse(citation[(at + 1)..], out var number))
        {
            return (null, 0);
        }

        return (citation[..at], number);
    }

    /// <summary>Every line in the family that any needle matches, keyed <c>relative/path.cs:line</c>.</summary>
    private static Dictionary<string, string> CandidateLines(string wpfRoot)
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var directory in FamilyDirectories)
        {
            var full = Path.Combine(wpfRoot, directory.Replace('/', Path.DirectorySeparatorChar));
            foreach (var file in Directory.EnumerateFiles(full, "*.cs", SearchOption.AllDirectories)
                                          .OrderBy(f => f, StringComparer.Ordinal))
            {
                var normalized = file.Replace('\\', '/');
                var relative = normalized[(normalized.IndexOf("/Services/", StringComparison.Ordinal) + 1)..];
                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    if (Needles.Any(n => lines[i].Contains(n, StringComparison.OrdinalIgnoreCase)))
                    {
                        found[$"{relative}:{i + 1}"] = lines[i];
                    }
                }
            }
        }

        return found;
    }

    /// <summary>The ladder's own numbers, still where §8 says they are.</summary>
    private static IReadOnlyList<string> LadderConstants(string wpfRoot)
    {
        var violations = new List<string>();
        var service = Path.Combine(wpfRoot, "Services", "Haptics", "HapticService.cs");
        var patterns = Path.Combine(wpfRoot, "Services", "Haptics", "Core", "HapticPatterns.cs");
        Require(service, 817, "~2s", violations);
        Require(service, 838, "i < 8", violations);
        Require(service, 840, "Math.Pow(0.7, i)", violations);
        Require(service, 842, "250", violations);
        Require(service, 843, "i * 450", violations);
        Require(patterns, 36, "MinFeltOnMs = 130", violations);
        Require(patterns, 40, "MinFeltDurationMs = 200", violations);
        // The ENVELOPE arithmetic, not only the loop's constants. RenderedRungMs below reproduces
        // Render's Constant arm, and D205's 3503 ms is that reproduction; without these two the
        // arm could change and leave the number wrong with the fact still green.
        Require(patterns, 126, "Math.Clamp(durationMs / 6, 10, 60)", violations);
        Require(patterns, 127, "Math.Clamp(durationMs / 4, 30, 150)", violations);
        return violations;
    }

    private static void Require(string path, int line, string needle, List<string> into)
    {
        if (!File.Exists(path))
        {
            into.Add($"{Path.GetFileName(path)} is missing");
            return;
        }

        var lines = File.ReadAllLines(path);
        if (line > lines.Length || !lines[line - 1].Contains(needle, StringComparison.Ordinal))
        {
            into.Add($"{Path.GetFileName(path)}:{line} no longer carries \"{needle}\"");
        }
    }

    // ---- the ladder's arithmetic, reproduced from the constants above ----

    private enum LadderMode
    {
        Constant,
        Pulse,
        Wave,
        Heartbeat,
        Escalate,
        Earthquake,
    }

    /// <summary>
    /// <c>PlayDecayLadder</c> appends eight rendered patterns at <c>i * 450</c> ms
    /// (<c>HapticService.cs:838-843</c>); the span is the last rung's offset plus its own rendered
    /// length, which is what <c>HapticPatterns.TotalMs</c> computes (<c>:137-147</c>).
    /// </summary>
    private static int RenderedLadderSpanMs(LadderMode mode) => (7 * 450) + RenderedRungMs(mode, 250);

    private static int RenderedRungMs(LadderMode mode, int requested)
    {
        var duration = Math.Clamp(requested, 200, 60_000); // MinFeltDurationMs floor
        const int onFloor = 130;                            // MinFeltOnMs
        return mode switch
        {
            // period = MinFeltOnMs + 70; one tap at 250 ms; attack 8, hold 130, decay 20
            LadderMode.Pulse => ((Math.Clamp(duration / (onFloor + 70), 1, 40) - 1) * (onFloor + 70)) + 8 + onFloor + 20,
            // one swell: half up, half down, each floored at the on-time
            LadderMode.Wave => 2 * Math.Max(onFloor, duration / 2),
            // beats 400 ms apart, the second offset by 140; attack 10, hold floor, decay 30
            LadderMode.Heartbeat => ((Math.Clamp(duration / 400, 1, 20) - 1) * 400) + 140 + 10 + onFloor + 30,
            // n rungs of slice ms; attack 10, hold slice, decay 20
            LadderMode.Escalate => Escalate(duration, onFloor),
            // jolts every MinFeltOnMs+40; attack 5, hold floor, decay 25
            LadderMode.Earthquake => ((Math.Clamp(duration / (onFloor + 40), 2, 60) - 1) * (onFloor + 40)) + 5 + onFloor + 25,
            // attack = duration/6 clamped 10..60, hold = duration, decay = duration/4 clamped 30..150
            _ => Math.Clamp(duration / 6, 10, 60) + duration + Math.Clamp(duration / 4, 30, 150),
        };
    }

    private static int Escalate(int duration, int onFloor)
    {
        var n = Math.Clamp(duration / onFloor, 1, 8);
        var slice = Math.Max(onFloor, duration / n);
        return ((n - 1) * slice) + 10 + slice + 20;
    }

    // ---- markdown parsing ----

    private static IReadOnlyList<SiteRow> ParseSites(string text) =>
        Rows(text, "census:sites")
            .Where(c => c.Length >= 6)
            .Select(c => new SiteRow(c[0], c[1], c[2], c[3], c[4], c[5]))
            .ToList();

    private static IReadOnlyList<AccountedRow> ParseAccounted(string text) =>
        Rows(text, "census:accounted")
            .Where(c => c.Length >= 3)
            .Select(c => new AccountedRow(c[0], c[1], c[2]))
            .ToList();

    private static IReadOnlyList<DecisionRow> ParseDecisions(string text) =>
        Rows(text, "census:decisions")
            .Where(c => c.Length >= 3)
            .Select(c => new DecisionRow(c[0], c[1], c[2]))
            .ToList();

    private static List<string[]> Rows(string text, string marker)
    {
        var rows = new List<string[]>();
        var opening = $"<!-- {marker} -->";
        var open = text.IndexOf(opening, StringComparison.Ordinal);
        var close = text.IndexOf($"<!-- /{marker} -->", StringComparison.Ordinal);
        if (open < 0 || close <= open)
        {
            return rows;
        }

        var body = text[(open + opening.Length)..close];
        foreach (var raw in body.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith('|') || line.StartsWith("|---", StringComparison.Ordinal))
            {
                continue;
            }

            var cells = line.Trim('|').Split('|').Select(c => c.Trim().Trim('`').Trim()).ToArray();
            if (cells.Length > 0 && (cells[0] == "#" || cells[0] == "upstream" || cells[0] == "site"))
            {
                continue; // header
            }

            rows.Add(cells);
        }

        return rows;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine([dir.FullName, .. RepoAnchorParts])))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"repo root (the directory holding {string.Join('/', RepoAnchorParts)}) not found above " +
            $"{AppContext.BaseDirectory} — this guard fails rather than skips");
    }

    // ---- temp-dir fixtures ----

    private const int FixtureVerdictLineNumber = 6;

    private const string FixtureRowWithBadVerdict =
        "| 1 | `Services/Flash/FlashService.cs:2` | `FlashDecayVibeAsync` | probably-fine | " +
        "`Effects/FlashSurfacePresenter.cs:2` | `Place(` | fail | a flash appears |";

    private sealed class TempRepo : IDisposable
    {
        public TempRepo()
        {
            Root = Path.Combine(Path.GetTempPath(), "ccp-sp120-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Write(string relative, string content)
        {
            var full = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }

        public void Append(string relative, string line)
        {
            var full = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
            File.AppendAllText(full, line + Environment.NewLine);
        }

        public void Replace(string relative, int line, string replacement)
        {
            var full = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
            var lines = File.ReadAllLines(full);
            lines[line - 1] = replacement;
            File.WriteAllLines(full, lines);
        }

        public void RemoveDirectory(string relative)
        {
            var full = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.Delete(full, recursive: true);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // a temp directory that outlives the run is not a test failure
            }
        }
    }

    /// <summary>
    /// The smallest repository this guard can pass over: two command sites (one mapped, one absent),
    /// one accounted line, one decision, and a family whose three directories all exist.
    /// </summary>
    private static void SeedConsistentRepo(TempRepo repo, bool includeDecisionRow = true)
    {
        repo.Write("client/CcpClient.sln", "fixture");
        repo.Write("ConditioningControlPanel/Services/Flash/FlashService.cs", string.Join(Environment.NewLine, new string[]
        {
            "// prose about haptic timing",
            "        _ = App.Haptics?.FlashDecayVibeAsync();",
            "        _ = App.Haptics?.FlashClickVibeAsync();",
        }));
        repo.Write("ConditioningControlPanel/Services/Video/VideoService.cs",
            "// nothing of interest here" + Environment.NewLine);
        repo.Write("ConditioningControlPanel/Services/Subliminal/SubliminalService.cs",
            "// nor here" + Environment.NewLine);
        repo.Write("ConditioningControlPanel/Services/Haptics/HapticService.cs", string.Join(Environment.NewLine,
            Enumerable.Range(1, 845).Select(LadderFixtureLine)));
        repo.Write("ConditioningControlPanel/Services/Haptics/Core/HapticPatterns.cs", string.Join(Environment.NewLine,
            Enumerable.Range(1, 130).Select(PatternsFixtureLine)));
        repo.Write("client/src/CcpClient.Desktop/Effects/FlashSurfacePresenter.cs", string.Join(Environment.NewLine, new string[]
        {
            "class FlashSurfacePresenter",
            "    _surfaces.Place(slot, request, frame, draw.Lifetime);",
            "    // filler",
            "    // serve pop / hydra / XP mechanics this port does not have",
        }));

        var decisions = includeDecisionRow
            ? "| 2 | `Effects/FlashSurfacePresenter.cs:4` | `serve pop / hydra / XP mechanics this port does not have` |"
            : "| 2 | `Effects/FlashSurfacePresenter.cs:4` | `` |";

        repo.Write("client/docs/haptic-limb-census.md", string.Join(Environment.NewLine, new string[]
        {
            "# fixture census",
            "",
            "<!-- census:sites -->",
            "| # | upstream | needle | verdict | port trigger | port needle | T3 | notes |",
            "|---|---|---|---|---|---|---|---|",
            "| 1 | `Services/Flash/FlashService.cs:2` | `FlashDecayVibeAsync` | present | " +
                "`Effects/FlashSurfacePresenter.cs:2` | `_surfaces.Place(` | fail | a flash appears |",
            "| 2 | `Services/Flash/FlashService.cs:3` | `FlashClickVibeAsync` | absent-by-decision | " +
                "— | — | — | a flash is popped |",
            "<!-- /census:sites -->",
            "",
            "<!-- census:accounted -->",
            "| upstream | needle | bucket | why |",
            "|---|---|---|---|",
            "| `Services/Flash/FlashService.cs:1` | `haptic` | noise | prose |",
            "<!-- /census:accounted -->",
            "",
            "<!-- census:decisions -->",
            "| site | decision | quote |",
            "|---|---|---|",
            decisions,
            "<!-- /census:decisions -->",
        }));
    }

    private static string LadderFixtureLine(int number) => number switch
    {
        817 => "        /// <summary>Vibe that decays over ~2s. The slider sets the starting intensity;",
        838 => "            for (int i = 0; i < 8; i++)",
        840 => "                var intensity = Math.Max(start * Math.Pow(0.7, i), MinPerceptibleIntensity);",
        842 => "                    HapticPatterns.Render(rule.Mode, intensity, 250, priority: 1, target: rule.Target),",
        843 => "                    offsetMs: i * 450);",
        _ => "        // filler",
    };

    private static string PatternsFixtureLine(int number) => number switch
    {
        36 => "        public const int MinFeltOnMs = 130;",
        40 => "        public const int MinFeltDurationMs = 200;",
        126 => "                    var attack = Math.Clamp(durationMs / 6, 10, 60);",
        127 => "                    var decay = Math.Clamp(durationMs / 4, 30, 150);",
        _ => "        // filler",
    };
}
