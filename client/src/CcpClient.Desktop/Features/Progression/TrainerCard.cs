using System.Text.Json;
using CcpClient.Desktop.Persistence;

namespace CcpClient.Desktop.Features.Progression;

/// <summary>What the card can say about one award, as a TYPE rather than as a sentence.</summary>
public enum TrainerCardAwardState
{
    /// <summary>The award record holds this id.</summary>
    Earned,

    /// <summary>The record was read, and this id is not in it. Knowable, and known.</summary>
    NotEarnedYet,

    /// <summary>This build keeps no counter for it, so it can neither award it nor say how close
    /// it is. NEVER rendered as a zero — a zero here would read as a score.</summary>
    NotTracked,

    /// <summary>Nothing in this build can produce the event it needs.</summary>
    CannotBeEarnedHere,

    /// <summary>The record could not be read, so this build cannot tell. Distinct from
    /// <see cref="NotEarnedYet"/> on purpose, and for the same reason
    /// <c>Entitlement/EntitlementOutcome.cs:7-17</c> keeps "no" and "I could not tell" apart.</summary>
    Unknown,
}

/// <summary>One row of the card. <paramref name="Name"/> and <paramref name="Requirement"/> are
/// upstream's own authored strings (<c>Models/Achievement.cs:663-701</c>); <paramref name="Status"/>
/// is what THIS build can honestly say about it now.</summary>
public sealed record TrainerCardAward(
    string Id,
    string Name,
    string Requirement,
    TrainerCardAwardState State,
    string Status);

/// <summary>Whether the award record could be read at all.</summary>
public enum TrainerCardRecordState
{
    /// <summary>No award file exists yet: no graded run has ever been recorded here. Nothing is
    /// earned, and that is a fact rather than a guess.</summary>
    NoRunsYet,

    /// <summary>The record was read.</summary>
    Read,

    /// <summary>The record exists and could not be read. The card says so instead of showing an
    /// empty one.</summary>
    Unreadable,
}

