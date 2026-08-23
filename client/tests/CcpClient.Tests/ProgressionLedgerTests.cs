using System.Text.Json;
using System.Text.RegularExpressions;
using CcpClient.Desktop.Features.Arcademy;
using CcpClient.Desktop.Features.Dtrh;
using CcpClient.Desktop.Features.Progression;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// THE PROGRESSION SPINE: the level curve, the XP ledger, its persistence, and the three payouts
/// that used to be computed and thrown away.
///
/// <para><b>WHAT EACH FACT CATCHES.</b> The curve is the thing that has to be exactly right, so it
/// is pinned twice from opposite directions: <see cref="TheLevelCurve_IsUpstreamsV1_BandByBand"/>
/// holds the port's ANSWERS (move any threshold, any slope or any base and exactly this fact reds),
/// and <see cref="TheCurvesLiterals_AreStillTheShippingSourcesLiterals"/> holds the port against the
/// shipping BYTES, so a change made upstream is reported here rather than silently diverged from.
/// The spend loop, the ceiling, the two amount refusals and the unknown-ledger refusal each red on
/// removing exactly the guard they name.</para>
///
/// <para><b>WHAT THESE FACTS DO NOT PROVE.</b> Pure logic over temp files. Nothing here renders,
/// composites, takes input, opens a window, plays a sound or runs on Linux. There is no level-up
/// ceremony to test because the port builds none.
/// <see cref="TheIntakeCompletionPath_BanksItsXp_AtItsOwnSeam"/> is a SOURCE-level chokepoint pin:
/// it proves the call site exists in the product text, and proves nothing whatsoever about the
/// window executing it. The Arcademy fact drives <see cref="ArcademySession"/> directly; no Arcademy
/// host window exists in this build to drive it for real.</para>
/// </summary>
public sealed class ProgressionLedgerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ccp-progression-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _log = [];

    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ } }

    private sealed class SinkAdapter(List<string> lines) : ILogSink
    {
        public void Log(string message) => lines.Add(message);
    }

    private string NewDir()
    {
        var dir = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private (PersistenceStore<ProgressionDocument> Store, ProgressionLedger Ledger, string Path) NewLedger(string? dir = null)
    {
        var path = Path.Combine(dir ?? NewDir(), ProgressionDocument.FileName);
        var store = new PersistenceStore<ProgressionDocument>(
            new OperationRegistry().OwnerFor("ProgressionLedgerTests-" + Guid.NewGuid().ToString("N")),
            new SinkAdapter(_log),
            path,
            ProgressionDocument.CurrentSchemaVersion);
        store.StartAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult(); // wallclock-allow: PersistenceStore.StartAsync loads on the calling thread and hands back an already-complete task (pinned by PersistenceStoreTests) — this bridge waits on nothing
        return (store, new ProgressionLedger(store, _log.Add), path);
    }

    /// <summary>Write a document straight to disk — the shape a hand edit or another build leaves
    /// behind, reaching the store through JSON binding and not through any entry point.</summary>
    private string SeedOnDisk(string json)
    {
        var dir = NewDir();
        File.WriteAllText(Path.Combine(dir, ProgressionDocument.FileName), json);
        return dir;
    }

    /// <summary>The descent's slot machinery, started. Out of the test bodies so the inline start
    /// bridge stays where xUnit1031 does not have to reason about it.</summary>
    private DtrhSaveSlots NewSlots()
    {
        var slots = new DtrhSaveSlots(
            new ParticipantInfrastructure(new OperationRegistry(), new UiDispatchBoundary(), new SinkAdapter(_log)),
            NewDir());
        slots.StartAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult(); // wallclock-allow: only awaits PersistenceStore starts, each already complete when handed over (pinned by PersistenceStoreTests)
        return slots;
    }

    /// <summary>An Arcademy settings store, started. Same reason as <see cref="NewSlots"/>.</summary>
    private PersistenceStore<ArcademySettingsDocument> NewArcademySettings()
    {
        var settings = new PersistenceStore<ArcademySettingsDocument>(
            new OperationRegistry().OwnerFor("ProgressionLedgerTests-arcademy-" + Guid.NewGuid().ToString("N")),
            new SinkAdapter(_log),
            Path.Combine(NewDir(), "arcademy_settings.json"),
            ArcademySettingsDocument.CurrentSchemaVersion);
        settings.StartAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult(); // wallclock-allow: loads on the calling thread and hands back an already-complete task (pinned by PersistenceStoreTests)
        return settings;
    }

    // ==================================================================================
    // The curve.
    // ==================================================================================

    /// <summary>
    /// Upstream's <c>XpForLevelV1</c> (<c>ProgressionService.cs:300-335</c>), answer for answer, at
    /// every band boundary and on both sides of it.
    ///
    /// <para>Boundary levels appear in exactly one band but are reachable by two formulas, and the
    /// seams are continuous by construction: 80 → 2500 closes band 1 and opens band 2, 100 → 4000,
    /// 125 → 6000, 150 → 10000. A moved threshold, a changed slope or a changed base all show up
    /// here and nowhere else in this file.</para>
    ///
    /// <para>L40 = 1639 is upstream's own corroboration: the Descent recurve restarts its second
    /// segment from the flat literal 1639 and calls out the 0.24 XP discontinuity that leaves
    /// (<c>:349-352</c>). Getting 1639 out of THIS band is the check that v1's slope is the slope
    /// that literal came from.</para>
    /// </summary>
    [Fact]
    public void TheLevelCurve_IsUpstreamsV1_BandByBand()
    {
        // Band 1 (:301-305): 800 → 2500 across L1-80, slope 1700/79.
        Assert.Equal(800, XpCurve.XpForLevel(1));
        Assert.Equal(822, XpCurve.XpForLevel(2));
        Assert.Equal(1639, XpCurve.XpForLevel(40));
        Assert.Equal(2478, XpCurve.XpForLevel(79));
        Assert.Equal(2500, XpCurve.XpForLevel(80));

        // Band 2 (:307-312): 2500 → 4000 across L81-100, slope 75.
        Assert.Equal(2575, XpCurve.XpForLevel(81));
        Assert.Equal(4000, XpCurve.XpForLevel(100));

        // Band 3 (:314-319): 4000 → 6000 across L101-125, slope 80.
        Assert.Equal(4080, XpCurve.XpForLevel(101));
        Assert.Equal(6000, XpCurve.XpForLevel(125));

        // Band 4 (:321-326): 6000 → 10000 across L126-150, slope 160.
        Assert.Equal(6160, XpCurve.XpForLevel(126));
        Assert.Equal(10000, XpCurve.XpForLevel(150));

        // Band 5 (:328-334): 3% compound from 10000.
        Assert.Equal(10300, XpCurve.XpForLevel(151));
        Assert.Equal(43839, XpCurve.XpForLevel(200));
        Assert.Equal(311192160, XpCurve.XpForLevel(XpCurve.MaxLevel));

        // Cumulative is the plain sum of the costs below a level (:413-421), and its floor is 0 for
        // the first level rather than the cost of a level nobody has to buy.
        Assert.Equal(0, XpCurve.CumulativeXpToReachLevel(XpCurve.FirstLevel));
        Assert.Equal(1622, XpCurve.CumulativeXpToReachLevel(3));      // 800 + 822
    }

    /// <summary>
    /// The port's five bands, re-derived from the shipping bytes on every run. A change made to
    /// <c>XpForLevelV1</c> upstream is reported HERE rather than discovered as a silent divergence
    /// later — and the guard is scoped to v1's body so the Descent recurve's own literals
    /// (<c>:360-393</c>) cannot satisfy it by accident.
    /// </summary>
    [Fact]
    public void TheCurvesLiterals_AreStillTheShippingSourcesLiterals()
    {
        var v1 = ExtractOne(
            ReadRepoFile("ConditioningControlPanel/Services/Progression/ProgressionService.cs"),
            @"public static double XpForLevelV1\(int level\)\s*\{(.*?)\n        \}",
            RegexOptions.Singleline);

        Assert.Contains("Math.Round(800 + (level - 1) * (1700.0 / 79))", v1, StringComparison.Ordinal);
        Assert.Contains("baseAt80 + (level - 80) * (1500.0 / 20)", v1, StringComparison.Ordinal);
        Assert.Contains("baseAt100 + (level - 100) * (2000.0 / 25)", v1, StringComparison.Ordinal);
        Assert.Contains("baseAt125 + (level - 125) * (4000.0 / 25)", v1, StringComparison.Ordinal);
        Assert.Contains("baseAt150 * Math.Pow(1.03, level - 150)", v1, StringComparison.Ordinal);

        // The band thresholds and the three bases the expressions above only NAME.
        Assert.Contains("level <= 80", v1, StringComparison.Ordinal);
        Assert.Contains("level <= 100", v1, StringComparison.Ordinal);
        Assert.Contains("level <= 125", v1, StringComparison.Ordinal);
        Assert.Contains("level <= 150", v1, StringComparison.Ordinal);
        Assert.Contains("baseAt80 = 2500", v1, StringComparison.Ordinal);
        Assert.Contains("baseAt100 = 4000", v1, StringComparison.Ordinal);
        Assert.Contains("baseAt125 = 6000", v1, StringComparison.Ordinal);
        Assert.Contains("baseAt150 = 10000", v1, StringComparison.Ordinal);

        // v1 rounds with the framework DEFAULT; only v2 passes AwayFromZero (:354-358). No v1 cost
        // can land on a midpoint, so the mode is unobservable — which is exactly why a reader who
        // assumed otherwise could "tidy" it without a single fact going red. This one would.
        Assert.DoesNotContain("MidpointRounding", v1, StringComparison.Ordinal);

        // And the ceiling this port borrows for its spend loop is still upstream's own number.
        Assert.Equal(
            XpCurve.MaxLevel.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ExtractOne(
                ReadRepoFile("ConditioningControlPanel/Services/Progression/ProgressionService.cs"),
                @"MaxDerivableLevel\s*=\s*(\d+)"));
    }

    /// <summary>
    /// <c>SpendXPOnLevels</c> (<c>:212-223</c>): drain the bank for as long as the next level is
    /// affordable, and keep the remainder.
    ///
    /// <para>The comparison is <c>&gt;=</c> (<c>:215</c>), so a bank exactly equal to the cost buys
    /// the level. Changing it to <c>&gt;</c> reds the 800 case below and nothing else here.</para>
    /// </summary>
    [Fact]
    public void SpendingXp_BuysEveryLevelItCanAfford_AndBanksTheRemainder()
    {
        // One XP short of L1's cost buys nothing and keeps everything.
        var short1 = XpCurve.Spend(1, 799);
        Assert.Equal(1, short1.Level);
        Assert.Equal(799, short1.XpIntoLevel);
        Assert.Equal(0, short1.LevelsGained);

        // EXACTLY the cost buys the level and leaves nothing (:215's >=).
        var exact = XpCurve.Spend(1, 800);
        Assert.Equal(2, exact.Level);
        Assert.Equal(0, exact.XpIntoLevel);
        Assert.Equal(1, exact.LevelsGained);

        // Two levels in one spend: 800 + 822 = 1622, and the odd XP rides along.
        var two = XpCurve.Spend(1, 1622 + 7);
        Assert.Equal(3, two.Level);
        Assert.Equal(7, two.XpIntoLevel);
        Assert.Equal(2, two.LevelsGained);
        Assert.False(two.AtCeiling);
    }

    /// <summary>
    /// The ceiling (<c>MaxDerivableLevel</c>, <c>:22-27</c>, applied to the spend loop — the stated
    /// divergence on <see cref="XpCurve.MaxLevel"/>): a garbage figure from a hosted page stops the
    /// loop at 500 instead of grinding through tens of thousands of iterations, and the overflow
    /// STAYS BANKED rather than being destroyed (upstream's derive loop keeps its remainder the same
    /// way, <c>:451</c>).
    /// </summary>
    [Fact]
    public void TheCeiling_StopsTheSpendLoop_AndTheOverflowStaysBanked()
    {
        var absurd = XpCurve.Spend(1, double.MaxValue);
        Assert.Equal(XpCurve.MaxLevel, absurd.Level);
        Assert.True(absurd.AtCeiling);
        Assert.Equal(XpCurve.MaxLevel - 1, absurd.LevelsGained);
        Assert.True(absurd.XpIntoLevel > 0, "the overflow is kept, not swallowed");

        // Already at the ceiling: nothing is spent and everything is kept.
        var parked = XpCurve.Spend(XpCurve.MaxLevel, 1_000_000);
        Assert.Equal(XpCurve.MaxLevel, parked.Level);
        Assert.Equal(1_000_000, parked.XpIntoLevel);
        Assert.Equal(0, parked.LevelsGained);
        Assert.True(parked.AtCeiling);
    }

    // ==================================================================================
    // The ledger.
    // ==================================================================================

    /// <summary>
    /// A grant moves the level and REACHES DISK. Asserted through the store's own quiescence signal
    /// and the bytes on the other side of it — never through <c>IsDirty</c>, which the write that a
    /// call itself enqueued clears, and which is therefore racy by construction.
    /// </summary>
    [Fact]
    public async Task AGrant_MovesTheLevel_AndTheFileHoldsTheLevelAndTheRemainder()
    {
        var (store, ledger, path) = NewLedger();

        // A fresh ledger is a KNOWN level 1 with nothing into it (AppSettings.cs:237, :244) — that
        // is an answer, not a guess, so it is not null.
        Assert.True(ledger.Known);
        Assert.Equal(1, ledger.Level);
        Assert.Equal(0, ledger.XpIntoLevel);
        Assert.Equal(800, ledger.XpForNextLevel);
        Assert.Equal(0, ledger.HighestLevelEver);        // AppSettings.cs:5455 — 0, not 1

        var first = ledger.Grant(500, "test");
        Assert.True(first.Banked);
        Assert.False(first.LeveledUp);
        Assert.Equal(1, first.LevelBefore);
        Assert.Equal(1, first.LevelAfter);

        var second = ledger.Grant(400, "test");           // 900 total: buys L2 (800), keeps 100
        Assert.True(second.Banked);
        Assert.True(second.LeveledUp);
        Assert.Equal(1, second.LevelBefore);
        Assert.Equal(2, second.LevelAfter);
        Assert.Equal(2, ledger.Level);
        Assert.Equal(100, ledger.XpIntoLevel);
        Assert.Equal(822, ledger.XpForNextLevel);
        Assert.Equal(2, ledger.HighestLevelEver);         // :220-223

        Assert.IsType<OperationOutcome.Completed>(await store.SaveImmediate());

        var onDisk = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
        Assert.Equal(2, onDisk.GetProperty("level").GetInt32());
        Assert.Equal(100, onDisk.GetProperty("xp").GetDouble());
        Assert.Equal(2, onDisk.GetProperty("highestLevelEver").GetInt32());
        Assert.Equal(
            ProgressionDocument.CurrentSchemaVersion,
            onDisk.GetProperty(PersistenceStore<ProgressionDocument>.SchemaVersionKey).GetInt32());
    }

    /// <summary>
    /// A non-finite amount is refused at the door. Every one of the three wired sources gets its
    /// number from a hosted web page's frame — the descent's <c>score</c> is a double the PAGE
    /// chooses — and one NaN banked once makes <c>xp</c> permanently NaN, every later comparison
    /// false, and the level frozen for good. Delete the <c>IsFinite</c> guard and this reds.
    /// </summary>
    [Fact]
    public void ANonFiniteAmount_IsRefused_AndTheLedgerStaysUsableAfterwards()
    {
        var (_, ledger, _) = NewLedger();

        foreach (var poison in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            var refused = ledger.Grant(poison, "test");
            Assert.False(refused.Banked);
            Assert.Equal(XpGrantState.RefusedNotFinite, refused.State);
        }

        // The proof that the refusal mattered: the ledger still adds up afterwards.
        Assert.True(ledger.Grant(800, "test").LeveledUp);
        Assert.Equal(2, ledger.Level);
        Assert.Equal(0, ledger.XpIntoLevel);
    }

    /// <summary>
    /// Zero and negative are refused, which is upstream's own first statement in <c>AddClaimedXP</c>
    /// (<c>:85</c>). The live case is not hypothetical: an Arcademy retake computes exactly 0
    /// (<c>ArcademyHostService.cs:1388</c>), and upstream's <c>if (xp &gt; 0)</c> (<c>:1394</c>) is
    /// the same decision made at the call site instead.
    /// </summary>
    [Fact]
    public void AZeroOrNegativeAmount_IsRefused_TheWayUpstreamRefusesIt()
    {
        var (_, ledger, _) = NewLedger();

        Assert.Equal(XpGrantState.RefusedNotPositive, ledger.Grant(0, "retake").State);
        Assert.Equal(XpGrantState.RefusedNotPositive, ledger.Grant(-500, "test").State);
        Assert.Equal(1, ledger.Level);
        Assert.Equal(0, ledger.XpIntoLevel);
    }

    /// <summary>
    /// <b>A LEVEL THE LEDGER CANNOT KNOW ANSWERS UNKNOWN, NEVER 1.</b> Two degraded loads, and the
    /// record survives both.
    ///
    /// <para>A document from a newer build: writes are disabled, the level is <c>null</c>, the grant
    /// is refused by state rather than by exception, and the FILE IS BYTE-IDENTICAL afterwards.
    /// A document that cannot be parsed at all: the store quarantines it, and the user's real record
    /// is still readable in the backup — which is exactly what a grant of 1 written over the top
    /// would have made pointless.</para>
    /// </summary>
    [Fact]
    public void AnUnreadableLedger_AnswersUnknown_AndRefusesToBankOverTheRecord()
    {
        const string fromTheFuture =
            """{ "schemaVersion": 99, "level": 42, "xp": 1234.5, "highestLevelEver": 47 }""";
        var newerDir = SeedOnDisk(fromTheFuture);
        var (newerStore, newerLedger, newerPath) = NewLedger(newerDir);

        Assert.IsType<LoadOutcome.NewerSchema>(newerStore.LastLoadOutcome);
        Assert.False(newerLedger.Known);
        Assert.Null(newerLedger.Level);                   // NOT 1, and NOT 0
        Assert.Null(newerLedger.XpIntoLevel);
        Assert.Null(newerLedger.XpForNextLevel);
        Assert.Null(newerLedger.HighestLevelEver);

        var refused = newerLedger.Grant(5000, "test");
        Assert.Equal(XpGrantState.RefusedLedgerUnknown, refused.State);
        Assert.Null(refused.LevelBefore);
        Assert.Null(refused.LevelAfter);
        Assert.False(refused.LeveledUp);
        Assert.Equal(fromTheFuture, File.ReadAllText(newerPath));

        // Unparseable: the store moves it aside, and the level is unknown rather than reset.
        var brokenDir = SeedOnDisk("""{ "level": 42, this is not json""");
        var (brokenStore, brokenLedger, _) = NewLedger(brokenDir);

        var quarantined = Assert.IsType<LoadOutcome.Quarantined>(brokenStore.LastLoadOutcome);
        Assert.False(brokenLedger.Known);
        Assert.Null(brokenLedger.Level);
        Assert.Equal(XpGrantState.RefusedLedgerUnknown, brokenLedger.Grant(5000, "test").State);
        Assert.Contains("\"level\": 42", File.ReadAllText(quarantined.BackupPath), StringComparison.Ordinal);
    }

    /// <summary>
    /// The document's own setters are the last line of defence against a hand edit, because the
    /// values reach it through JSON binding and never through <see cref="ProgressionLedger.Grant"/>.
    /// A level of 0 or -3 would otherwise be the level a spend loop starts from, and a NaN <c>xp</c>
    /// would be the bank it adds to.
    /// </summary>
    [Fact]
    public async Task AHandEditedDocument_IsClampedOnLoad_RatherThanTrusted()
    {
        var (_, ledger, _) = NewLedger(SeedOnDisk(
            """{ "level": -3, "xp": -999, "highestLevelEver": -7 }"""));

        Assert.True(ledger.Known);
        Assert.Equal(XpCurve.FirstLevel, ledger.Level);
        Assert.Equal(0, ledger.XpIntoLevel);
        Assert.Equal(0, ledger.HighestLevelEver);

        var (_, past, _) = NewLedger(SeedOnDisk("""{ "level": 99999 }"""));
        Assert.Equal(XpCurve.MaxLevel, past.Level);

        // Unknown members survive the round trip (contract §6) — a future build's field is not
        // deleted by this one merely because a class was finished on it.
        var (store, keeper, path) = NewLedger(SeedOnDisk("""{ "level": 5, "seasonPeakLevel": 61 }"""));
        Assert.True(keeper.Grant(100, "test").Banked);
        Assert.IsType<OperationOutcome.Completed>(await store.SaveImmediate());
        Assert.Equal(61, JsonDocument.Parse(File.ReadAllText(path)).RootElement
            .GetProperty("seasonPeakLevel").GetInt32());
    }

    // ==================================================================================
    // The four level gates.
    // ==================================================================================

    /// <summary>
    /// <b>THE GATES REFUSE NOBODY, AND THAT IS THE PARITY OUTCOME — verified against the shipping
    /// bytes, not asserted.</b>
    ///
    /// <para>Every level gate in the shipping app funnels through <c>AppSettings.IsLevelUnlocked</c>
    /// (<c>Models/AppSettings.cs:5439</c>), whose entire body is <c>return true;</c> under the
    /// comment <i>"Feature level gating has been removed — every feature is available from level
    /// 1"</i>. Making these bite in the port would take four features away from every user below
    /// 70/50/35/75 that the shipping app hands them at level 1 — a regression dressed as a port.
    /// This fact reds on someone "making the gates real" by guess, and it also reds if upstream ever
    /// puts a body back, which is the day the decision genuinely changes.</para>
    /// </summary>
    [Fact]
    public void TheFourLevelGates_RefuseNobody_BecauseUpstreamDeletedFeatureLevelGating()
    {
        foreach (var required in new[]
                 {
                     LevelUnlocks.BrainDrain, LevelUnlocks.BubbleCount,
                     LevelUnlocks.LockCard, LevelUnlocks.MindWipe,
                 })
        {
            Assert.True(LevelUnlocks.IsUnlocked(required));
        }

        Assert.Equal(70, LevelUnlocks.BrainDrain);
        Assert.Equal(50, LevelUnlocks.BubbleCount);
        Assert.Equal(35, LevelUnlocks.LockCard);
        Assert.Equal(75, LevelUnlocks.MindWipe);

        var settings = ReadRepoFile("ConditioningControlPanel/Models/AppSettings.cs");
        var body = ExtractOne(
            settings,
            @"public bool IsLevelUnlocked\(int requiredLevel\)\s*\{(.*?)\}",
            RegexOptions.Singleline);
        Assert.Equal("return true;", body.Trim());
        Assert.Contains("Feature level gating has been removed", settings, StringComparison.Ordinal);

        // Brain Drain's is the ONE requirement still written as a call (:208). The other three exist
        // only as prose, which is why two of them are pinned as prose and the Lock Card's 35 is
        // pinned as absent — there is no statement of it anywhere in the shipping tree.
        Assert.Contains(
            $"IsLevelUnlocked({LevelUnlocks.BrainDrain})",
            ReadRepoFile("ConditioningControlPanel/Services/LockCard/BrainDrainService.cs"),
            StringComparison.Ordinal);
        Assert.Contains(
            $"Unlocks at Level {LevelUnlocks.BubbleCount}",
            ReadRepoFile("ConditioningControlPanel/Services/BubbleCountService.cs"),
            StringComparison.Ordinal);
        Assert.Contains(
            $"Unlockable at level {LevelUnlocks.MindWipe}",
            ReadRepoFile("ConditioningControlPanel/Services/LockCard/MindWipeService.cs"),
            StringComparison.Ordinal);
    }

    // ==================================================================================
    // The three payouts that used to be thrown away.
    // ==================================================================================

    /// <summary>
    /// The descent's run-ended payout banks (<c>DtrhHostService.cs:603</c>), and it banks
    /// <c>baseXp</c> — NOT <c>finalXp</c>, because upstream's <c>AddXP</c> applies the skill
    /// multiplier itself (<c>ProgressionService.cs:75-77</c>) and <c>finalXp</c> is the payout
    /// frame's display figure. A dry run banks nothing, inside the same <c>!_testMode</c> block
    /// upstream puts the grant in (<c>:601</c>).
    /// </summary>
    [Fact]
    public void TheDescentPayout_BanksBaseXp_AndADryRunBanksNothing()
    {
        var (_, ledger, _) = NewLedger();
        var slots = NewSlots();
        var stats = new DtrhAssetStats(slots.AssetStatsStore, _log.Add);

        // The m2test.js payout case: capBase = 250 × 3 min × 1.0 → baseXp 750 (score is over cap).
        const string run =
            """{"score":12000,"durationSec":180,"elapsedSec":180,"difficultyMult":1.0,"sparkGainMult":1.0}""";

        var live = new DtrhMeta(slots.StoreFor(slots.ActiveSlot), slots.IndexStore, stats,
            _ => { }, _log.Add, testMode: false, slots.SlotFilePath(slots.ActiveSlot), xp: ledger);
        var payout = live.OnRunEnded(JsonDocument.Parse(run).RootElement.Clone());

        Assert.Equal(750, payout.BaseXp);
        Assert.Equal(payout.BaseXp, payout.FinalXp);      // skillMult 1.0 — the two are equal here…
        Assert.Equal(750, ledger.XpIntoLevel);            // …and THIS is which one was banked.
        Assert.Equal(1, ledger.Level);                    // 750 < 800, so no level yet.

        var dry = new DtrhMeta(slots.StoreFor(slots.ActiveSlot), slots.IndexStore, stats,
            _ => { }, _log.Add, testMode: true, slots.SlotFilePath(slots.ActiveSlot), xp: ledger);
        var dryPayout = dry.OnRunEnded(JsonDocument.Parse(run).RootElement.Clone());

        Assert.True(dryPayout.DryRun);
        Assert.Equal(750, ledger.XpIntoLevel);            // unchanged: a test run credits nothing
    }

    /// <summary>
    /// A finished class banks its payout BEFORE the <c>payout-result</c> frame goes out, which is
    /// upstream's order (<c>ArcademyHostService.cs:1390</c> level-before, <c>:1396</c> grant,
    /// <c>:1399</c> level-after, <c>:1410-1416</c> frame) — and that order is the whole reason the
    /// frame's <c>levelUp</c> can be a real comparison instead of a constant.
    ///
    /// <para>Move the grant below the post and the frame's <c>levelUp</c> goes false while the level
    /// still moves: this fact reds on exactly that reorder.</para>
    /// </summary>
    [Fact]
    public void AFinishedClass_BanksItsPayout_BeforeTheFrameReportsTheLevelUp()
    {
        var (_, ledger, _) = NewLedger();
        Assert.True(ledger.Grant(700, "seed").Banked);    // 100 short of L1's 800

        var posted = new List<object>();
        var settings = NewArcademySettings();
        using var session = new ArcademySession(
            settings, new ArcademyAppFacts(), posted.Add, new SinkAdapter(_log), meta: null, xp: ledger);

        // Tier 4 grade S + the capped flavour bonus: 110 × 1.5 + 15 = 180. 700 + 180 = 880 ≥ 800.
        session.ClassEnd(JsonDocument.Parse(
            """{"gameKey":"the-deep-end","gradeTier":4,"grade":"S","flavorXp":15}""").RootElement.Clone());

        Assert.Equal(2, ledger.Level);
        Assert.Equal(80, ledger.XpIntoLevel);             // 880 − 800

        var frame = JsonDocument.Parse(ArcademyProtocol.SerializeForPage(posted[^1])).RootElement;
        Assert.Equal("payout-result", frame.GetProperty("type").GetString());
        Assert.Equal(180.0, frame.GetProperty("xp").GetDouble());
        Assert.True(frame.GetProperty("levelUp").GetBoolean());

        // A retake pays 0 on the same UTC day upstream; with no meta store there is no ledger day to
        // claim, so the second class here pays again — what this half pins is that a session with NO
        // XP ledger still computes, still answers the page, and says which of the two it did.
        var noLedger = new ArcademySession(
            settings, new ArcademyAppFacts(), posted.Add, new SinkAdapter(_log));
        noLedger.ClassEnd(JsonDocument.Parse("""{"gameKey":"x","gradeTier":1,"grade":"C"}""").RootElement.Clone());
        var unbanked = JsonDocument.Parse(ArcademyProtocol.SerializeForPage(posted[^1])).RootElement;
        Assert.Equal(24.0, unbanked.GetProperty("xp").GetDouble());   // 40 × 0.6
        Assert.False(unbanked.GetProperty("levelUp").GetBoolean());
        Assert.Contains(_log, line => line.Contains(ArcademySession.NoLedgerReason, StringComparison.Ordinal));
        noLedger.Dispose();
    }

    /// <summary>
    /// A SOURCE-LEVEL CHOKEPOINT PIN, named for exactly what it is. Every other fact in this file
    /// would stay green if the grant were deleted from <c>OnQuizResult</c>, because the window is not
    /// constructible in a pure-logic project. This reds on that deletion — and it is NOT evidence
    /// that the window runs, renders, or is ever reached.
    /// </summary>
    [Fact]
    public void TheIntakeCompletionPath_BanksItsXp_AtItsOwnSeam()
    {
        var window = ReadRepoFile("client/src/CcpClient.Desktop/Features/Intake/IntakeHostWindow.axaml.cs");

        Assert.Contains("_context.Progression.Grant(xp, \"intake completion\")", window, StringComparison.Ordinal);
        Assert.True(
            window.IndexOf("_context.Progression.Grant(xp,", StringComparison.Ordinal)
            < window.IndexOf("_context.Pass.ConsumeForCompletedIntake();", StringComparison.Ordinal),
            "the XP is granted BEFORE the pass spend, which is upstream's order (:446 then :465) — a "
            + "reorder would change which side of a mid-completion crash the grant falls on");

        // And the amount is the one the draft stamps, so the two can never disagree.
        Assert.Contains("var xp = IntakeDraft.ComputeCompletionXp(run);", window, StringComparison.Ordinal);
        Assert.Contains(
            "App.Progression?.AddXP(Math.Min(xp, 100), XPSource.Other)",
            ReadRepoFile("ConditioningControlPanel/Services/Quiz/IntakeHostService.cs"),
            StringComparison.Ordinal);
    }

    // ==================================================================================
    // Harness.
    // ==================================================================================

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

    private static string ExtractOne(string text, string pattern, RegexOptions options = RegexOptions.None)
    {
        var match = Regex.Match(text, pattern, options);
        Assert.True(match.Success, $"the shipping source no longer matches /{pattern}/ — the port's constant is now unanchored");
        return match.Groups[1].Value;
    }
}
