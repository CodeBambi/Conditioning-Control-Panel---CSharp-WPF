using System.Text.RegularExpressions;
using CcpClient.Desktop.Features.Intake;
using CcpClient.Desktop.Features.Progression;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-128: the graded-run award path — upstream's <c>GamificationBridge.OnQuizCompleted</c>
/// perfect branch (<c>Services/GamificationBridge.cs:598-609</c>), and the defect it must not
/// import.
///
/// <para><b>WHICH FACT CATCHES WHICH REGRESSION — MEASURED, NOT ASSUMED.</b> The port defends the
/// distinctness in two layers, so no single fact covers both and an earlier draft of this comment
/// claimed otherwise. Every row below was watched red on that exact edit (record.md §4):</para>
/// <list type="bullet">
/// <item><b>Comparer alone</b> (<c>OrdinalIgnoreCase</c> → <c>Ordinal</c>, entry normalisation
/// intact) → reds <see cref="DistinctCategories_StayCaseInsensitive_AcrossThePersistedRoundTrip"/>,
/// <see cref="ADocumentAlreadyHoldingBothCasings_CollapsesOnLoad_RatherThanCountingTwice"/> and
/// <see cref="TheNamedComparers_FoldCaseForCategories_AndNeverForAwardIds"/>. Those seed the value
/// onto DISK, so it reaches the set through JSON binding and never through
/// <see cref="GradedRunAwards.NormalizeCategory"/> — the only thing left that can collapse it is
/// the comparer.</item>
/// <item><b>Normalisation alone</b> → reds
/// <see cref="NormalizeCategory_TrimsAndFoldsCase_AndEmptiesWhitespace"/> and
/// <see cref="WhitespaceOnlyCategory_IsRejected_ByTheConsumersOwnNormalisation"/>.</item>
/// <item><b>BOTH</b> → reds
/// <see cref="HonorRoll_DoesNotFireEarly_WhenTheTwoProducersDisagreeAboutCase"/>, which is the
/// user-visible outcome and reproduces upstream's early fire exactly. It does NOT red on either
/// single-layer regression, because either layer alone still produces the right answer. That is
/// what defence in depth means here, and stating it the other way round would be a description
/// outrunning its mechanism.</item>
/// </list>
///
/// <para><b>What these facts do NOT prove.</b> Pure logic over a temp file. Nothing here renders,
/// composites, takes input, opens a window or runs on Linux.
/// <see cref="TheIntakeCompletionPath_CallsTheAwardRecorder_AtItsOwnSeam"/> is a SOURCE-level
/// chokepoint pin: it proves the call site exists in the product text, and proves nothing
/// whatsoever about the window executing it.</para>
/// </summary>
public sealed class GradedRunAwardsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ccp-sp128-awards-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _log = [];

    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ } }

    private sealed class SinkAdapter(List<string> lines) : ILogSink
    {
        public void Log(string message) => lines.Add(message);
    }

    /// <summary>Counts COMPLETED atomic renames — the moment a write reaches the target file.</summary>
    private sealed class WriteCounter
    {
        public int Writes;
    }

    private (PersistenceStore<GradedRunAwardsDocument> Store, GradedRunAwards Awards) NewAwards(
        string? dir = null, WriteCounter? counter = null)
    {
        var defaults = new AtomicWriteHooks();
        var hooks = counter is null ? null : new AtomicWriteHooks
        {
            WriteTempFile = defaults.WriteTempFile,
            Rename = (temp, target) => { counter.Writes++; defaults.Rename(temp, target); },
        };

        var store = new PersistenceStore<GradedRunAwardsDocument>(
            new OperationRegistry().OwnerFor("IntakeGradedRunAwards"),
            new SinkAdapter(_log),
            Path.Combine(dir ?? Path.Combine(_root, Guid.NewGuid().ToString("N")), "graded_run_awards.json"),
            GradedRunAwardsDocument.CurrentSchemaVersion,
            hooks: hooks);
        store.StartAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult();
        return (store, new GradedRunAwards(store, _log.Add));
    }

    /// <summary>Write a document straight to disk — a producer the consumer's entry point cannot
    /// see. This is the shape a hand edit, or another build, leaves behind.</summary>
    private string SeedOnDisk(string json)
    {
        var dir = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "graded_run_awards.json"), json);
        return dir;
    }

    private static IntakeQuizRun Run(double total, double max, string niche) => new()
    {
        Niche = niche,
        TotalScore = total,
        MaxScore = max,
    };

    // ---------- top_of_the_class (GamificationBridge.cs:598-600) ----------

    [Fact]
    public void TopOfTheClass_Awards_OnTheFirstTopMarksRun()
    {
        var (_, awards) = NewAwards();

        var outcome = awards.RecordGradedRun(topMarks: true, category: "bambi");

        Assert.Equal([GradedRunAwards.TopOfTheClassId], outcome.Awarded);
        Assert.True(awards.IsAwarded(GradedRunAwards.TopOfTheClassId));
        Assert.False(awards.IsAwarded(GradedRunAwards.HonorRollId));
    }

    [Fact]
    public void NotTopMarks_AwardsNothing_AndRecordsNoCategory()
    {
        // :598 — the whole award branch is inside `if (e.Perfect)`. This port has no passed
        // branch at all (teachers_pet/held_back are residue), so a run below the bar is inert.
        var (store, awards) = NewAwards();

        var outcome = awards.RecordGradedRun(topMarks: false, category: "bambi");

        Assert.Empty(outcome.Awarded);
        Assert.Equal(0, awards.DistinctPerfectedCategories);
        Assert.False(store.IsDirty);
        Assert.Equal("bambi", outcome.Category); // still REPORTED, so the diagnostic line is honest
    }

    [Fact]
    public void TopOfTheClass_IsAwardedOnce_AcrossRepeatedTopMarksRuns()
    {
        // AchievementService.cs:1115 — `if (_progress.IsUnlocked(achievementId)) return false;`
        // is the FIRST statement of TryUnlockExclusive.
        var (_, awards) = NewAwards();

        var first = awards.RecordGradedRun(topMarks: true, category: "bambi");
        var second = awards.RecordGradedRun(topMarks: true, category: "bambi");

        Assert.Equal([GradedRunAwards.TopOfTheClassId], first.Awarded);
        Assert.Empty(second.Awarded);
        Assert.True(awards.IsAwarded(GradedRunAwards.TopOfTheClassId));
    }

    [Fact]
    public void EmptyCategory_StillAwardsTopOfTheClass_ButRecordsNothing()
    {
        // :600 fires BEFORE :602 looks at the category, so a categoryless run still earns it.
        var (_, awards) = NewAwards();

        var outcome = awards.RecordGradedRun(topMarks: true, category: null);

        Assert.Equal([GradedRunAwards.TopOfTheClassId], outcome.Awarded);
        Assert.Equal(0, awards.DistinctPerfectedCategories);
        Assert.False(outcome.CategoryWasNew);
    }

    // ---------- honor_roll: the threshold, the clause order, and the defect ----------

    [Fact]
    public void HonorRoll_FiresAtTheThirdDistinctCategory_AndNotTheSecond()
    {
        // HonorRollCategories = 3 (GamificationBridge.cs:40), pinned WITH its comparison: a
        // test that only asserts the constant is not evidence the award fires at it.
        var (_, awards) = NewAwards();

        var first = awards.RecordGradedRun(topMarks: true, category: "bambi");
        var second = awards.RecordGradedRun(topMarks: true, category: "sissy");
        var third = awards.RecordGradedRun(topMarks: true, category: "drone");

        Assert.DoesNotContain(GradedRunAwards.HonorRollId, first.Awarded);
        Assert.DoesNotContain(GradedRunAwards.HonorRollId, second.Awarded);
        Assert.Equal([GradedRunAwards.HonorRollId], third.Awarded);
        Assert.Equal(3, awards.DistinctPerfectedCategories);
        Assert.Equal(3, GradedRunAwards.HonorRollCategories);
    }

    [Fact]
    public void HonorRoll_DoesNotFireEarly_WhenTheTwoProducersDisagreeAboutCase()
    {
        // THE CENTRAL TRAP (D226). Upstream's set is ordinal (AchievementProgress.cs:169) and
        // its consumer adds the category raw (:602), while its two producers disagree: the
        // intake normalises (IntakeHostService.cs:427-429) and the classic quiz passes the
        // PascalCase enum name (QuizWindow.xaml.cs:540, QuizCategory.cs:6-13). Upstream would
        // count "sissy" and "Sissy" as two of three slots and fire honor_roll a category early
        // — permanently, because the set is persisted.
        var (_, awards) = NewAwards();

        awards.RecordGradedRun(topMarks: true, category: "sissy");   // producer A, normalised
        var duplicate = awards.RecordGradedRun(topMarks: true, category: "Sissy"); // producer B's shape
        var third = awards.RecordGradedRun(topMarks: true, category: "bambi");

        Assert.False(duplicate.CategoryWasNew);
        Assert.Equal(2, awards.DistinctPerfectedCategories);
        Assert.DoesNotContain(GradedRunAwards.HonorRollId, third.Awarded);
        Assert.False(awards.IsAwarded(GradedRunAwards.HonorRollId));
    }

    [Fact]
    public void DistinctCategories_StayCaseInsensitive_AcrossThePersistedRoundTrip()
    {
        // THE COMPARER-SPECIFIC PIN. "Sissy" arrives through JSON binding and never passes
        // NormalizeCategory, so the ONLY thing that can collapse it is the set's comparer.
        // Flip CategoryComparer to StringComparer.Ordinal with the entry normalisation fully
        // intact and this reds; HonorRoll_DoesNotFireEarly... alone would not see it.
        var dir = SeedOnDisk("{\"schemaVersion\":1,\"perfectedCategories\":[\"Sissy\"],\"awardedIds\":[]}");
        var (store, awards) = NewAwards(dir);

        Assert.Equal(LoadOutcome.Loaded.Instance, store.LastLoadOutcome);
        Assert.Equal(1, awards.DistinctPerfectedCategories);

        var outcome = awards.RecordGradedRun(topMarks: true, category: "sissy");

        Assert.False(outcome.CategoryWasNew);
        Assert.Equal(1, awards.DistinctPerfectedCategories);
        Assert.False(awards.IsAwarded(GradedRunAwards.HonorRollId));
    }

    [Fact]
    public void ADocumentAlreadyHoldingBothCasings_CollapsesOnLoad_RatherThanCountingTwice()
    {
        // The healing half of the same divergence: a file written by upstream's own arrangement
        // (or by a hand edit) holds both casings. Re-wrapping through the named comparer at bind
        // time collapses them, so the duplicate is not permanent the way it is upstream.
        var dir = SeedOnDisk("{\"schemaVersion\":1,\"perfectedCategories\":[\"Sissy\",\"sissy\",\"  SISSY  \"],\"awardedIds\":[]}");
        var (store, awards) = NewAwards(dir);

        Assert.Equal(LoadOutcome.Loaded.Instance, store.LastLoadOutcome);
        Assert.Equal(2, awards.DistinctPerfectedCategories); // "sissy" and the untrimmed "  SISSY  "
        Assert.Contains("Sissy", store.Current.PerfectedCategories);
        Assert.Contains("sissy", store.Current.PerfectedCategories);
    }

    [Fact]
    public void HonorRoll_FiresOnlyOnTheRunThatGrowsTheSet()
    {
        // :602's middle clause. `&&` short-circuits, so `Count >= 3` is evaluated ONLY when
        // `Add` returned true. Three categories already cleared and nothing yet awarded: a
        // perfect run in an ALREADY-cleared category must not award, because the set did not
        // grow. Ignore Add's return value and this reds.
        var dir = SeedOnDisk("{\"schemaVersion\":1,\"perfectedCategories\":[\"bambi\",\"sissy\",\"drone\"],\"awardedIds\":[]}");
        var (_, awards) = NewAwards(dir);

        Assert.Equal(3, awards.DistinctPerfectedCategories);

        var repeat = awards.RecordGradedRun(topMarks: true, category: "bambi");

        Assert.DoesNotContain(GradedRunAwards.HonorRollId, repeat.Awarded);
        Assert.False(awards.IsAwarded(GradedRunAwards.HonorRollId));

        var fourth = awards.RecordGradedRun(topMarks: true, category: "circe");

        Assert.Equal([GradedRunAwards.HonorRollId], fourth.Awarded); // :603 is `>=`, so 4 counts
    }

    // ---------- normalisation at the consumer ----------

    [Theory]
    [InlineData("bambi", "bambi")]
    [InlineData(" Sissy ", "sissy")]
    [InlineData("DRONE", "drone")]
    [InlineData("Sissy", "sissy")]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void NormalizeCategory_TrimsAndFoldsCase_AndEmptiesWhitespace(string? raw, string expected)
    {
        // The same call upstream's producer makes (IntakeHostService.cs:427-429), moved to the
        // point every producer must pass through.
        Assert.Equal(expected, GradedRunAwards.NormalizeCategory(raw));
    }

    [Fact]
    public void WhitespaceOnlyCategory_IsRejected_ByTheConsumersOwnNormalisation()
    {
        // D227. Upstream guards with `!string.IsNullOrEmpty(e.Category)` (:602), which ADMITS a
        // whitespace-only category into an ordinal set. The port normalises first, so it is
        // rejected. Through the port's own producer this never fires — IntakeGraded.Category
        // substitutes IntakeNiche.Fallback for whitespace — so it is a property of the
        // consumer's contract for FUTURE producers, not a live difference on the shipped path.
        var (_, awards) = NewAwards();

        var outcome = awards.RecordGradedRun(topMarks: true, category: "   ");

        Assert.Equal([GradedRunAwards.TopOfTheClassId], outcome.Awarded);
        Assert.Equal(0, awards.DistinctPerfectedCategories);
        Assert.Equal(string.Empty, outcome.Category);
    }

    [Fact]
    public void TheNamedComparers_FoldCaseForCategories_AndNeverForAwardIds()
    {
        // The comparer is NAMED and reachable, so the pin does not depend on how a HashSet
        // reports the instance it was handed.
        var fresh = new GradedRunAwardsDocument();

        Assert.Same(StringComparer.OrdinalIgnoreCase, GradedRunAwards.CategoryComparer);
        Assert.Same(StringComparer.Ordinal, GradedRunAwards.AwardIdComparer);
        Assert.True(fresh.PerfectedCategories.Comparer.Equals("sissy", "SISSY"));
        Assert.False(fresh.AwardedIds.Comparer.Equals(GradedRunAwards.HonorRollId, "HONOR_ROLL"));
    }

    // ---------- persistence ----------

    [Fact]
    public async Task Awards_SurviveTheRoundTrip_AndNeverReFire()
    {
        var dir = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        var (store, awards) = NewAwards(dir);

        awards.RecordGradedRun(topMarks: true, category: "bambi");
        awards.RecordGradedRun(topMarks: true, category: "sissy");
        awards.RecordGradedRun(topMarks: true, category: "drone");
        await store.SaveImmediate();
        await store.StopAsync();

        var (reloaded, awardsAgain) = NewAwards(dir);

        Assert.Equal(LoadOutcome.Loaded.Instance, reloaded.LastLoadOutcome);
        Assert.True(awardsAgain.IsAwarded(GradedRunAwards.TopOfTheClassId));
        Assert.True(awardsAgain.IsAwarded(GradedRunAwards.HonorRollId));
        Assert.Equal(3, awardsAgain.DistinctPerfectedCategories);
        Assert.Empty(awardsAgain.RecordGradedRun(topMarks: true, category: "circe").Awarded);
    }

    [Fact]
    public async Task ARunThatChangesNothing_EnqueuesNoWrite()
    {
        // D230. Upstream's `Ach?.MarkDirty()` (:609) runs unconditionally, but it flags a 30 s
        // save TIMER and upstream's passed branch always moves a counter, so it never has this
        // case. This store has no timer, so the OUTCOME of "mark dirty" is "the change reaches
        // disk" — and there is no change here to reach it.
        //
        // THE MECHANISM MATTERS AND THE FIRST ONE I WROTE WAS WRONG. Asserting `store.IsDirty`
        // stays false does NOT observe this: a Save() clears the flag, so an unconditional save
        // leaves IsDirty false too and the assertion passed with the defect installed (record.md
        // §4, demo 10). Counting COMPLETED RENAMES through the injected atomic-write hook does
        // observe it. The chained writer makes the count deterministic without any wall clock:
        // SaveImmediate() awaits the whole enqueued chain, so a stray write issued before it has
        // necessarily completed and been counted by the time the await returns.
        var counter = new WriteCounter();
        var (store, awards) = NewAwards(counter: counter);

        awards.RecordGradedRun(topMarks: true, category: "bambi"); // a real change: awards + adds
        await store.SaveImmediate();
        var afterTheRealChange = counter.Writes;

        Assert.True(afterTheRealChange > 0, "the run that DID change something must have written");

        awards.RecordGradedRun(topMarks: true, category: "bambi");  // award held, category held
        awards.RecordGradedRun(topMarks: false, category: "sissy"); // below the bar
        await store.SaveImmediate();

        // Exactly ONE more rename — this test's own explicit SaveImmediate. Two no-change runs
        // that each issued a save would land on three.
        Assert.Equal(afterTheRealChange + 1, counter.Writes);
        Assert.False(store.IsDirty);
    }

    [Fact]
    public void TheAwardableSet_IsExactlyTheTwoThisPacketBuilds()
    {
        // teachers_pet (:41, :588-589) and held_back (:42, :592-595) need the passed branch and
        // its two persisted counters — named residue (census §6.2), and held_back is unreachable
        // here by construction because the port's only producer emits `passed: true` (:433).
        // Widening this list without building the state behind it would ship an award nothing
        // can earn, which is the #870 dead-letter upstream's own comments complain about.
        var (store, awards) = NewAwards();

        awards.RecordGradedRun(topMarks: true, category: "bambi");
        awards.RecordGradedRun(topMarks: true, category: "sissy");
        awards.RecordGradedRun(topMarks: true, category: "drone");
        awards.RecordGradedRun(topMarks: false, category: "circe");

        Assert.Equal([GradedRunAwards.TopOfTheClassId, GradedRunAwards.HonorRollId], GradedRunAwards.AwardableIds);
        Assert.Equal(GradedRunAwards.AwardableIds.Order(), store.Current.AwardedIds.Order());
        Assert.DoesNotContain("teachers_pet", store.Current.AwardedIds);
        Assert.DoesNotContain("held_back", store.Current.AwardedIds);
    }

    // ---------- the intake adapter, and the seam ----------

    [Fact]
    public void IntakeGraded_Record_FeedsTheNormalisedNiche_AndTheTopMarksVerdict()
    {
        var (_, awards) = NewAwards();

        var below = IntakeGraded.Record(awards, Run(8.999, 10, " BAMBI "));   // 89.99% — under the bar
        var atBar = IntakeGraded.Record(awards, Run(9, 10, " BAMBI "));        // exactly 90.0%

        Assert.Empty(below.Awarded);
        Assert.Equal("bambi", below.Category);
        Assert.Equal([GradedRunAwards.TopOfTheClassId], atBar.Awarded);
        Assert.Equal("bambi", atBar.Category);
        Assert.Equal(1, awards.DistinctPerfectedCategories);
    }

    [Fact]
    public void IntakeGraded_Record_TreatsAZeroMaxRunAsNeverTopMarks()
    {
        // Pins the OUTCOME — a zero-max run reaches the award path and awards nothing.
        //
        // WHAT IT DOES NOT PIN, stated because the name could be read as more: upstream's
        // `run.MaxScore > 0 &&` conjunct (:434) is REDUNDANT with the percentage it guards —
        // ScorePercent's own ternary already returns 0.0 for a zero max (:426) — so removing
        // that conjunct leaves this fact green. It is ported verbatim anyway, because a
        // zero-max run has no grade at all; plan.md 1.1 books the redundancy.
        var (_, awards) = NewAwards();

        var outcome = IntakeGraded.Record(awards, Run(5, 0, "bambi"));

        Assert.Empty(outcome.Awarded);
        Assert.Equal(0, awards.DistinctPerfectedCategories);
    }

    [Fact]
    public void TheIntakeCompletionPath_CallsTheAwardRecorder_AtItsOwnSeam()
    {
        // A SOURCE-LEVEL CHOKEPOINT PIN, named for exactly what it is. Every other fact here
        // would stay green if the call were deleted from OnQuizResult, because the window is
        // not constructible in a pure-logic project. This reds on that deletion — and it is
        // NOT evidence that the window runs, renders, or is ever reached.
        var window = ReadRepoFile("client/src/CcpClient.Desktop/Features/Intake/IntakeHostWindow.axaml.cs");

        Assert.Contains("IntakeGraded.Record(_context.Awards, run)", window, StringComparison.Ordinal);
        Assert.Contains("_context.Pass.ConsumeForCompletedIntake();", window, StringComparison.Ordinal);
        Assert.True(
            window.IndexOf("IntakeGraded.Record(_context.Awards, run)", StringComparison.Ordinal)
            < window.IndexOf("_context.Pass.ConsumeForCompletedIntake();", StringComparison.Ordinal),
            "the award is recorded BEFORE the pass spend, mirroring GamificationBridge's handler running "
            + "before IntakeHostService.cs:465 — a reorder would change which side of a mid-completion "
            + "crash the award falls on");
    }

    // ---------- the thresholds, re-derived from the shipping bytes ----------

    [Fact]
    public void ThePortsThresholds_MatchTheShippingSourceBytes()
    {
        // The packet's rule: pin what you claim. These constants are re-derived from
        // ConditioningControlPanel/ on every run, so the port cannot drift from the source it
        // says it ported, and a source change is reported here rather than discovered later.
        var bridge = ReadRepoFile("ConditioningControlPanel/Services/GamificationBridge.cs");
        var intake = ReadRepoFile("ConditioningControlPanel/Services/Quiz/IntakeHostService.cs");

        Assert.Equal(
            GradedRunAwards.HonorRollCategories.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ExtractOne(bridge, @"HonorRollCategories\s*=\s*(\d+)"));
        Assert.Equal("90.0", ExtractOne(intake, @"TopMarksPercent\s*=\s*([\d.]+)"));
        Assert.Equal(90.0, IntakeGraded.TopMarksPercent);
        Assert.Contains($"TryUnlockExclusive(\"{GradedRunAwards.TopOfTheClassId}\")", bridge, StringComparison.Ordinal);
        Assert.Contains($"TryUnlockExclusive(\"{GradedRunAwards.HonorRollId}\")", bridge, StringComparison.Ordinal);
    }

    [Fact]
    public void TheUpstreamClauseOrder_IsStillEmptyThenAddThenCount()
    {
        // The clause ORDER is behaviour, because `&&` short-circuits: Add runs only for a
        // non-empty category, and the count is compared only when Add grew the set. This
        // re-derives that order from the shipping bytes so the port's copy of it cannot be
        // "corrected" on one side only.
        var bridge = ReadRepoFile("ConditioningControlPanel/Services/GamificationBridge.cs");
        var clause = ExtractOne(bridge,
            @"if \(!string\.IsNullOrEmpty\(e\.Category\) && (p\.PerfectedQuizCategories\.Add\(e\.Category\))");

        Assert.Equal("p.PerfectedQuizCategories.Add(e.Category)", clause);
        Assert.Contains("PerfectedQuizCategories.Count >= HonorRollCategories", bridge, StringComparison.Ordinal);
        Assert.Contains("public HashSet<string> PerfectedQuizCategories { get; set; } = new();",
            ReadRepoFile("ConditioningControlPanel/Models/AchievementProgress.cs"), StringComparison.Ordinal);
    }

    // ---------- helpers: they FAIL, never skip, and they own the existence checks ----------

    /// <summary>
    /// Reads a repository file, failing loudly when it is absent. The existence check lives HERE
    /// on purpose: the vacuous-shape detector reads only <c>[Fact]</c>/<c>[Theory]</c> bodies
    /// (<c>VacuousShapeDetector.cs:225-228</c>), and a <c>File.Exists</c> in a fact body is a
    /// dispositioned site. A guarded early return would be worse still — the reference tree is
    /// IN this repository, so its absence is a corrupt checkout, never an environment to pass in
    /// (the <c>VacuousShapeDetector.Scan</c> refuses-to-skip precedent, <c>:70-74</c>).
    /// </summary>
    private static string ReadRepoFile(string relative)
    {
        var path = Path.Combine(RepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"{relative} is missing at {path} — this guard never skips");
        return File.ReadAllText(path);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "client", "CcpClient.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        Assert.Fail($"repo root not found walking up from {AppContext.BaseDirectory} (anchor client/CcpClient.sln) — this guard never skips");
        return string.Empty;
    }

    private static string ExtractOne(string text, string pattern)
    {
        var match = Regex.Match(text, pattern);
        Assert.True(match.Success, $"the shipping source no longer matches /{pattern}/ — the port's constant is now unanchored");
        return match.Groups[1].Value;
    }
}