/// <summary>
/// THE TRAINER CARD, reduced to what this port can actually stand behind.
///
/// <para>Upstream's card is a portrait with a wardrobe over it, a level, a rank, pinned badges and
/// a scene banner (<c>MainWindow/MainWindow.ProfileCard.cs</c> + the seven other
/// <c>MainWindow.Profile*</c> partials, 3060 lines; census §1.7). This port has ONE piece of that:
/// the graded-run award record (<see cref="GradedRunAwards"/>). So this type projects that record
/// and, for everything else, says in words that it does not know — never a zero, never a blank
/// stat, never a level the port does not compute.</para>
///
/// <para><b>THE LEVEL IS NOW THE CARD'S SECOND PIECE OF REAL STATE.</b> It is projected by
/// <see cref="TrainerCardLevel"/> from the same <c>progression.json</c> the intake, the descent and
/// the Arcademy bank into, read the same passive way this type reads the award record. The two are
/// deliberately separate projections over separate files: the award rows here are complete and
/// proved, and threading a level through <see cref="From"/>, <see cref="Unreadable"/> and
/// <see cref="Rows"/> would give every one of them a parameter it has no use for. What they share is
/// the rule, not the code — an unreadable file answers Unknown with a NULL number, never a zero and
/// never a consoling 1 (<see cref="TrainerCardAwardState.Unknown"/>,
/// <see cref="TrainerCardLevelState.Unknown"/>, <see cref="ProgressionLedger.Known"/>).</para>
///
/// <para><b>WHY THIS RENDERS AWARDS IT CANNOT VERIFY AN ENTITLEMENT FOR (divergence D228).</b> All
/// four graded-run achievements are <c>IsExclusive = true</c>
/// (<c>Models/Achievement.cs:670,680,690,700</c>) and upstream grants them through
/// <c>TryUnlockExclusive</c>, which refuses unless <c>App.Patreon?.HasPremiumAccess == true</c>
/// (<c>Services/Progression/AchievementService.cs:1107</c>, gate at <c>:1116-1120</c>). This port
/// has no entitlement AUTHORITY, and <c>Entitlement/EntitlementOutcome.cs:7-17</c> forbids reading
/// that absence as a refusal — so the ledger grants, and this card renders what was granted. It
/// states the situation (<see cref="NoTierNote"/>) and claims no tier on the user's behalf.</para>
///
/// <para><b>WHAT IS DELIBERATELY ABSENT.</b> No share, export, upload or publish control exists
/// here — not present, not disabled, not hidden. Upstream's counterpart is the leaderboard privacy
/// dialog's eleven sharing toggles (<c>Views/Controls/ProfilePrivacyPanel.xaml.cs:43-74</c>) and the
/// leaderboard traffic behind them (<c>Services/Progression/LeaderboardService.cs:16,106,174</c>),
/// which is owner-gated and unapproved (census §7). A disabled share button would be the
/// fake-available shape the capability contract bans, so there is no button.</para>
/// </summary>
public sealed record TrainerCard(
    TrainerCardRecordState Record,
    string RecordNote,
    int? ClearedCategories,
    IReadOnlyList<TrainerCardAward> Awards)
{
    /// <summary>The surface's own name — the board row's noun.</summary>
    public const string Title = "Trainer Card";

    /// <summary>
    /// What the level on this card is, and — more importantly — what it is NOT. Three sentences,
    /// each answering a thing a level normally implies and this one does not:
    ///
    /// <list type="number">
    /// <item>where the number comes from, so the user can tell which runs moved it;</item>
    /// <item>that it unlocks nothing, because upstream DELETED feature level gating —
    /// <c>Models/AppSettings.cs:5439</c> is <c>return true;</c> under "every feature is available
    /// from level 1" (<c>:5434-5438</c>). A rendered level with no statement here would read as a
    /// key, and this one is a score (see <see cref="LevelUnlocks"/>);</item>
    /// <item>that nothing celebrates a level-up and there is still no streak — the two absences a
    /// user who has seen the shipping app would otherwise read as a broken card.</item>
    /// </list>
    /// </summary>
    public const string LevelNote =
        "Your level and XP are kept on this machine, in " + ProgressionDocument.FileName
        + ", and the graded intake, the descent and the Arcademy all bank into it. The level unlocks "
        + "nothing: feature level gating was removed upstream and every feature is available from "
        + "level 1. Nothing here celebrates a level-up, and this build keeps no streak at all.";

    /// <summary>The other half of the absence, in the same voice.</summary>
    public const string NoPortraitNote =
        "There is no portrait, wardrobe or banner art in this build either, so the card is words rather "
        + "than a picture.";

    /// <summary>The entitlement sentence. It says what the BUILD did, and claims nothing about the
    /// person — see the type remarks and divergence D228.</summary>
    public const string NoTierNote =
        "All four of these are patron-exclusive in the shipping app. This build has no entitlement "
        + "authority to ask, so it cannot tell whether you are a patron: it claims no tier for you, and "
        + "grants what a run earns rather than refusing everyone.";

    /// <summary>The row's constraint, rendered. There is no control to go with it, by design.</summary>
    public const string LocalOnlyNote =
        "This card is read from this machine and stays on it. There is no sharing, export, upload or "
        + "publish path in this build.";

    /// <summary>Upstream's id, name and requirement text, verbatim (<c>Models/Achievement.cs:663-701</c>).
    /// The two numbers inside the requirement strings are upstream's own wording, and
    /// <c>TrainerCardTests</c> re-derives both from the port's live constants so a drifted bar cannot
    /// keep a stale sentence.</summary>
    public const string TopOfTheClassName = "Top of the Class";

    /// <inheritdoc cref="TopOfTheClassName"/>
    public const string TopOfTheClassRequirement = "Score 90% or better on a quiz";

    /// <inheritdoc cref="TopOfTheClassName"/>
    public const string HonorRollName = "Honor Roll";

    /// <inheritdoc cref="TopOfTheClassName"/>
    public const string HonorRollRequirement = "Score 90% or better in 3 different categories";

    /// <inheritdoc cref="TopOfTheClassName"/>
    public const string TeachersPetName = "Teacher's Pet";

    /// <inheritdoc cref="TopOfTheClassName"/>
    public const string TeachersPetRequirement = "Pass 25 quizzes";

    /// <inheritdoc cref="TopOfTheClassName"/>
    public const string HeldBackName = "Held Back";

    /// <inheritdoc cref="TopOfTheClassName"/>
    public const string HeldBackRequirement = "Fail three quizzes in a row";

    /// <summary>The id upstream counts passes for (<c>GamificationBridge.cs:41,588-589</c>).</summary>
    public const string TeachersPetId = "teachers_pet";

    /// <summary>The id upstream counts a fail streak for (<c>GamificationBridge.cs:42,592-595</c>).</summary>
    public const string HeldBackId = "held_back";

    /// <summary>Status wording for a row the record answers YES for.</summary>
    public const string EarnedStatus = "Earned.";

    /// <summary>Status wording for a row the record answers NO for.</summary>
    public const string NotEarnedStatus = "Not earned yet.";

    /// <summary>Status wording for the two rows the record cannot answer at all.</summary>
    public const string UnknownStatus =
        "This build cannot tell: the award record could not be read.";

    /// <summary>Why <c>teachers_pet</c> shows no progress. Upstream counts
    /// <c>ProgressionData.QuizzesPassed</c> (<c>GamificationBridge.cs:586-587</c>); this port keeps no
    /// such counter, and "0 of 25" would be a score the app never computed.</summary>
    public const string TeachersPetStatus =
        "Not tracked here: this build counts no passed runs, so it cannot say how close this is, and it "
        + "never awards it.";

    /// <summary>Why <c>held_back</c> can never fire. Upstream's own comment says an intake has no fail
    /// state (<c>Services/Quiz/IntakeHostService.cs:418-420</c>, restated at
    /// <c>GamificationBridge.cs:574-576</c>), and the graded intake is this port's only producer.</summary>
    public const string HeldBackStatus =
        "Cannot be earned here: the graded intake has no fail state, so nothing in this build can lose "
        + "three runs in a row.";

    /// <summary>Said when the file does not exist. Nothing is earned, and that IS knowable.</summary>
    public const string NoRunsYetNote =
        "No graded run has been recorded on this machine yet.";

    /// <summary>The head of every unreadable-record sentence.</summary>
    public const string UnreadableNoteHead =
        "This card cannot say what you have earned: ";

    /// <summary>Reason clause — malformed bytes. Names the FILE, never the path (the port's display
    /// rule; <c>PortablePath</c>'s remarks).</summary>
    public const string UnreadableInvalidJson =
        GradedRunAwardsDocument.FileName + " is not valid JSON.";

    /// <summary>Reason clause — a document from a build that knows more than this one. Same posture as
    /// the store's <c>LoadOutcome.NewerSchema</c>: read nothing, claim nothing.</summary>
    public const string UnreadableNewerSchema =
        GradedRunAwardsDocument.FileName + " was written by a newer build of this app.";

    /// <summary>Reason clause — the bytes could not be reached at all.</summary>
    public const string UnreadableIoFailure =
        GradedRunAwardsDocument.FileName + " could not be opened.";

    /// <summary>
    /// Case-insensitive on purpose: it makes the read independent of whichever naming policy the
    /// WRITER used, so a change to <c>PersistenceStore</c>'s serializer options can never silently
    /// turn this card into an empty one. Shared with <see cref="TrainerCardLevel"/> rather than
    /// copied, so the two halves of the card cannot end up binding by different rules.
    /// </summary>
    internal static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>The record of a machine that has recorded nothing. Not the same object as "no
    /// record at all", which is <c>null</c> everywhere below and renders as Unknown.</summary>
    private static readonly HashSet<string> NothingEarned = new(GradedRunAwards.AwardIdComparer);

    /// <summary>The rows in card order, whatever the record says.</summary>
    public static IReadOnlyList<string> RowOrder { get; } =
    [
        GradedRunAwards.TopOfTheClassId,
        GradedRunAwards.HonorRollId,
        TeachersPetId,
        HeldBackId,
    ];

    /// <summary>
    /// Read the award record WITHOUT touching it. Deliberately not a
    /// <see cref="PersistenceStore{TModel}"/>: that class's load adopts an orphaned temp file,
    /// deletes a stale one and QUARANTINES a document it cannot bind
    /// (<c>Persistence/PersistenceStore.cs:322-430</c>). Those are the right moves for the owner of
    /// the file and the wrong ones for a card that is only looking at it — a passive render must
    /// never rename a user's record. So this opens, parses, projects, and on any failure says it
    /// could not read rather than showing an empty card.
    /// </summary>
    public static TrainerCard Read(string awardFilePath)
    {
        string text;
        try
        {
            if (!File.Exists(awardFilePath))
            {
                // No file is an ANSWER, not a failure: the ledger writes on the first thing it has
                // to record (GradedRunAwards.cs:260-263), so its absence means nothing was ever
                // earned. Hence the empty set rather than null — these rows are known, not unknown.
                return new TrainerCard(TrainerCardRecordState.NoRunsYet, NoRunsYetNote, 0, Rows(NothingEarned, 0));
            }

            text = File.ReadAllText(awardFilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return Unreadable(UnreadableIoFailure);
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind is not JsonValueKind.Object)
            {
                return Unreadable(UnreadableInvalidJson);
            }

            // The store writes its schema version into the document itself
            // (Persistence/PersistenceStore.cs:86). A file from a LATER build may mean anything, so
            // this refuses to read it rather than binding the half it recognises — the same posture
            // as LoadOutcome.NewerSchema, minus the write-disable that only an owner needs.
            var version = document.RootElement.TryGetProperty(
                    PersistenceStore<GradedRunAwardsDocument>.SchemaVersionKey, out var declared)
                && declared.TryGetInt32(out var parsed)
                ? parsed
                : 0;
            if (version > GradedRunAwardsDocument.CurrentSchemaVersion)
            {
                return Unreadable(UnreadableNewerSchema);
            }

            var model = document.RootElement.Deserialize<GradedRunAwardsDocument>(ReadOptions);
            if (model is null)
            {
                return Unreadable(UnreadableInvalidJson);
            }

            // The document's own setters re-wrap both sets with GradedRunAwards' named comparers, so
            // a file holding "Sissy" and "sissy" is counted ONCE here exactly as the ledger counts it.
            return From(model.AwardedIds, model.PerfectedCategories.Count);
        }
        catch (JsonException)
        {
            return Unreadable(UnreadableInvalidJson);
        }
    }

    /// <summary>Project a record that was read.</summary>
    public static TrainerCard From(IReadOnlySet<string> awardedIds, int clearedCategories)
    {
        ArgumentNullException.ThrowIfNull(awardedIds);
        return new TrainerCard(
            TrainerCardRecordState.Read,
            string.Empty,
            clearedCategories,
            Rows(awardedIds, clearedCategories));
    }

    /// <summary>
    /// The record is there and could not be read. The two ledger-backed rows go
    /// <see cref="TrainerCardAwardState.Unknown"/>; the two that do not depend on the file keep
    /// their answers, because those are properties of the BUILD and an unreadable file says nothing
    /// about them.
    /// </summary>
    public static TrainerCard Unreadable(string reason) =>
        new(TrainerCardRecordState.Unreadable, UnreadableNoteHead + reason, null, Rows(null, null));

    private static IReadOnlyList<TrainerCardAward> Rows(IReadOnlySet<string>? awardedIds, int? clearedCategories) =>
    [
        LedgerRow(GradedRunAwards.TopOfTheClassId, TopOfTheClassName, TopOfTheClassRequirement,
            awardedIds, NotEarnedStatus),
        LedgerRow(GradedRunAwards.HonorRollId, HonorRollName, HonorRollRequirement,
            awardedIds, HonorRollProgress(clearedCategories)),
        FixedRow(TeachersPetId, TeachersPetName, TeachersPetRequirement,
            awardedIds, TrainerCardAwardState.NotTracked, TeachersPetStatus),
        FixedRow(HeldBackId, HeldBackName, HeldBackRequirement,
            awardedIds, TrainerCardAwardState.CannotBeEarnedHere, HeldBackStatus),
    ];

    /// <summary>A row this build can earn: the record decides, and a record that could not be read
    /// produces <see cref="TrainerCardAwardState.Unknown"/> rather than "not earned".</summary>
    private static TrainerCardAward LedgerRow(
        string id, string name, string requirement, IReadOnlySet<string>? awardedIds, string notEarned) =>
        awardedIds is null
            ? new TrainerCardAward(id, name, requirement, TrainerCardAwardState.Unknown, UnknownStatus)
            : awardedIds.Contains(id)
                ? new TrainerCardAward(id, name, requirement, TrainerCardAwardState.Earned, EarnedStatus)
                : new TrainerCardAward(id, name, requirement, TrainerCardAwardState.NotEarnedYet, notEarned);

    /// <summary>
    /// A row this build cannot earn. It still reports <see cref="TrainerCardAwardState.Earned"/> when
    /// the record holds the id: the ledger preserves ids this build does not know
    /// (<c>GradedRunAwards.cs:61-65</c>, upstream's own <c>Contains</c> behaviour), and a card that
    /// hid one would be editing the user's record on the way to the screen.
    /// </summary>
    private static TrainerCardAward FixedRow(
        string id, string name, string requirement, IReadOnlySet<string>? awardedIds,
        TrainerCardAwardState state, string status) =>
        awardedIds?.Contains(id) == true
            ? new TrainerCardAward(id, name, requirement, TrainerCardAwardState.Earned, EarnedStatus)
            : new TrainerCardAward(id, name, requirement, state, status);

    /// <summary>
    /// The one real progress number on the card, and the only place a count is rendered: distinct
    /// cleared categories against <see cref="GradedRunAwards.HonorRollCategories"/>
    /// (<c>GamificationBridge.cs:40,603</c>). Both terms come from live state — neither is a
    /// literal — so this line cannot drift away from what the ledger would award.
    /// </summary>
    private static string HonorRollProgress(int? clearedCategories) =>
        clearedCategories is not { } cleared
            ? UnknownStatus
            : $"{NotEarnedStatus} {cleared} of {GradedRunAwards.HonorRollCategories} categories cleared at top marks.";
}

