using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-125 — the pin under <c>client/docs/fyp-census.md</c>.
///
/// <para><b>Why this exists.</b> The board row for For You Feed claimed <b>3 files</b> in
/// <c>Services/Fyp/</c>; a recursive walk found <b>9</b>, plus an <c>Online/</c> subdirectory that
/// eleven files outside the surface consume. The port has been wrong about an upstream count four
/// times already (haptic sites 8 → 13 → 14 → 18) and every correction came from WIDENING THE SEARCH,
/// never from re-reading the same lines. So this guard follows
/// <see cref="HapticSiteCensusTests"/>'s rule exactly rather than inventing a second convention for
/// the same job: the document is the DATA and this file is the LOGIC. The directory roots and the
/// type-name needles live HERE, and the file sets are RE-DERIVED from the shipping bytes on every
/// run. Editing the census document can never shrink the search.</para>
///
/// <para><b>Non-vacuous design.</b> The repo root is anchored on <c>client/CcpClient.sln</c> (present
/// in every checkout and in a worktree); root-not-found and a missing census document are hard
/// FAILURES, never skips. "Unreachable reference tree" means exactly one thing: the repo has no
/// <c>ConditioningControlPanel/</c> directory at all. A HALF-present reference — the directory exists
/// but the surface directory does not — is a corrupt checkout and FAILS, and the branch taken is
/// written to the test output so a permanently-unreachable guard is visible in the TRX. The
/// unreachable branch still enforces the document's whole shape, so a gutted census cannot pass
/// either branch. The fixture facts at the bottom pin the parser and the comparer against temp-dir
/// repositories, so none of this rests on today's tree happening to agree.</para>
/// </summary>
public sealed class FypCensusTests
{
    private const string CensusRelativePath = "client/docs/fyp-census.md";

    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];
    private static readonly string[] CensusParts = ["client", "docs", "fyp-census.md"];
    private static readonly string[] WpfRootParts = ["ConditioningControlPanel"];

    /// <summary>The surface: one directory, searched whole and recursively, EVERY extension. NOT a file
    /// list — a file list is exactly how the board row arrived at 3 and how the haptic census lost
    /// <c>VideoService.Browser.cs</c> twice. The walk is <c>*</c> rather than <c>*.cs</c> on purpose: the
    /// census counts this tree with <c>find -type f</c>, and a guard that counted only <c>.cs</c> would
    /// let a <c>.json</c> or <c>.resx</c> land upstream and make the prose stale while staying green.
    /// </summary>
    private static readonly string[] SurfaceDirectories = ["Services/Fyp"];

    /// <summary>The payload tree, counted the same way. The bytes stay owned by the legacy tree and
    /// are never forked into <c>client/</c>; this guard only counts them.</summary>
    private static readonly string[] PayloadDirectories = ["Resources/web/fyp"];

    /// <summary>The surface's own type names, promoted to search needles. This is the step that found
    /// <c>Chaos/ChaosFlashOverlay.cs</c> and <c>Chaos/ChaosGifCascadeOverlay.cs</c>, neither of which
    /// contains the token "fyp" anywhere — a name-shaped search cannot see them.</summary>
    private static readonly string[] SurfaceTypeNames =
    [
        "FypHostService", "FypGhostOverlay", "FypAssetManifest", "FypMetaStore",
        "FypOnlineCoordinator", "RemoteMediaCache", "RemoteMediaFormats",
        "FeedMediaKind", "IFeedSource",
    ];

    /// <summary>Extensions the consumer sweep reads. The shipping tree's UI is split across both.</summary>
    private static readonly string[] ConsumerExtensions = [".cs", ".xaml"];

    /// <summary>First-attempt port projects. Searched by the census for lessons, but never part of the
    /// shipping consumer set — <c>docs/constitution.md:32</c> bars importing their status claims.</summary>
    private const string FirstAttemptPrefix = "CCP.";

    /// <summary>The number this packet re-derived. A change to it is a change to this file, which is
    /// the review friction that stops a silent drift.</summary>
    private const int ExpectedProductFiles = 9;

    private const int ExpectedPayloadFiles = 8;

    private const int ExpectedConsumerFiles = 16;

    /// <summary>
    /// The privacy verdicts themselves, pinned. Counting the answers and checking they are non-blank
    /// leaves Q3 free to flip YES to NO and stay green — and Q3 (does it leave the machine) and Q4
    /// (which sensor, under whose consent) are the two OWNER-FLAGGED findings, which makes them the
    /// part of this census a later edit is most likely to soften. <c>StartsWith</c> for the verdicts,
    /// <c>Contains</c> for the sensor, both after markdown emphasis is stripped.
    /// </summary>
    private static readonly (string Id, bool StartsWith, string Needle)[] PrivacyVerdicts =
    [
        ("Q1", true, "YES"),
        ("Q2", true, "NO"),
        ("Q3", true, "YES"),
        ("Q4", false, "webcam"),
    ];

    /// <summary>The closed label vocabulary from the census's §3 decision rule.</summary>
    private static readonly string[] Labels = ["COVERED", "PARTIAL", "GAP", "OWNER-GATED"];

    private readonly ITestOutputHelper _output;

    public FypCensusTests(ITestOutputHelper output) => _output = output;

    // ==================================================================
    // The guard, against the real repository this test runs from.
    // ==================================================================

    [Fact]
    public void TheProductFileSetIsRederivedFromTheShippingBytes_NotReadOutOfTheCensus()
    {
        var report = RunAgainstRealRepo();
        _output.WriteLine(report.Describe());

        Assert.Empty(report.StructureProblems);
        Assert.Empty(report.ProductFilesMissingFromCensus);
        Assert.Empty(report.ProductFilesMissingFromDisk);

        if (report.Branch == GuardBranch.Reachable)
        {
            Assert.Equal(ExpectedProductFiles, report.ProductFilesOnDisk.Count);
        }

        Assert.Equal(ExpectedProductFiles, report.ProductRows.Count);
    }

    [Fact]
    public void ThePayloadCountIsRederivedFromTheShippingBytes_AndTheDispositionStaysNotForked()
    {
        var report = RunAgainstRealRepo();
        _output.WriteLine(report.Describe());

        Assert.Empty(report.StructureProblems);
        Assert.Equal(ExpectedPayloadFiles, report.CensusPayloadCount);
        Assert.Equal("not-forked", report.CensusPayloadDisposition);

        if (report.Branch == GuardBranch.Reachable)
        {
            Assert.Equal(ExpectedPayloadFiles, report.PayloadFilesOnDisk);
            Assert.Equal(report.CensusPayloadCount, report.PayloadFilesOnDisk);
        }
    }

    /// <summary>
    /// The reach of the surface, re-derived. This is the fact the board row was most wrong about:
    /// <c>Services/Fyp/Online/</c> is an app-wide remote-media subsystem, not part of the feed.
    /// </summary>
    [Fact]
    public void TheConsumerSetIsRederivedFromTheShippingBytes_SoTheSurfacesReachCannotDriftUnnoticed()
    {
        var report = RunAgainstRealRepo();
        _output.WriteLine(report.Describe());

        Assert.Empty(report.StructureProblems);
        Assert.Empty(report.ConsumersMissingFromCensus);
        Assert.Empty(report.ConsumersMissingFromDisk);
        Assert.Equal(ExpectedConsumerFiles, report.ConsumerRows.Count);

        if (report.Branch == GuardBranch.Reachable)
        {
            Assert.Equal(ExpectedConsumerFiles, report.ConsumersOnDisk.Count);
        }
    }

    [Fact]
    public void EveryBehaviourRowCarriesAClosedVocabularyLabelAndBothPlatformVerdicts()
    {
        var report = RunAgainstRealRepo();
        _output.WriteLine(report.Describe());

        Assert.Empty(report.StructureProblems);
        Assert.Empty(report.LabelViolations);
        Assert.Empty(report.PlatformViolations);
        Assert.Equal(7, report.BehaviourRows.Count);
    }

    /// <summary>
    /// The four privacy questions are answered for the WHOLE surface, not just ghost mode — the
    /// network half lives in <c>Services/Fyp/Online/</c> and a test scoped to ghost mode would walk
    /// straight past it.
    /// </summary>
    [Fact]
    public void TheFourPrivacyQuestionsAreAnsweredForTheWholeSurface()
    {
        var report = RunAgainstRealRepo();
        _output.WriteLine(report.Describe());

        Assert.Empty(report.StructureProblems);
        Assert.Equal(4, report.PrivacyAnswers.Count);
        Assert.All(report.PrivacyAnswers, a => Assert.False(string.IsNullOrWhiteSpace(a.Answer)));
    }

    /// <summary>
    /// The two OWNER-FLAGGED answers are the most valuable thing in this census and the part a later
    /// edit is most likely to soften, so the VERDICTS are pinned and not merely counted: Q3 cannot
    /// stop saying the surface talks to the network, and Q4 cannot stop naming the webcam.
    /// </summary>
    [Fact]
    public void ThePrivacyVerdictsThemselvesArePinned_NotJustTheirCount()
    {
        var report = RunAgainstRealRepo();
        _output.WriteLine(report.Describe());

        Assert.Empty(report.StructureProblems);
        Assert.Empty(report.PrivacyViolations);
    }

    [Fact]
    public void TheCensusNeverProposesForkingThePayloadBytesIntoTheClient()
    {
        var report = RunAgainstRealRepo();
        _output.WriteLine(report.Describe());

        Assert.Equal("not-forked", report.CensusPayloadDisposition);
    }

    // ==================================================================
    // Fixture facts: the machinery, pinned against temp-dir repositories
    // so none of the above rests on today's tree agreeing.
    // ==================================================================

    [Fact]
    public void AProductFileUpstreamThatTheCensusNeverHeardOfFailsTheGuard()
    {
        using var repo = new TempRepo();
        SeedConsistentRepo(repo);
        repo.Write("ConditioningControlPanel/Services/Fyp/Online/BrandNewSource.cs", "// upstream grew");

        var report = Run(repo.Root);
        _output.WriteLine(report.Describe());

        Assert.NotEmpty(report.ProductFilesMissingFromCensus);
        Assert.Contains("BrandNewSource.cs", string.Join(" ", report.ProductFilesMissingFromCensus),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ACensusRowForAProductFileThatNoLongerExistsFailsTheGuard()
    {
        using var repo = new TempRepo();
        SeedConsistentRepo(repo);
        repo.Delete("ConditioningControlPanel/Services/Fyp/Online/ScrolllerSource.cs");

        var report = Run(repo.Root);
        _output.WriteLine(report.Describe());

        Assert.NotEmpty(report.ProductFilesMissingFromDisk);
        Assert.Contains("ScrolllerSource.cs", string.Join(" ", report.ProductFilesMissingFromDisk),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The one that matters most: a NEW consumer appearing outside the surface directory. This is the
    /// exact shape of the failure the haptic census hit four times, and of the two <c>Chaos/</c> files
    /// a name-shaped search could never have found.
    /// </summary>
    [Fact]
    public void ANewConsumerOutsideTheSurfaceDirectoryFailsTheGuard()
    {
        using var repo = new TempRepo();
        SeedConsistentRepo(repo);
        repo.Write("ConditioningControlPanel/Services/Descent/DescentService.cs",
            "var c = FypOnlineCoordinator.For(\"descent\", chans, FeedMediaKind.Video);");

        var report = Run(repo.Root);
        _output.WriteLine(report.Describe());

        Assert.NotEmpty(report.ConsumersMissingFromCensus);
        Assert.Contains("DescentService.cs", string.Join(" ", report.ConsumersMissingFromCensus),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AConsumerThatStoppedUsingTheSurfaceFailsTheGuard()
    {
        using var repo = new TempRepo();
        SeedConsistentRepo(repo);
        repo.Write("ConditioningControlPanel/Services/BubbleCountService.cs", "// no longer stands down");

        var report = Run(repo.Root);
        _output.WriteLine(report.Describe());

        Assert.NotEmpty(report.ConsumersMissingFromDisk);
        Assert.Contains("BubbleCountService.cs", string.Join(" ", report.ConsumersMissingFromDisk),
            StringComparison.Ordinal);
    }

    [Fact]
    public void APayloadFileAppearingUpstreamFailsTheGuard()
    {
        using var repo = new TempRepo();
        SeedConsistentRepo(repo);
        repo.Write("ConditioningControlPanel/Resources/web/fyp/newthing.js", "// upstream grew");

        var report = Run(repo.Root);
        _output.WriteLine(report.Describe());

        Assert.NotEqual(report.CensusPayloadCount, report.PayloadFilesOnDisk);
        Assert.Equal(ExpectedPayloadFiles + 1, report.PayloadFilesOnDisk);
    }

    [Fact]
    public void AHalfPresentReferenceTreeIsAFailure_NotTheUnreachableBranch()
    {
        using var repo = new TempRepo();
        SeedConsistentRepo(repo);
        repo.RemoveDirectory("ConditioningControlPanel/Services/Fyp");

        var report = Run(repo.Root);
        _output.WriteLine(report.Describe());

        Assert.Equal(GuardBranch.Reachable, report.Branch);
        Assert.NotEmpty(report.StructureProblems);
        Assert.Contains("half-present", string.Join(" ", report.StructureProblems),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheUnreachableBranchStillEnforcesTheDocumentsShape()
    {
        using var clean = new TempRepo();
        SeedConsistentRepo(clean);
        clean.RemoveDirectory("ConditioningControlPanel");
        var cleanReport = Run(clean.Root);
        _output.WriteLine(cleanReport.Describe());

        using var gutted = new TempRepo();
        SeedConsistentRepo(gutted);
        gutted.RemoveDirectory("ConditioningControlPanel");
        gutted.Write(CensusRelativePath, "# gutted\n\nnothing here\n");
        var guttedReport = Run(gutted.Root);
        _output.WriteLine(guttedReport.Describe());

        Assert.Equal(GuardBranch.Unreachable, cleanReport.Branch);
        Assert.Empty(cleanReport.StructureProblems);

        Assert.Equal(GuardBranch.Unreachable, guttedReport.Branch);
        Assert.NotEmpty(guttedReport.StructureProblems);
    }

    [Fact]
    public void AMissingCensusDocumentFailsRatherThanSkips()
    {
        using var repo = new TempRepo();
        SeedConsistentRepo(repo);
        repo.Delete(CensusRelativePath);

        var ex = Assert.Throws<InvalidOperationException>(() => Run(repo.Root));
        Assert.Contains("refuses to skip", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>Softening the network answer is the specific edit this pin exists to catch: Q3 going
    /// from YES to NO would quietly retire the owner-flagged third-party dependency.</summary>
    [Fact]
    public void SofteningTheNetworkPrivacyAnswerFailsTheGuard()
    {
        using var repo = new TempRepo();
        SeedConsistentRepo(repo);
        repo.ReplaceInFile(CensusRelativePath,
            "| Q3 | leaves the machine? | YES | c |",
            "| Q3 | leaves the machine? | NO | c |");

        var report = Run(repo.Root);
        _output.WriteLine(report.Describe());

        Assert.NotEmpty(report.PrivacyViolations);
        Assert.Contains("Q3", string.Join(" ", report.PrivacyViolations), StringComparison.Ordinal);
    }

    [Fact]
    public void DroppingTheSensorNameFromTheConsentAnswerFailsTheGuard()
    {
        using var repo = new TempRepo();
        SeedConsistentRepo(repo);
        repo.ReplaceInFile(CensusRelativePath,
            "| Q4 | sensor? | webcam | d |",
            "| Q4 | sensor? | none worth naming | d |");

        var report = Run(repo.Root);
        _output.WriteLine(report.Describe());

        Assert.NotEmpty(report.PrivacyViolations);
        Assert.Contains("Q4", string.Join(" ", report.PrivacyViolations), StringComparison.Ordinal);
    }

    [Fact]
    public void ABehaviourRowWithoutAClosedVocabularyLabelFailsTheGuard()
    {
        using var repo = new TempRepo();
        SeedConsistentRepo(repo);
        repo.ReplaceInFile(CensusRelativePath, "| **COVERED by video/webview** |", "| probably fine |");

        var report = Run(repo.Root);
        _output.WriteLine(report.Describe());

        Assert.NotEmpty(report.LabelViolations);
    }

    [Fact]
    public void ABehaviourRowMissingAPlatformVerdictFailsTheGuard()
    {
        using var repo = new TempRepo();
        SeedConsistentRepo(repo);
        repo.ReplaceInFile(CensusRelativePath,
            "| Windows: proven-on-paper. Linux: unproven |", "| someday |");

        var report = Run(repo.Root);
        _output.WriteLine(report.Describe());

        Assert.NotEmpty(report.PlatformViolations);
    }

    // ==================================================================
    // Machinery.
    // ==================================================================

    private enum GuardBranch
    {
        Reachable,
        Unreachable,
    }

    private sealed record ProductRow(string Id, string Path);

    private sealed record ConsumerRow(string Id, string Path, string Kind);

    private sealed record BehaviourRow(string Id, string Label, string Platform);

    private sealed record Report(
        GuardBranch Branch,
        IReadOnlyList<ProductRow> ProductRows,
        IReadOnlyList<ConsumerRow> ConsumerRows,
        IReadOnlyList<BehaviourRow> BehaviourRows,
        IReadOnlyList<(string Id, string Answer)> PrivacyAnswers,
        IReadOnlyList<string> PrivacyViolations,
        int CensusPayloadCount,
        string CensusPayloadDisposition,
        IReadOnlyList<string> ProductFilesOnDisk,
        IReadOnlyList<string> ConsumersOnDisk,
        int PayloadFilesOnDisk,
        IReadOnlyList<string> ProductFilesMissingFromCensus,
        IReadOnlyList<string> ProductFilesMissingFromDisk,
        IReadOnlyList<string> ConsumersMissingFromCensus,
        IReadOnlyList<string> ConsumersMissingFromDisk,
        IReadOnlyList<string> LabelViolations,
        IReadOnlyList<string> PlatformViolations,
        IReadOnlyList<string> StructureProblems)
    {
        public string Describe()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"branch={Branch}");
            sb.AppendLine($"census: {ProductRows.Count} product rows, {ConsumerRows.Count} consumer rows, "
                          + $"{BehaviourRows.Count} behaviour rows, {PrivacyAnswers.Count} privacy answers, "
                          + $"payload={CensusPayloadCount} ({CensusPayloadDisposition})");
            sb.AppendLine($"disk: {ProductFilesOnDisk.Count} product files, {ConsumersOnDisk.Count} consumers, "
                          + $"{PayloadFilesOnDisk} payload files");
            Append(sb, "product files upstream that the census never heard of", ProductFilesMissingFromCensus);
            Append(sb, "census product rows with no file on disk", ProductFilesMissingFromDisk);
            Append(sb, "consumers upstream that the census never heard of", ConsumersMissingFromCensus);
            Append(sb, "census consumer rows with no file on disk", ConsumersMissingFromDisk);
            Append(sb, "privacy violations", PrivacyViolations);
            Append(sb, "label violations", LabelViolations);
            Append(sb, "platform violations", PlatformViolations);
            Append(sb, "structure problems", StructureProblems);
            return sb.ToString();
        }

        private static void Append(StringBuilder sb, string title, IReadOnlyList<string> items)
        {
            if (items.Count == 0) return;
            sb.AppendLine($"{title} ({items.Count}):");
            foreach (var item in items) sb.AppendLine($"  - {item}");
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
        var productRows = ParseProductRows(text);
        var consumerRows = ParseConsumerRows(text);
        var behaviourRows = ParseBehaviourRows(text);
        var privacy = ParsePrivacyAnswers(text);
        var (payloadCount, payloadDisposition) = ParsePayload(text);

        var structure = new List<string>();
        if (productRows.Count == 0) structure.Add("the product-file table is empty or unparseable");
        if (consumerRows.Count == 0) structure.Add("the consumer table is empty or unparseable");
        if (behaviourRows.Count == 0) structure.Add("the behaviour table is empty or unparseable");
        if (privacy.Count != 4) structure.Add($"expected 4 privacy answers, parsed {privacy.Count}");

        var privacyViolations = new List<string>();
        foreach (var (id, startsWith, needle) in PrivacyVerdicts)
        {
            var match = privacy.FirstOrDefault(a => a.Id == id);
            if (match.Id is null)
            {
                privacyViolations.Add($"{id}: no answer row parsed");
                continue;
            }

            var answer = StripEmphasis(match.Answer);
            var ok = startsWith
                ? answer.StartsWith(needle, StringComparison.Ordinal)
                : answer.Contains(needle, StringComparison.OrdinalIgnoreCase);
            if (!ok)
            {
                privacyViolations.Add(
                    $"{id}: answer '{Trim(answer)}' no longer "
                    + (startsWith ? $"begins with '{needle}'" : $"names '{needle}'"));
            }
        }
        if (payloadCount < 0) structure.Add("the payload count is missing or unparseable");
        if (payloadDisposition.Length == 0) structure.Add("the payload disposition is missing");

        var labelViolations = new List<string>();
        var platformViolations = new List<string>();
        foreach (var row in behaviourRows)
        {
            if (!Labels.Any(l => row.Label.Contains(l, StringComparison.Ordinal)))
            {
                labelViolations.Add($"{row.Id}: label '{Trim(row.Label)}' is outside the closed vocabulary");
            }

            var hasWindows = row.Platform.Contains("Windows:", StringComparison.OrdinalIgnoreCase);
            var hasLinux = row.Platform.Contains("Linux:", StringComparison.OrdinalIgnoreCase);
            if (!hasWindows || !hasLinux)
            {
                platformViolations.Add(
                    $"{row.Id}: platform cell '{Trim(row.Platform)}' must carry BOTH a Windows: and a Linux: verdict");
            }
        }

        var wpfRoot = Path.Combine([root, .. WpfRootParts]);
        if (!Directory.Exists(wpfRoot))
        {
            return new Report(
                GuardBranch.Unreachable, productRows, consumerRows, behaviourRows, privacy, privacyViolations,
                payloadCount, payloadDisposition, [], [], 0, [], [], [], [],
                labelViolations, platformViolations, structure);
        }

        foreach (var directory in SurfaceDirectories.Concat(PayloadDirectories))
        {
            var full = Path.Combine(wpfRoot, directory.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(full))
            {
                structure.Add(
                    $"{directory} is missing from a PRESENT ConditioningControlPanel/ — a half-present "
                    + "reference tree is a corrupt checkout, not an unreachable one");
            }
        }

        if (structure.Count > 0)
        {
            return new Report(
                GuardBranch.Reachable, productRows, consumerRows, behaviourRows, privacy, privacyViolations,
                payloadCount, payloadDisposition, [], [], 0, [], [], [], [],
                labelViolations, platformViolations, structure);
        }

        var productOnDisk = ProductFiles(wpfRoot);
        var consumersOnDisk = ConsumerFiles(wpfRoot);
        var payloadOnDisk = PayloadFileCount(wpfRoot);

        var censusProduct = productRows.Select(r => r.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var censusConsumers = consumerRows.Select(r => r.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new Report(
            GuardBranch.Reachable, productRows, consumerRows, behaviourRows, privacy, privacyViolations,
            payloadCount, payloadDisposition,
            productOnDisk, consumersOnDisk, payloadOnDisk,
            [.. productOnDisk.Where(p => !censusProduct.Contains(p)).Order(StringComparer.Ordinal)],
            [.. censusProduct.Where(p => !productOnDisk.Contains(p, StringComparer.OrdinalIgnoreCase))
                             .Order(StringComparer.Ordinal)],
            [.. consumersOnDisk.Where(p => !censusConsumers.Contains(p)).Order(StringComparer.Ordinal)],
            [.. censusConsumers.Where(p => !consumersOnDisk.Contains(p, StringComparer.OrdinalIgnoreCase))
                               .Order(StringComparer.Ordinal)],
            labelViolations, platformViolations, structure);
    }

    // ---- re-derivation from the shipping bytes ----

    private static List<string> ProductFiles(string wpfRoot)
    {
        var found = new List<string>();
        foreach (var directory in SurfaceDirectories)
        {
            var full = Path.Combine(wpfRoot, directory.Replace('/', Path.DirectorySeparatorChar));
            foreach (var file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
            {
                found.Add(Relative(wpfRoot, file));
            }
        }

        found.Sort(StringComparer.Ordinal);
        return found;
    }

    private static int PayloadFileCount(string wpfRoot)
    {
        var count = 0;
        foreach (var directory in PayloadDirectories)
        {
            var full = Path.Combine(wpfRoot, directory.Replace('/', Path.DirectorySeparatorChar));
            count += Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories).Count();
        }

        return count;
    }

    /// <summary>
    /// Every file in the shipping tree naming one of the surface's types, EXCLUDING the surface's own
    /// directory and the <c>CCP.*</c> first-attempt projects. Walks the whole tree recursively; there
    /// is no file list anywhere in this method.
    /// </summary>
    private static List<string> ConsumerFiles(string wpfRoot)
    {
        var surfacePrefixes = SurfaceDirectories
            .Select(d => d.Replace('/', Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar)
            .ToArray();

        var found = new List<string>();
        foreach (var file in Directory.EnumerateFiles(wpfRoot, "*", SearchOption.AllDirectories))
        {
            var extension = Path.GetExtension(file);
            if (!ConsumerExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)) continue;

            var relative = Relative(wpfRoot, file);
            var native = relative.Replace('/', Path.DirectorySeparatorChar);
            if (native.StartsWith(FirstAttemptPrefix, StringComparison.Ordinal)) continue;
            if (surfacePrefixes.Any(p => native.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;
            if (native.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase)) continue;
            if (native.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase)) continue;

            string content;
            try { content = File.ReadAllText(file); }
            catch (IOException) { continue; }

            if (SurfaceTypeNames.Any(t => content.Contains(t, StringComparison.Ordinal)))
            {
                found.Add(relative);
            }
        }

        found.Sort(StringComparer.Ordinal);
        return found;
    }

    private static string Relative(string root, string file) =>
        Path.GetRelativePath(root, file).Replace('\\', '/');

    // ---- parsing the census document ----

    private static List<ProductRow> ParseProductRows(string text)
    {
        var rows = new List<ProductRow>();
        foreach (Match m in Regex.Matches(text, @"^\|\s*(P\d+)\s*\|\s*([^|]+?)\s*\|\s*$",
                     RegexOptions.Multiline))
        {
            rows.Add(new ProductRow(m.Groups[1].Value, m.Groups[2].Value.Trim()));
        }

        return rows;
    }

    private static List<ConsumerRow> ParseConsumerRows(string text)
    {
        var rows = new List<ConsumerRow>();
        foreach (Match m in Regex.Matches(text, @"^\|\s*(C\d+)\s*\|\s*([^|]+?)\s*\|\s*(code|comment)\s*\|\s*$",
                     RegexOptions.Multiline))
        {
            rows.Add(new ConsumerRow(m.Groups[1].Value, m.Groups[2].Value.Trim(), m.Groups[3].Value));
        }

        return rows;
    }

    /// <summary>The §3 behaviour table: id, then the label cell and the platform cell, which are the
    /// last two columns of each row.</summary>
    private static List<BehaviourRow> ParseBehaviourRows(string text)
    {
        var rows = new List<BehaviourRow>();
        foreach (Match m in Regex.Matches(text, @"^\|\s*(B\d+)\s*\|(.+)\|\s*$", RegexOptions.Multiline))
        {
            var cells = m.Groups[2].Value.Split('|');
            if (cells.Length < 2) continue;
            rows.Add(new BehaviourRow(
                m.Groups[1].Value,
                cells[^2],
                cells[^1]));
        }

        return rows;
    }

    private static List<(string Id, string Answer)> ParsePrivacyAnswers(string text)
    {
        var answers = new List<(string, string)>();
        foreach (Match m in Regex.Matches(text, @"^\|\s*(Q[1-4])\s*\|[^|]+\|([^|]+)\|", RegexOptions.Multiline))
        {
            var answer = m.Groups[2].Value.Trim();
            if (answer.Length > 0 && !answer.All(c => c == '-')) answers.Add((m.Groups[1].Value, answer));
        }

        return answers;
    }

    /// <summary>Markdown emphasis is presentation, not content: strip it before reading a verdict.</summary>
    private static string StripEmphasis(string cell) => cell.Replace("*", string.Empty).Trim();

    private static (int Count, string Disposition) ParsePayload(string text)
    {
        var count = -1;
        var disposition = string.Empty;

        var countMatch = Regex.Match(text, @"^\|\s*payload-files\s*\|\s*(\d+)\s*\|\s*$", RegexOptions.Multiline);
        if (countMatch.Success) count = int.Parse(countMatch.Groups[1].Value);

        var dispositionMatch = Regex.Match(text, @"^\|\s*disposition\s*\|\s*([^|]+?)\s*\|\s*$",
            RegexOptions.Multiline);
        if (dispositionMatch.Success) disposition = dispositionMatch.Groups[1].Value.Trim();

        return (count, disposition);
    }

    private static string Trim(string cell) =>
        cell.Trim().Length > 60 ? cell.Trim()[..60] + "…" : cell.Trim();

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
            $"repo root (the directory holding {string.Join('/', RepoAnchorParts)}) not found above "
            + $"{AppContext.BaseDirectory} — this guard fails rather than skips");
    }

    // ---- temp-dir repositories for the fixture facts ----

    private sealed class TempRepo : IDisposable
    {
        public TempRepo()
        {
            Root = Path.Combine(Path.GetTempPath(), "ccp-fyp-census-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Write(string relative, string content)
        {
            var full = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }

        public void Delete(string relative)
        {
            var full = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(full)) File.Delete(full);
        }

        public void RemoveDirectory(string relative)
        {
            var full = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(full)) Directory.Delete(full, recursive: true);
        }

        public void ReplaceInFile(string relative, string find, string replace)
        {
            var full = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
            var text = File.ReadAllText(full);
            File.WriteAllText(full, text.Replace(find, replace, StringComparison.Ordinal));
        }

        public void Dispose()
        {
            try { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// A miniature repository that AGREES with itself: the anchor, a census document with the same
    /// shape as the real one, the nine product files, the sixteen consumers, and eight payload files.
    /// Every fixture fact perturbs exactly one thing about it.
    /// </summary>
    private static void SeedConsistentRepo(TempRepo repo)
    {
        repo.Write("client/CcpClient.sln", "# anchor");

        foreach (var row in SeedProductPaths)
        {
            repo.Write($"ConditioningControlPanel/{row}", "// upstream product file");
        }

        foreach (var (path, needle) in SeedConsumers)
        {
            repo.Write($"ConditioningControlPanel/{path}", $"// uses {needle}");
        }

        for (var i = 0; i < ExpectedPayloadFiles; i++)
        {
            repo.Write($"ConditioningControlPanel/Resources/web/fyp/asset{i}.js", "// payload");
        }

        repo.Write(CensusRelativePath, SeedCensus());
    }

    private static readonly string[] SeedProductPaths =
    [
        "Services/Fyp/FypAssetManifest.cs",
        "Services/Fyp/FypGhostOverlay.cs",
        "Services/Fyp/FypHostService.cs",
        "Services/Fyp/FypMetaStore.cs",
        "Services/Fyp/Online/FypOnlineCoordinator.cs",
        "Services/Fyp/Online/IFeedSource.cs",
        "Services/Fyp/Online/RemoteMediaCache.cs",
        "Services/Fyp/Online/RemoteMediaFormats.cs",
        "Services/Fyp/Online/ScrolllerSource.cs",
    ];

    private static readonly (string Path, string Needle)[] SeedConsumers =
    [
        ("App.xaml.cs", "RemoteMediaCache"),
        ("Chaos/ChaosFlashOverlay.cs", "RemoteMediaCache"),
        ("Chaos/ChaosGifCascadeOverlay.cs", "RemoteMediaCache"),
        ("MainWindow/MainWindow.Assets.cs", "FypOnlineCoordinator"),
        ("MainWindow/MainWindow.Lab.cs", "FypHostService"),
        ("MainWindow/MainWindow.xaml.cs", "FypHostService"),
        ("Services/AutonomyService.cs", "FypHostService"),
        ("Services/BubbleCountService.cs", "FypHostService"),
        ("Services/Chaos/DtrhAssetManifest.cs", "FypOnlineCoordinator"),
        ("Services/Flash/FlashService.cs", "FypOnlineCoordinator"),
        ("Services/Quiz/IntakeHostService.cs", "FypOnlineCoordinator"),
        ("Services/Video/VideoService.cs", "RemoteMediaFormats"),
        ("Services/Video/WallpaperService.cs", "RemoteMediaCache"),
        ("Views/Tabs/AssetsTabView.xaml", "FypOnlineCoordinator"),
        ("Views/Tabs/PlayTabView.Cards.cs", "FypHostService"),
        ("Views/Tabs/PlayTabView.xaml", "FypHostService"),
    ];

    private static string SeedCensus()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# seed census");
        sb.AppendLine();
        sb.AppendLine("| # | Owner's phrase | WPF | Primitive | Anchor | Label | Platform |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        sb.AppendLine("| B1 | feed | x | y | z | **COVERED by video/webview** | "
                      + "Windows: proven-on-paper. Linux: unproven |");
        for (var i = 2; i <= 7; i++)
        {
            sb.AppendLine($"| B{i} | phrase | x | y | z | **GAP: something** | "
                          + "Windows: unproven. Linux: unproven |");
        }

        sb.AppendLine();
        sb.AppendLine("| # | Question | Answer | Citation |");
        sb.AppendLine("|---|---|---|---|");
        sb.AppendLine("| Q1 | persisted? | YES | a |");
        sb.AppendLine("| Q2 | shown to others? | NO | b |");
        sb.AppendLine("| Q3 | leaves the machine? | YES | c |");
        sb.AppendLine("| Q4 | sensor? | webcam | d |");
        sb.AppendLine();
        sb.AppendLine("| Id | Path |");
        sb.AppendLine("|---|---|");
        for (var i = 0; i < SeedProductPaths.Length; i++)
        {
            sb.AppendLine($"| P{i + 1} | {SeedProductPaths[i]} |");
        }

        sb.AppendLine();
        sb.AppendLine("| Id | Path | Kind |");
        sb.AppendLine("|---|---|---|");
        for (var i = 0; i < SeedConsumers.Length; i++)
        {
            var kind = SeedConsumers[i].Path.StartsWith("Chaos/", StringComparison.Ordinal)
                       || SeedConsumers[i].Path.StartsWith("Views/", StringComparison.Ordinal)
                ? "comment"
                : "code";
            sb.AppendLine($"| C{i + 1} | {SeedConsumers[i].Path} | {kind} |");
        }

        sb.AppendLine();
        sb.AppendLine("| Key | Value |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| payload-files | {ExpectedPayloadFiles} |");
        sb.AppendLine("| disposition | not-forked |");
        return sb.ToString();
    }
}
