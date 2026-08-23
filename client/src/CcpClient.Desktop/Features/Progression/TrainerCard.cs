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
/// <para><b>WHY THERE IS NO LEVEL AND NO XP HERE.</b> Not an omission: no XP, level, streak or rank
/// exists anywhere in <c>client/src</c> (census §3 B1, re-derived 2026-08-23 — every <c>XP</c>
/// mention in the tree is a comment saying the port does not have one, e.g.
/// <c>Effects/BouncingTextEffect.cs:35</c>). Inventing an economy to fill the card would put a
/// number in front of a user that nothing in the app computes.</para>
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

    /// <summary>Said out loud rather than left as an empty stat block.</summary>
    public const string NoLevelNote =
        "This build keeps no level, XP, rank or streak, so this card shows none. What it does know is "
        + "the graded-run record below.";

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
    /// turn this card into an empty one.
    /// </summary>
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

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
