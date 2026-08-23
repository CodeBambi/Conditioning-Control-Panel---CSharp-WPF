using System.Text.RegularExpressions;
using CcpClient.Desktop.Features.Intake;
using CcpClient.Desktop.Features.Progression;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The Trainer Card — what this port can honestly put in front of a user about its own
/// progression, and every place it has to say it does not know.
///
/// <para><b>THE SHAPE OF EVERY FACT HERE IS AN INVERSION.</b> The card's whole value is that it
/// refuses to fabricate, so each fact below fails against the fabrication it forbids, not merely
/// against a crash: an unreadable record rendered as "not earned"
/// (<see cref="AnUnreadableRecord_SaysSo_AndIsNeverRenderedAsNotEarned"/>), a newer-schema record
/// half-read (<see cref="ARecordFromANewerBuild_IsRefused_RatherThanPartlyRead"/>), a zero standing
/// in for an uncounted total (<see cref="TeachersPet_ShowsNoProgressNumber_BecauseNothingCountsPasses"/>),
/// a tier claimed on the user's behalf (<see cref="TheCard_ClaimsNoTier_AndNamesWhyItCannot"/>), a
/// sharing control appearing on the page
/// (<see cref="TheIntakePage_CarriesNoSharingOrExportControl"/>), and a passive read growing a
/// side effect on the user's file
/// (<see cref="ReadingTheCard_LeavesTheRecordAndItsStaleTempExactlyWhereTheyWere"/>).</para>
///
/// <para><b>What these facts do NOT prove.</b> Pure logic over temp files and over the product's
/// own source text. Nothing here renders, composites, lays out, takes input or opens a window —
/// <c>TrainerCardHeadlessTests</c> covers the visual tree, and neither suite is headed evidence.
/// <see cref="TheIntakePage_CarriesNoSharingOrExportControl"/> and
/// <see cref="TheCardsProductSource_NeitherWritesNorTransmits"/> are SOURCE-level chokepoint pins in
/// the shape <c>GradedRunAwardsTests</c> already uses: they prove what the product text does and
/// does not contain, and nothing about what runs.</para>
/// </summary>
public sealed class TrainerCardTests : IDisposable
{
    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];

    private static readonly string[] PageParts =
        ["client", "src", "CcpClient.Desktop", "Views", "Pages", "IntakePage.axaml"];

    private static readonly string[] CardParts =
        ["client", "src", "CcpClient.Desktop", "Features", "Progression", "TrainerCard.cs"];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "ccp-trainer-card-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _log = [];

    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ } }

    private sealed class SinkAdapter(List<string> lines) : ILogSink
    {
        public void Log(string message) => lines.Add(message);
    }

    /// <summary>A record written straight to disk — the shape another build, or a hand edit,
    /// leaves behind. Returns the file the card reads.</summary>
    private string SeedOnDisk(string json)
    {
        var dir = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, GradedRunAwardsDocument.FileName);
        File.WriteAllText(path, json);
        return path;
    }

    private static TrainerCardAward Row(TrainerCard card, string id) =>
        card.Awards.Single(a => a.Id == id);

    // ---------- what the record can and cannot say ----------

    [Fact]
    public void AMissingRecord_ReadsAsNothingEarned_AndNotAsUnknown()
    {
        // No file is an ANSWER: the ledger writes on the first thing it has to record
        // (GradedRunAwards.cs:260-263), so its absence means nothing was ever earned. Rendering
        // that as "could not tell" would be the mirror of the lie this card exists to avoid.
        var card = TrainerCard.Read(Path.Combine(_root, "never-run", GradedRunAwardsDocument.FileName));

        Assert.Equal(TrainerCardRecordState.NoRunsYet, card.Record);
        Assert.Equal(TrainerCard.NoRunsYetNote, card.RecordNote);
        Assert.Equal(0, card.ClearedCategories);
        Assert.Equal(TrainerCardAwardState.NotEarnedYet, Row(card, GradedRunAwards.TopOfTheClassId).State);
        Assert.Equal(TrainerCardAwardState.NotEarnedYet, Row(card, GradedRunAwards.HonorRollId).State);
    }

    [Fact]
    public async Task AGradedRunRecordedByTheRealLedger_ReadsBackAsEarned()
    {
        // THE FACT THAT CATCHES A DRIFT NO HAND-WRITTEN JSON EVER COULD. Every other fact here
        // seeds the file itself, so all of them would still pass if the card's reader and the
        // store's writer disagreed about property naming and the card silently showed an empty
        // record forever. This one writes through the REAL ledger on the REAL store and reads it
        // back through the card.
        var dir = Path.Combine(_root, "real-ledger");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, GradedRunAwardsDocument.FileName);

        var store = new PersistenceStore<GradedRunAwardsDocument>(
            new OperationRegistry().OwnerFor("IntakeGradedRunAwards"),
            new SinkAdapter(_log),
            path,
            GradedRunAwardsDocument.CurrentSchemaVersion);
        await store.StartAsync(TestContext.Current.CancellationToken);
        var awards = new GradedRunAwards(store, _log.Add);

        awards.RecordGradedRun(topMarks: true, category: "bambi");
        awards.RecordGradedRun(topMarks: true, category: "sissy");
        await store.SaveImmediate();
        await store.StopAsync();

        var card = TrainerCard.Read(path);

        Assert.Equal(TrainerCardRecordState.Read, card.Record);
        Assert.Equal(string.Empty, card.RecordNote);
        Assert.Equal(TrainerCardAwardState.Earned, Row(card, GradedRunAwards.TopOfTheClassId).State);
        Assert.Equal(TrainerCard.EarnedStatus, Row(card, GradedRunAwards.TopOfTheClassId).Status);
        Assert.Equal(2, card.ClearedCategories);
        Assert.Equal(TrainerCardAwardState.NotEarnedYet, Row(card, GradedRunAwards.HonorRollId).State);
    }

    [Fact]
    public void AnUnreadableRecord_SaysSo_AndIsNeverRenderedAsNotEarned()
    {
        var card = TrainerCard.Read(SeedOnDisk("{ this is not json"));

        Assert.Equal(TrainerCardRecordState.Unreadable, card.Record);
        Assert.Contains(GradedRunAwardsDocument.FileName, card.RecordNote, StringComparison.Ordinal);
        Assert.Contains("not valid JSON", card.RecordNote, StringComparison.Ordinal);

        // Null, not zero: a count the card could not read must not arrive at a surface as a number.
        Assert.Null(card.ClearedCategories);

        foreach (var id in new[] { GradedRunAwards.TopOfTheClassId, GradedRunAwards.HonorRollId })
        {
            Assert.Equal(TrainerCardAwardState.Unknown, Row(card, id).State);
            Assert.Equal(TrainerCard.UnknownStatus, Row(card, id).Status);
            Assert.NotEqual(TrainerCard.NotEarnedStatus, Row(card, id).Status);
        }
    }

    [Fact]
    public void ARecordFromANewerBuild_IsRefused_RatherThanPartlyRead()
    {
        // The document binds perfectly well; only its schemaVersion says a later build wrote it.
        // Reading the half this build recognises would report an award list that may be wrong in
        // either direction, so the card refuses the whole document — the same posture as the
        // store's LoadOutcome.NewerSchema, minus the write-disable only an owner needs.
        var card = TrainerCard.Read(SeedOnDisk(
            $$"""
            {
              "{{PersistenceStore<GradedRunAwardsDocument>.SchemaVersionKey}}": {{GradedRunAwardsDocument.CurrentSchemaVersion + 1}},
              "awardedIds": ["top_of_the_class", "honor_roll"],
              "perfectedCategories": ["bambi", "sissy", "drone"]
            }
            """));

        Assert.Equal(TrainerCardRecordState.Unreadable, card.Record);
        Assert.Contains("newer build", card.RecordNote, StringComparison.Ordinal);
        Assert.Null(card.ClearedCategories);
        Assert.Equal(TrainerCardAwardState.Unknown, Row(card, GradedRunAwards.TopOfTheClassId).State);
        Assert.Equal(TrainerCardAwardState.Unknown, Row(card, GradedRunAwards.HonorRollId).State);
    }

    [Fact]
    public void TheTwoRowsThisBuildCannotEarn_KeepTheirAnswer_WhenTheRecordCannotBeRead()
    {
        // "This build counts no passes" and "the intake has no fail state" are properties of the
        // BUILD. An unreadable file says nothing about either, so collapsing them into Unknown
        // with the other two would lose a true answer to a failure that did not touch it.
        var card = TrainerCard.Read(SeedOnDisk("not json at all"));

        Assert.Equal(TrainerCardAwardState.NotTracked, Row(card, TrainerCard.TeachersPetId).State);
        Assert.Equal(TrainerCard.TeachersPetStatus, Row(card, TrainerCard.TeachersPetId).Status);
        Assert.Equal(TrainerCardAwardState.CannotBeEarnedHere, Row(card, TrainerCard.HeldBackId).State);
        Assert.Equal(TrainerCard.HeldBackStatus, Row(card, TrainerCard.HeldBackId).Status);
    }

    [Fact]
    public void TeachersPet_ShowsNoProgressNumber_BecauseNothingCountsPasses()
    {
        // Upstream counts ProgressionData.QuizzesPassed toward 25 (GamificationBridge.cs:41,586-589)
        // and this port keeps no such counter (census §4 C10/C13). "0 of 25" would be a score the
        // app never computed, so the row carries no digits at all.
        var status = Row(TrainerCard.Read(SeedOnDisk("{}")), TrainerCard.TeachersPetId).Status;

        Assert.DoesNotContain(status, char.IsDigit);
        Assert.Contains("counts no passed runs", status, StringComparison.Ordinal);
        Assert.Contains("never awards it", status, StringComparison.Ordinal);
    }

    [Fact]
    public void HonorRollProgress_CountsAgainstThePortsOwnThreshold()
    {
        var card = TrainerCard.Read(SeedOnDisk("""{ "perfectedCategories": ["bambi", "sissy"] }"""));

        Assert.Equal(2, card.ClearedCategories);
        Assert.Contains(
            $"2 of {GradedRunAwards.HonorRollCategories} categories cleared",
            Row(card, GradedRunAwards.HonorRollId).Status,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ADuplicateCasedCategory_CountsOnce_OnTheCardsOwnReadPath()
    {
        // §5.2's defect, on the card's side of the glass. The card does not go through
        // GradedRunAwards.NormalizeCategory — it binds straight off disk — so the only thing that
        // can collapse these two entries is the document's named comparer surviving JSON binding.
        // Rendering "2 of 3" here would show a user a third of an award they have not earned.
        var card = TrainerCard.Read(SeedOnDisk("""{ "perfectedCategories": ["sissy", "Sissy", "SISSY"] }"""));

        Assert.Equal(1, card.ClearedCategories);
        Assert.Contains($"1 of {GradedRunAwards.HonorRollCategories}", Row(card, GradedRunAwards.HonorRollId).Status,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AnIdThisBuildCannotEarn_StillShowsEarned_WhenTheRecordHoldsIt()
    {
        // The ledger preserves ids this build does not know (GradedRunAwards.cs:61-65, upstream's
        // own plain Contains at AchievementProgress.cs:212). A card that hid one would be editing
        // the user's record on the way to the screen.
        var card = TrainerCard.Read(SeedOnDisk("""{ "awardedIds": ["teachers_pet"] }"""));

        Assert.Equal(TrainerCardAwardState.Earned, Row(card, TrainerCard.TeachersPetId).State);
        Assert.Equal(TrainerCard.EarnedStatus, Row(card, TrainerCard.TeachersPetId).Status);
    }

    // ---------- the words, held to what the build actually is ----------

    [Fact]
    public void TheRequirementLines_AgreeWithThePortsOwnNumbers()
    {
        // The four requirement strings are upstream's authored text (Models/Achievement.cs:666,686)
        // and they carry numbers. If the port's own bar or threshold ever moved, those sentences
        // would keep quoting the old one to a user — so both are re-derived here from the live
        // constants rather than trusted.
        Assert.Equal(
            IntakeGraded.TopMarksPercent.ToString("0", System.Globalization.CultureInfo.InvariantCulture),
            Regex.Match(TrainerCard.TopOfTheClassRequirement, @"\d+").Value);
        Assert.Equal(
            IntakeGraded.TopMarksPercent.ToString("0", System.Globalization.CultureInfo.InvariantCulture),
            Regex.Matches(TrainerCard.HonorRollRequirement, @"\d+")[0].Value);
        Assert.Equal(
            GradedRunAwards.HonorRollCategories.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Regex.Matches(TrainerCard.HonorRollRequirement, @"\d+")[1].Value);
    }

    [Fact]
    public void TheCard_ClaimsNoTier_AndNamesWhyItCannot()
    {
        // Divergence D228. All four ids are IsExclusive upstream (Models/Achievement.cs:670,680,690,700)
        // and are granted through TryUnlockExclusive behind App.Patreon?.HasPremiumAccess
        // (AchievementService.cs:1107,1116-1120). This port grants ungated because it has no
        // authority to ask, so the card must say that and must not wear the tier livery of a
        // surface that DID ask.
        Assert.Contains("no entitlement authority", TrainerCard.NoTierNote, StringComparison.Ordinal);
        Assert.Contains("claims no tier for you", TrainerCard.NoTierNote, StringComparison.Ordinal);

        var everythingTheCardCanSay = string.Join(
            '\n',
            TrainerCard.Title,
            TrainerCard.NoLevelNote,
            TrainerCard.NoPortraitNote,
            TrainerCard.NoTierNote,
            TrainerCard.LocalOnlyNote,
            TrainerCard.NoRunsYetNote,
            TrainerCard.UnknownStatus,
            TrainerCard.TeachersPetStatus,
            TrainerCard.HeldBackStatus);

        // The two things a surface says when it HAS an answer about entitlement — the port's own
        // refusal wording and WPF's tier badge — may never appear on a card that asked nobody.
        Assert.DoesNotContain("Tier 2", everythingTheCardCanSay, StringComparison.Ordinal);
        Assert.DoesNotContain(
            CcpClient.Desktop.Features.Dtrh.DtrhGate.TierBadgeWording,
            everythingTheCardCanSay,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheCard_SaysOutLoudThatItHasNoLevelAndNoPortrait()
    {
        // No XP, level, streak or rank exists anywhere in client/src (census §3 B1). The failure
        // this forbids is a card that renders the absence as an empty stat block or a zero.
        Assert.Contains("no level, XP, rank or streak", TrainerCard.NoLevelNote, StringComparison.Ordinal);
        Assert.Contains("no portrait, wardrobe or banner", TrainerCard.NoPortraitNote, StringComparison.Ordinal);
        Assert.Contains("stays on it", TrainerCard.LocalOnlyNote, StringComparison.Ordinal);
        Assert.Contains("no sharing, export, upload or publish path", TrainerCard.LocalOnlyNote, StringComparison.Ordinal);
    }

    // ---------- the two chokepoints: no side effects, no way out of the machine ----------

    [Fact]
    public void ReadingTheCard_LeavesTheRecordAndItsStaleTempExactlyWhereTheyWere()
    {
        // THE REASON THIS CARD DOES NOT USE PersistenceStore. That class's load adopts an orphaned
        // temp, deletes a stale one and QUARANTINES a document it cannot bind
        // (PersistenceStore.cs:322-430) — all correct for the file's OWNER and all wrong for a
        // surface that is only looking at it. Seeded here with exactly the two shapes that would
        // fire: an unbindable document, and a stale .tmp beside it.
        var path = SeedOnDisk("{ corrupt");
        var directory = Path.GetDirectoryName(path)!;
        var temp = path + ".tmp";
        File.WriteAllText(temp, """{ "awardedIds": ["honor_roll"] }""");

        var before = Directory.GetFiles(directory)
            .ToDictionary(f => Path.GetFileName(f), File.ReadAllText, StringComparer.Ordinal);

        var card = TrainerCard.Read(path);
        Assert.Equal(TrainerCardRecordState.Unreadable, card.Record);

        var after = Directory.GetFiles(directory)
            .ToDictionary(f => Path.GetFileName(f), File.ReadAllText, StringComparer.Ordinal);

        Assert.Equal(before.Keys.OrderBy(k => k, StringComparer.Ordinal), after.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(before[GradedRunAwardsDocument.FileName], after[GradedRunAwardsDocument.FileName]);
        Assert.Equal(before[Path.GetFileName(temp)], after[Path.GetFileName(temp)]);
    }

    [Fact]
    public void TheIntakePage_CarriesNoSharingOrExportControl()
    {
        // The row's hard constraint, made mechanical: sharing is unapproved, so it is ABSENT rather
        // than present-and-disabled (a greyed control that swallows the gesture is the §9 D7 shape).
        // The page therefore declares exactly ONE interactive control — the intake launcher — and a
        // lane that adds a second has to come through this fact to do it.
        var page = File.ReadAllText(Path.Combine([FindRepoRoot(), .. PageParts]));

        var controls = Regex.Matches(
            page,
            @"<(Button|CheckBox|ToggleSwitch|ToggleButton|RadioButton|MenuItem|HyperlinkButton|ComboBox|Slider|TextBox|ListBox)\b");

        var control = Assert.Single(controls);
        Assert.Equal("Button", control.Groups[1].Value);
        Assert.Contains("x:Name=\"BeginIntakeButton\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCardsProductSource_NeitherWritesNorTransmits()
    {
        // The other half of the same constraint: no export path in the model either. A card that
        // could write a file or open a socket would be the beginning of the share feature the owner
        // has not approved, and it would arrive without a control to make it visible.
        var source = File.ReadAllText(Path.Combine([FindRepoRoot(), .. CardParts]));

        foreach (var forbidden in new[]
                 {
                     "File.Write", "File.Move", "File.Delete", "File.Copy", "File.Create",
                     "HttpClient", "Socket", "Clipboard", "Process.Start", "SaveFileDialog",
                 })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
        }

        // And it reads exactly one file: the record.
        Assert.Single(Regex.Matches(source, @"File\.ReadAllText\("));
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
            $"repo root not found walking up from {AppContext.BaseDirectory} "
            + $"(anchor: {string.Join('/', RepoAnchorParts)}) — the Trainer Card source pins refuse to skip");
    }
}