/// <summary>Whether this build can say what level the subject stands at.</summary>
public enum TrainerCardLevelState
{
    /// <summary>The ledger was read — including the case where it does not exist yet, which is an
    /// ANSWER (a fresh account stands at <see cref="XpCurve.FirstLevel"/> with no XP into it) and
    /// not a failure. That is the same reading <see cref="ProgressionLedger.Known"/> takes:
    /// <c>LoadOutcome.Missing</c> is not degraded, so the ledger reports level 1 rather than
    /// null.</summary>
    Known,

    /// <summary>The file is there and could not be read, so the level is UNKNOWN. Rendered as
    /// words, never as a number — see the type remarks.</summary>
    Unknown,
}

/// <summary>
/// THE LEVEL, AS SOMETHING A SURFACE CAN DRAW. The port has computed a level since the XP spine
/// landed and nothing rendered it; this is the projection that ends that, and it is upstream's
/// <c>UpdateLevelDisplay</c> (<c>MainWindow/MainWindow.UiUpdates.cs:51-87</c>) reduced to the four
/// things it writes that this build can stand behind.
///
/// <list type="table">
/// <item><term>the level number</term><description><c>MainWindow.UiUpdates.cs:59</c> —
/// <c>TxtLevelLabel.Text = $"LVL {level}"</c> (the header chip; the profile bubble's second copy at
/// <c>:58</c> reads <c>$"Lvl {level}"</c>).</description></item>
/// <item><term>the XP readout</term><description><c>MainWindow/MainWindow.ChromeFx.cs:814</c> —
/// <c>$"{(int)xp} / {(int)xpNeeded} XP"</c>, where <c>xpNeeded</c> is
/// <c>App.Progression.GetXPForLevel(level)</c> (<c>UiUpdates.cs:55</c>), ported as
/// <see cref="XpCurve.XpForLevel"/>.</description></item>
/// <item><term>the bar's fill</term><description><c>MainWindow.ChromeFx.cs:826</c> —
/// <c>Math.Min(1.0, xpNeeded &gt; 0 ? xp / xpNeeded : 0)</c>, against a track upstream draws at
/// <c>MainWindow.xaml:2168-2170</c>.</description></item>
/// <item><term>the rank title</term><description><c>MainWindow.UiUpdates.cs:70-76</c> — four bands,
/// four literals, no table and no threshold of this port's own invention.</description></item>
/// </list>
///
/// <para><b>WHY THIS IS A PASSIVE READ AND NOT THE LEDGER.</b> <see cref="ProgressionLedger.Open"/>
/// starts a <see cref="PersistenceStore{TModel}"/>, and that load adopts an orphaned temp file,
/// deletes a stale one and QUARANTINES a document it cannot bind
/// (<c>Persistence/PersistenceStore.cs:322-430</c>). Those are the right moves for the ledger's
/// OWNER — the intake, the descent and the Arcademy hosts — and the wrong ones for a card being
/// looked at. So this opens one file, parses it, closes it, and touches nothing: exactly the
/// argument <see cref="TrainerCard.Read"/> already makes for the award record beside it.</para>
///
/// <para><b>THE LEVEL-UP CEREMONY IS REFUSED, AND HERE IS EVERY PIECE OF IT WITH ITS REASON.</b>
/// Upstream runs one on every level gained (<c>MainWindow/MainWindow.xaml.cs:763-777</c> and
/// <c>Services/Progression/ProgressionService.cs:226-255</c>): an XP-bar bloom, an <c>LVL</c>-chip
/// pop and a particle burst (<c>MainWindow/MainWindow.EventFx.cs:232-246</c>); a tray balloon
/// reading <i>"You reached Level N!"</i> (<c>MainWindow.xaml.cs:771</c>); <c>lvup.mp3</c>
/// (<c>:773</c>, resolved at <c>:779-800</c>); an avatar swap at 20/50/100 (<c>:775</c>); a haptic
/// pattern (<c>ProgressionService.cs:229</c>); level achievements (<c>:231</c>); skill points
/// (<c>:235</c>); Discord presence (<c>:241</c>); a milestone webhook (<c>:247</c>); and a
/// leaderboard sync (<c>:251</c>). NONE of it is here, and the refusal is structural rather than a
/// deferral: <b>this surface never sees a level-up.</b> It is a passive read taken when the page is
/// mounted, and the grant that would raise a level happens inside a modal run window with this page
/// unmounted behind it — there is no event to celebrate on. The sound is refused a second time on
/// its own: <c>lvup.mp3</c> is legacy-owned bytes
/// (<c>ConditioningControlPanel/Resources/sounds/lvup.mp3</c>) that <c>client/</c> does not link, so
/// wiring it would be a payload decision rather than a render. <see cref="TrainerCard.LevelNote"/>
/// says the absence out loud rather than leaving a user to notice it.</para>
///
/// <para><b>AND THE LEVEL UNLOCKS NOTHING.</b> Upstream deleted feature level gating outright —
/// <c>Models/AppSettings.cs:5439</c> is <c>return true;</c> (<see cref="LevelUnlocks"/>) — so
/// nothing here reads this number to decide anything. It is a score the user can see, and
/// <see cref="TrainerCard.LevelNote"/> is on the card so it cannot be mistaken for a key.</para>
/// </summary>
public sealed record TrainerCardLevel(
    TrainerCardLevelState State,
    int? Level,
    double? XpIntoLevel,
    double? XpForNextLevel,
    string Note)
{
    /// <summary>The heading over the level block. Not upstream's, which has none at all because its
    /// level lives in permanent window chrome; a card needs to say what its numbers are.</summary>
    public const string Heading = "Level";

    /// <summary>What stands where the number would be when the ledger could not be read. The honesty
    /// bar this card and the ledger both hold to: a level that cannot be known renders as words,
    /// never as 0 and never as a consoling 1 (<see cref="ProgressionLedger.Level"/> is null for the
    /// same reason).</summary>
    public const string UnknownLevelLine = "Level unknown";

    /// <summary>The head of every unreadable-ledger sentence. Distinct from
    /// <see cref="TrainerCard.UnreadableNoteHead"/>: the two files fail independently, and a card
    /// that could not read the AWARD record can still show a level.</summary>
    public const string UnknownNoteHead = "This card cannot show your level: ";

    /// <summary>Reason clause — malformed bytes. Names the FILE, never the path (the port's display
    /// rule; <c>PortablePath</c>'s remarks).</summary>
    public const string UnknownInvalidJson = ProgressionDocument.FileName + " is not valid JSON.";

    /// <summary>Reason clause — a document from a build that knows more than this one. Same posture
    /// as the store's <c>LoadOutcome.NewerSchema</c>: read nothing, claim nothing.</summary>
    public const string UnknownNewerSchema =
        ProgressionDocument.FileName + " was written by a newer build of this app.";

    /// <summary>Reason clause — the bytes could not be reached at all.</summary>
    public const string UnknownIoFailure = ProgressionDocument.FileName + " could not be opened.";

    /// <summary>Upstream's four rank titles, verbatim
    /// (<c>MainWindow/MainWindow.UiUpdates.cs:71-75</c>). Bands and strings both; this port invents
    /// neither.</summary>
    public const string RankUnder20 = "BASIC BIMBO";

    /// <inheritdoc cref="RankUnder20"/>
    public const string RankUnder50 = "DUMB AIRHEAD";

    /// <inheritdoc cref="RankUnder20"/>
    public const string RankUnder100 = "SYNTHETIC BLOWDOLL";

    /// <inheritdoc cref="RankUnder20"/>
    public const string RankFrom100 = "PERFECT FUCKPUPPET";

    /// <summary>
    /// Upstream's rank switch (<c>MainWindow/MainWindow.UiUpdates.cs:70-76</c>), band for band.
    ///
    /// <para>Upstream then wraps the result in <c>App.Mods?.MakeModAware(rankTitle) ?? rankTitle</c>
    /// (<c>:77</c>). There is no mod system in this build, so the <c>?? rankTitle</c> arm IS the
    /// parity outcome — the same reading <see cref="XpCurve"/> already takes for
    /// <c>App.SkillTree?.GetTotalXpMultiplier() ?? 1.0</c>, and not a wrapper that was
    /// dropped.</para>
    /// </summary>
    public static string RankFor(int level) => level switch
    {
        < 20 => RankUnder20,                                                 // :71
        < 50 => RankUnder50,                                                 // :72
        < 100 => RankUnder100,                                               // :73
        _ => RankFrom100,                                                    // :74
    };

    /// <summary>The level number as upstream's header chip writes it
    /// (<c>MainWindow.UiUpdates.cs:59</c>), or <see cref="UnknownLevelLine"/>.</summary>
    public string LevelLine => Level is { } level ? $"LVL {level}" : UnknownLevelLine;

    /// <summary>The rank title for <see cref="Level"/>, or empty when there is no level to rank.
    /// Empty rather than a placeholder: a rank invented over an unknown level would be exactly the
    /// claim-about-the-person this card refuses to make.</summary>
    public string RankLine => Level is { } level ? RankFor(level) : string.Empty;

    /// <summary>
    /// The XP readout, upstream's format exactly (<c>MainWindow/MainWindow.ChromeFx.cs:814</c>):
    /// <c>$"{(int)xp} / {(int)xpNeeded} XP"</c>. The casts TRUNCATE, and that is upstream's
    /// behaviour rather than an approximation of it — a bank of 799.6 into an 800 level reads
    /// <c>799 / 800 XP</c> there and here.
    /// </summary>
    public string XpLine => XpIntoLevel is { } xp && XpForNextLevel is { } needed
        ? $"{(int)xp} / {(int)needed} XP"
        : string.Empty;

    /// <summary>
    /// How full the bar is, 0..1 — upstream's
    /// <c>Math.Min(1.0, xpNeeded &gt; 0 ? xp / xpNeeded : 0)</c>
    /// (<c>MainWindow/MainWindow.ChromeFx.cs:826</c>), which upstream then multiplies by the track's
    /// measured width. Null when the level is unknown, because a bar drawn at 0 for a subject who
    /// might be half way through a level is a number this card would be making up.
    ///
    /// <para>There is no lower clamp because upstream has none and needs none: XP into a level is
    /// never negative — <see cref="ProgressionDocument.Xp"/>'s setter reads a negative or non-finite
    /// value as 0 on the way in, which is the guard upstream keeps at <c>AddXP</c>'s own door
    /// (<c>ProgressionService.cs:85</c>).</para>
    /// </summary>
    public double? Fill => XpIntoLevel is { } xp && XpForNextLevel is { } needed
        ? Math.Min(1.0, needed > 0 ? xp / needed : 0)
        : null;

    /// <summary>
    /// Read the ledger WITHOUT touching it, in the shape <see cref="TrainerCard.Read"/> established
    /// for the award record beside it: no <see cref="PersistenceStore{TModel}"/>, no adoption, no
    /// quarantine, no write of any kind.
    /// </summary>
    public static TrainerCardLevel Read(string progressionFilePath)
    {
        string text;
        try
        {
            if (!File.Exists(progressionFilePath))
            {
                // No file is an ANSWER. The ledger's own reading of the same situation is
                // LoadOutcome.Missing, which is NOT degraded, so ProgressionLedger.Known is true and
                // its Level is the document's default — XpCurve.FirstLevel with nothing into it. A
                // fresh subject really does stand at level 1; that is not a guess, and it is the one
                // place a 1 on this card is honest.
                return From(new ProgressionDocument());
            }

            text = File.ReadAllText(progressionFilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return Unknown(UnknownIoFailure);
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind is not JsonValueKind.Object)
            {
                return Unknown(UnknownInvalidJson);
            }

            // A file from a LATER build may mean anything — a fifth band on the curve, a second
            // epoch, a watermark this build would ignore — so this refuses to read it rather than
            // binding the half it recognises. Same posture as LoadOutcome.NewerSchema
            // (PersistenceStore.cs:373-377), minus the write-disable only an owner needs.
            var version = document.RootElement.TryGetProperty(
                    PersistenceStore<ProgressionDocument>.SchemaVersionKey, out var declared)
                && declared.TryGetInt32(out var parsed)
                ? parsed
                : 0;
            if (version > ProgressionDocument.CurrentSchemaVersion)
            {
                return Unknown(UnknownNewerSchema);
            }

            var model = document.RootElement.Deserialize<ProgressionDocument>(TrainerCard.ReadOptions);
            return model is null ? Unknown(UnknownInvalidJson) : From(model);
        }
        catch (JsonException)
        {
            return Unknown(UnknownInvalidJson);
        }
    }

    /// <summary>
    /// Project a ledger that was read. The document's own setters have already clamped the level
    /// into <c>[FirstLevel, MaxLevel]</c> and zeroed a non-finite or negative bank, so a hand-edited
    /// file cannot put a level 0 or a NaN on the screen.
    /// </summary>
    public static TrainerCardLevel From(ProgressionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new TrainerCardLevel(
            TrainerCardLevelState.Known,
            document.Level,
            document.Xp,
            XpCurve.XpForLevel(document.Level),                              // UiUpdates.cs:55
            string.Empty);
    }

    /// <summary>The ledger is there and could not be read. Every number is null, so nothing
    /// downstream can render a zero by accident.</summary>
    public static TrainerCardLevel Unknown(string reason) =>
        new(TrainerCardLevelState.Unknown, null, null, null, UnknownNoteHead + reason);
}
