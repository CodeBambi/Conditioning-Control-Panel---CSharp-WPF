using CcpClient.Desktop.Features.Progression;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// THE LEVEL, AS A SURFACE IS ALLOWED TO SAY IT. Every fact here fails against a fabrication rather
/// than against a crash, which is the shape <c>TrainerCardTests</c> set for the award record beside
/// it: a level invented for a ledger nobody could read
/// (<see cref="AnUnreadableLedger_SaysSo_AndNeverRendersALevel"/>), an XP readout rounded where
/// upstream truncates (<see cref="TheLedgersNumbers_ReachTheStrings_AndTheXpLineTruncatesLikeUpstream"/>),
/// a bar drawn past the end of its track
/// (<see cref="ABankBiggerThanTheLevel_FillsTheBarAndNoMore_BecauseAPassiveReadNeverSpends"/>), a
/// rank band moved off upstream's edge (<see cref="TheFourRankBands_AreUpstreamsOwn_AtTheirEdges"/>),
/// and a passive read that quarantines the user's ledger on the way to the screen
/// (<see cref="ReadingTheLevel_LeavesTheLedgerAndItsStaleTempExactlyWhereTheyWere"/>).
///
/// <para><b>THE NUMBERS IN THESE ASSERTIONS ARE HAND-DERIVED FROM UPSTREAM'S BAND, NOT READ BACK
/// FROM <see cref="XpCurve"/>.</b> Level 1 costs 800 and level 3 costs 843 because upstream's first
/// band is <c>Math.Round(800 + (level - 1) * (1700.0 / 79))</c>
/// (<c>Services/Progression/ProgressionService.cs:301-305</c>) and 1700/79 = 21.5189…, so
/// 800 + 2 x 21.5189… = 843.038 → 843. Asserting <c>XpCurve.XpForLevel(3)</c> instead would pass
/// against any curve at all, including a wrong one.</para>
///
/// <para><b>What these facts do NOT prove.</b> Pure logic over temp files. Nothing here renders,
/// lays out, composites or measures anything: the star-column bar, the collapsed track and the
/// mounted page are <c>TrainerCardLevelHeadlessTests</c>, which is draw-level, and the pixels a user
/// sees are the headed <c>trainer-card-level</c> capture. The
/// <see cref="TrainerCardLevel.UnknownIoFailure"/> arm is NOT exercised: forcing a read failure
/// needs either a share-mode lock or a permission bit, and neither behaves the same on Windows and
/// Linux — an unreachable-on-one-platform fact would be worse evidence than this sentence.</para>
/// </summary>
public sealed class TrainerCardLevelTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "ccp-trainer-level-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ } }

    /// <summary>A ledger written straight to disk — the shape another build, or a hand edit, leaves
    /// behind. Returns the file the card reads.</summary>
    private string SeedOnDisk(string json)
    {
        var dir = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, ProgressionDocument.FileName);
        File.WriteAllText(path, json);
        return path;
    }

    /// <summary>A directory with no ledger in it at all.</summary>
    private string EmptyDirectory()
    {
        var dir = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, ProgressionDocument.FileName);
    }

    [Fact]
    public void AMissingLedger_ReadsAsTheFirstLevel_AndIsTheOnePlaceAOneIsHonest()
    {
        // No file is an ANSWER, and it is the same answer the LEDGER gives: LoadOutcome.Missing is
        // not degraded (Persistence/PersistenceStore.cs:24-27, :351), so ProgressionLedger.Known is
        // true and its Level is the document's default. A fresh subject really does stand at
        // upstream's level 1 (Models/AppSettings.cs:237, `private int _playerLevel = 1;`) with
        // nothing into it. Rendering that as "could not tell" would be the mirror of the lie this
        // card exists to avoid.
        var level = TrainerCardLevel.Read(EmptyDirectory());

        Assert.Equal(TrainerCardLevelState.Known, level.State);
        Assert.Equal(1, level.Level);
        Assert.Equal("LVL 1", level.LevelLine);
        Assert.Equal("0 / 800 XP", level.XpLine);          // ProgressionService.cs:301-305 at level 1
        Assert.Equal(0.0, level.Fill);
        Assert.Equal(string.Empty, level.Note);
    }

    [Fact]
    public void TheLedgersNumbers_ReachTheStrings_AndTheXpLineTruncatesLikeUpstream()
    {
        // 799.6 is chosen so that TRUNCATION and ROUNDING give different sentences: upstream writes
        // $"{(int)xp} / {(int)xpNeeded} XP" (MainWindow/MainWindow.ChromeFx.cs:814), and the cast
        // floors. A port that "tidied" that to Math.Round would put 800 / 843 in front of a user
        // who has not banked 800 — a level-up that has not happened.
        var level = TrainerCardLevel.Read(SeedOnDisk("""{ "level": 3, "xp": 799.6 }"""));

        Assert.Equal(TrainerCardLevelState.Known, level.State);
        Assert.Equal("LVL 3", level.LevelLine);            // MainWindow.UiUpdates.cs:59
        Assert.Equal("799 / 843 XP", level.XpLine);
        Assert.Equal("BASIC BIMBO", level.RankLine);       // MainWindow.UiUpdates.cs:71
        Assert.Equal(799.6 / 843, level.Fill!.Value, 10);  // MainWindow.ChromeFx.cs:826
        Assert.Equal(string.Empty, level.Note);
    }

    [Fact]
    public void TheFourRankBands_AreUpstreamsOwn_AtTheirEdges()
    {
        // MainWindow/MainWindow.UiUpdates.cs:70-76, band for band. The EDGES are the assertion: a
        // `< 20` quietly widened to `<= 20` renames level 20, and every band boundary is a place a
        // port can drift one step without anything else noticing. The strings are literals here on
        // purpose — asserting TrainerCardLevel.RankUnder20 against itself would pass with any text
        // at all in the constant.
        Assert.Equal("BASIC BIMBO", TrainerCardLevel.RankFor(1));
        Assert.Equal("BASIC BIMBO", TrainerCardLevel.RankFor(19));
        Assert.Equal("DUMB AIRHEAD", TrainerCardLevel.RankFor(20));
        Assert.Equal("DUMB AIRHEAD", TrainerCardLevel.RankFor(49));
        Assert.Equal("SYNTHETIC BLOWDOLL", TrainerCardLevel.RankFor(50));
        Assert.Equal("SYNTHETIC BLOWDOLL", TrainerCardLevel.RankFor(99));
        Assert.Equal("PERFECT FUCKPUPPET", TrainerCardLevel.RankFor(100));
        Assert.Equal("PERFECT FUCKPUPPET", TrainerCardLevel.RankFor(XpCurve.MaxLevel));

        // Four bands, four DISTINCT titles: a copy-paste that gave two bands the same string would
        // silently retire one of upstream's four ranks.
        Assert.Equal(4, new[] { 1, 20, 50, 100 }.Select(TrainerCardLevel.RankFor).Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("{ not json", "is not valid JSON")]
    [InlineData("[]", "is not valid JSON")]
    [InlineData("""{ "schemaVersion": 99, "level": 40 }""", "written by a newer build")]
    public void AnUnreadableLedger_SaysSo_AndNeverRendersALevel(string seeded, string expectedReason)
    {
        // THE HONESTY BAR, and the failure it forbids is specific: a level rendered as 1 for someone
        // standing at 40 is not merely uninformative, it is a claim about them. Every number is
        // null, so nothing downstream can draw a bar or a rank out of it — and the schemaVersion 99
        // case carries a real level 40 in the same bytes precisely so that "read the half you
        // recognise" would PASS if it were the behaviour.
        var level = TrainerCardLevel.Read(SeedOnDisk(seeded));

        Assert.Equal(TrainerCardLevelState.Unknown, level.State);
        Assert.Null(level.Level);
        Assert.Null(level.XpIntoLevel);
        Assert.Null(level.XpForNextLevel);
        Assert.Null(level.Fill);

        Assert.Equal("Level unknown", level.LevelLine);
        Assert.NotEqual("LVL 1", level.LevelLine);
        Assert.NotEqual("LVL 40", level.LevelLine);
        Assert.Equal(string.Empty, level.RankLine);
        Assert.Equal(string.Empty, level.XpLine);

        // It names the FILE and never the path — the port's display rule — and it says which of the
        // three things went wrong rather than one blanket apology.
        Assert.Contains(ProgressionDocument.FileName, level.Note, StringComparison.Ordinal);
        Assert.Contains(expectedReason, level.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void ABankBiggerThanTheLevel_FillsTheBarAndNoMore_BecauseAPassiveReadNeverSpends()
    {
        // 2000 into a level that costs 800 is a state the LEDGER never leaves behind — its Grant
        // drains the bank into levels before it writes (ProgressionLedger.Grant -> XpCurve.Spend ->
        // ProgressionService.cs:212-223). A hand-edited file can hold it, and a passive read must
        // not spend it on the user's behalf: this card reports what is on disk.
        //
        // So the fill is upstream's own Math.Min(1.0, ...) (MainWindow/MainWindow.ChromeFx.cs:826)
        // and it BITES here. Without the clamp the fraction is 2.5, and the bar's two star columns
        // would be 2.5 and -1.5 — a fill drawn past the end of its own track.
        var level = TrainerCardLevel.Read(SeedOnDisk("""{ "level": 1, "xp": 2000 }"""));

        Assert.Equal(1, level.Level);
        Assert.Equal(2000.0, level.XpIntoLevel);
        Assert.Equal(1.0, level.Fill);
        Assert.Equal("2000 / 800 XP", level.XpLine);
    }

    [Theory]
    [InlineData("""{ "level": 0, "xp": -5 }""", 1)]
    [InlineData("""{ "level": -12 }""", 1)]
    [InlineData("""{ "level": 9999 }""", 500)]
    public void AHandEditedLedger_CannotPutALevelZeroOrANaNOnTheScreen(string seeded, int expected)
    {
        // The clamps live on ProgressionDocument's setters, and this fact is that the CARD inherits
        // them rather than re-deriving its own: a passive reader that bound the raw JSON would print
        // "LVL 0" for a file the ledger itself would refuse to start from. 500 is XpCurve.MaxLevel,
        // upstream's ProgressionService.MaxDerivableLevel (:27).
        var level = TrainerCardLevel.Read(SeedOnDisk(seeded));

        Assert.Equal(TrainerCardLevelState.Known, level.State);
        Assert.Equal(expected, level.Level);
        Assert.Equal($"LVL {expected}", level.LevelLine);

        // And whatever the bank said, the bar is a real fraction rather than a NaN or a negative.
        Assert.NotNull(level.Fill);
        Assert.InRange(level.Fill.Value, 0.0, 1.0);
    }

    [Fact]
    public void ANonFiniteBank_ReadsAsNothingBanked_RatherThanPoisoningTheBar()
    {
        // JSON has no NaN literal, so the way a non-finite bank actually reaches this file is a
        // string that binds to double.NaN through the document's own converter path — or, as here,
        // a value the setter rejects. What matters on screen is that the fraction is never NaN: a
        // NaN star weight is a layout exception, not a wrong number.
        var level = TrainerCardLevel.Read(SeedOnDisk("""{ "level": 2, "xp": -0.0001 }"""));

        Assert.Equal(0.0, level.XpIntoLevel);
        Assert.Equal(0.0, level.Fill);
        Assert.False(double.IsNaN(level.Fill!.Value));
        Assert.Equal("0 / 822 XP", level.XpLine);          // 800 + 1 x 1700/79 = 821.519 -> 822
    }

    [Fact]
    public void ReadingTheLevel_LeavesTheLedgerAndItsStaleTempExactlyWhereTheyWere()
    {
        // THE REASON THIS DOES NOT USE ProgressionLedger.Open. That opens a PersistenceStore, whose
        // load adopts an orphaned temp, deletes a stale one and QUARANTINES a document it cannot
        // bind (PersistenceStore.cs:322-430). For the ledger's OWNER those are right. For a card
        // being looked at they are how a user who had a level loses it: the quarantine renames the
        // record and the next grant writes level 1 over the gap.
        var path = SeedOnDisk("{ corrupt");
        var directory = Path.GetDirectoryName(path)!;
        var temp = path + ".tmp";
        File.WriteAllText(temp, """{ "level": 40, "xp": 100 }""");

        var before = Directory.GetFiles(directory)
            .ToDictionary(f => Path.GetFileName(f), File.ReadAllText, StringComparer.Ordinal);

        var level = TrainerCardLevel.Read(path);
        Assert.Equal(TrainerCardLevelState.Unknown, level.State);

        var after = Directory.GetFiles(directory)
            .ToDictionary(f => Path.GetFileName(f), File.ReadAllText, StringComparer.Ordinal);

        Assert.Equal(
            before.Keys.OrderBy(k => k, StringComparer.Ordinal),
            after.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(before[ProgressionDocument.FileName], after[ProgressionDocument.FileName]);
        Assert.Equal(before[Path.GetFileName(temp)], after[Path.GetFileName(temp)]);
    }

    [Fact]
    public void ThePage_FormatsNoLevelOfItsOwn_SoItCannotDriftFromUpstreamsWording()
    {
        // A SOURCE-level chokepoint, in the shape TrainerCardTests already uses for the sharing
        // absence. The risk is concrete and cheap to close: the next edit to the page writes
        // $"LVL {n}" or "{x} / {y} XP" inline, and upstream's truncation, its band and its clamp
        // stop being the thing on screen. Every string in the level block comes from
        // TrainerCardLevel, which is where the citations and the tests are.
        var page = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "client", "src", "CcpClient.Desktop", "Views", "Pages", "IntakePage.axaml.cs"));

        foreach (var formatted in new[] { "LVL ", " XP\"", "Math.Min", "XpForLevel", "RankFor" })
        {
            Assert.DoesNotContain(formatted, page, StringComparison.Ordinal);
        }

        // And the page really does read the model — an absence proves nothing on its own.
        Assert.Contains("level.LevelLine", page, StringComparison.Ordinal);
        Assert.Contains("level.XpLine", page, StringComparison.Ordinal);
        Assert.Contains("level.Fill", page, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "client", "CcpClient.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("the repository root (client/CcpClient.sln) was not found above the test binary");
    }
}
