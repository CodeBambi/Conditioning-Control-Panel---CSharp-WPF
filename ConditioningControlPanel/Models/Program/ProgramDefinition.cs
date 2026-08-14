using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Models.Program;

/// <summary>
/// Which tier a program belongs to. Free programs are the conversion funnel and must not be
/// crippled; Premium programs may reference Patreon-exclusive verifiers.
/// </summary>
public enum ProgramTier
{
    Free,
    Premium
}

/// <summary>
/// How a day's task is proven. AutoVerified taps the existing quest fan-out
/// (QuestService.UpdateQuestProgress is the single choke point every Track* call funnels through).
/// Ritual is self-attested with a photo, reusing the roadmap diary.
/// </summary>
public enum ProgramTaskKind
{
    AutoVerified,
    Ritual
}

/// <summary>
/// A day's optional all-day passive layer. Unlocks at chapter 3 in every program - the moment the
/// program leaves the session and enters the day. Modelled now, honoured from P1 onward.
/// </summary>
public class ProgramAmbient
{
    /// <summary>Free-text description shown on the Today card ("Triggers armed").</summary>
    public string Description { get; set; } = "";

    /// <summary>Cumulative minutes of the ambient feature that the day requires. 0 = flavour only.</summary>
    public int RequiredMinutes { get; set; }

    /// <summary>Which quest category accumulates the ambient minutes, when one applies.</summary>
    public QuestCategory? Verifier { get; set; }

    /// <summary>
    /// Same meaning as <see cref="ProgramTask.OutsideSession"/>, one layer up: these minutes are
    /// meant to accrue across the user's whole day - a filter left on through the working day -
    /// rather than inside the day's own session, so <see cref="ProgramDefinition.Validate"/> does
    /// not hold the session's minute budget against them.
    ///
    /// It has to be set deliberately. Leaving it false on an ambient the session cannot possibly
    /// fill is the same authoring bug OutsideSession exists to catch, and it is worse here: an
    /// unreachable ambient does not block the day, it lets
    /// <c>ProgramService.SettleAmbientShortfallDay</c> settle the day at rollover *without the day
    /// XP*, so the user quietly earns less for a day they completed in full.
    ///
    /// Additive, defaulting to false, so definitions serialised before it existed round-trip
    /// unchanged.
    /// </summary>
    public bool OutsideSession { get; set; }
}

/// <summary>
/// One task on one day. AutoVerified tasks deliberately reuse <see cref="QuestCategory"/> so the
/// program rides the tracking fan-out that already exists rather than adding a second pass.
/// </summary>
public class ProgramTask
{
    /// <summary>Stable within a day. Used as the key in the day record's progress dictionary.</summary>
    public string Id { get; set; } = "";

    public ProgramTaskKind Kind { get; set; } = ProgramTaskKind.AutoVerified;

    /// <summary>Player-facing instruction ("Pop 20 bubbles").</summary>
    public string Description { get; set; } = "";

    /// <summary>AutoVerified only: which signal proves it.</summary>
    public QuestCategory? Verifier { get; set; }

    /// <summary>AutoVerified only: how much of that signal is required.</summary>
    public int TargetValue { get; set; } = 1;

    /// <summary>Ritual only: the roadmap step this task borrows (objective + photo requirement).</summary>
    public string? RoadmapStepId { get; set; }

    /// <summary>Gated behind an active pledge. Enforced in ProgramService, never in the session layer.</summary>
    public bool RequiresPremium { get; set; }

    /// <summary>
    /// A task the user may skip without it counting as a miss. Free-tier real-world rituals are
    /// optional by design - never gate the conversion funnel on a physical act.
    /// </summary>
    public bool Optional { get; set; }

    /// <summary>
    /// #747: this task is meant to be satisfied by the user's own dashboard time, NOT by the day's
    /// session - so the feasibility check skips it. Set it deliberately; leaving it false on a task
    /// the session cannot possibly deliver is the authoring bug that shipped Kept Day 1 unfinishable
    /// (see <see cref="ProgramDefinition.Validate"/>).
    /// </summary>
    public bool OutsideSession { get; set; }
}

/// <summary>
/// One day of a program. Carries a session *reference* - template id, duration and a position on
/// the intensity curve - never a full authored SessionSettings. This is the decision that keeps
/// a 28-day program at four authored templates instead of 28 hand-built session files.
/// </summary>
public class ProgramDay
{
    /// <summary>1-based, global across the whole program (not per chapter).</summary>
    public int DayIndex { get; set; }

    /// <summary>Short title shown on the Today card.</summary>
    public string Title { get; set; } = "";

    /// <summary>Spoiler-free blurb. Says what today feels like, not what today does.</summary>
    public string Blurb { get; set; } = "";

    /// <summary>Boss days are the chapter peak and ignore the deload.</summary>
    public bool IsBoss { get; set; }

    public string SessionTemplateId { get; set; } = "";

    /// <summary>Authored per day. Quantised 30 / 45 / 60 / 75 by convention, not enforced.</summary>
    public int SessionMinutes { get; set; } = 30;

    /// <summary>Position on the program's curve, 0..1. Lerps every numeric field of the template.</summary>
    public double Intensity { get; set; }

    /// <summary>
    /// Sparse per-day escalation the curve cannot express. Keys are SessionSettings property names.
    /// Values survive a JSON round-trip as JsonElement, so the applier materialises them explicitly
    /// (same hazard as TimelineEvent.GetSetting, issue #429).
    /// </summary>
    public Dictionary<string, object>? Overrides { get; set; }

    public List<ProgramTask> Tasks { get; set; } = new();

    public ProgramAmbient? Ambient { get; set; }

    /// <summary>Free-text description of what completing this day grants beyond XP.</summary>
    public string? RewardDescription { get; set; }
}

/// <summary>A named 7-day block: six days plus a boss.</summary>
public class ProgramChapter
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Subtitle { get; set; } = "";

    /// <summary>Hex, e.g. "#FF69B4". Falls back to the program accent when blank.</summary>
    public string AccentColor { get; set; } = "";

    public List<ProgramDay> Days { get; set; } = new();

    /// <summary>What finishing this chapter banks permanently. Survives a restart.</summary>
    public string? RewardDescription { get; set; }

    /// <summary>Identifier of the banked reward, recorded on the enrollment so a restart cannot revoke it.</summary>
    public string? RewardId { get; set; }

    /// <summary>
    /// The reward files this chapter's LAST day as a real, permanently replayable session in the
    /// user's own Sessions catalogue (ProgramRewardService does the filing).
    ///
    /// Authored on the chapter rather than hard-coded against RewardId so a community program can
    /// promise the same thing and actually get it. The day is derived - it is always the chapter's
    /// highest DayIndex - because every reward line in the built-ins names the chapter's final day
    /// and a second authored number could only ever disagree with the first.
    /// </summary>
    public bool RewardSavesFinalSession { get; set; }

    /// <summary>
    /// Phrases the reward installs permanently into the user's subliminal pool. These must be keys
    /// of the pool the program's own mod ships (see BuiltInMods) - a phrase that is not a manifest
    /// key still flashes, but has no linked whisper audio and no haptic pattern, so it lands as a
    /// silent stranger among the user's own phrases.
    ///
    /// Deliberately a SUBSET of the chapter's session templates rather than the whole pool: the
    /// reward line names two or three phrases, and installing fifteen would be a different promise.
    /// </summary>
    public List<string> RewardPhrases { get; set; } = new();
}

/// <summary>
/// A session shape authored once and reused across many days. Numeric fields lerp between
/// <see cref="Floor"/> and <see cref="Ceiling"/> by the day's intensity; booleans, enums and phrase
/// pools are taken from Floor, because which *features* a template runs is the template's identity.
/// </summary>
public class ProgramSessionTemplate
{
    public string Id { get; set; } = "";

    /// <summary>
    /// Display name. ProgramSessionBuilder prefixes it so it can never collide with the substrings
    /// AchievementService.TrackSessionComplete matches on ("morning drift", "gamer girl",
    /// "distant doll", "good girls").
    /// </summary>
    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    /// <summary>Settings at intensity 0.</summary>
    public SessionSettings Floor { get; set; } = new();

    /// <summary>Settings at intensity 1. Only numeric fields are read.</summary>
    public SessionSettings Ceiling { get; set; } = new();
}

/// <summary>Program-wide rules the engine enforces.</summary>
public class ProgramRules
{
    /// <summary>
    /// Days off for the whole program regardless of length - 7, 14 or 28 all get the same allowance.
    /// The second absence lapses the program and offers a restart.
    /// </summary>
    public int DaysOffAllowed { get; set; } = 1;

    /// <summary>Whether a zero-absence Strict enrollment is offered.</summary>
    public bool StrictAvailable { get; set; } = true;

    /// <summary>Hour the day rolls over, local. 04:00 so a 2am session counts for the day before.</summary>
    public int DefaultDayBoundaryHour { get; set; } = 4;

    /// <summary>Sanity ceiling on dedicated seat time. Escalate via ambient and intensity, not duration.</summary>
    public int MaxDailyMinutes { get; set; } = 90;
}

/// <summary>
/// A complete multi-day program. Serialised as a bare object to "&lt;id&gt;.program.json" using the
/// same System.Text.Json camelCase options as .session.json, so the catalogue round-trips for free.
/// </summary>
public class ProgramDefinition
{
    public const string CurrentSchema = "ccp-program/v1";

    public string Schema { get; set; } = CurrentSchema;

    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";

    /// <summary>The one-line sell, shown on the browse card and in the enrollment ceremony.</summary>
    public string Pitch { get; set; } = "";

    public string Icon { get; set; } = "\U0001f4c5";

    /// <summary>Mod this program is themed on, e.g. "builtin-bambisleep". Informational for now.</summary>
    public string ModId { get; set; } = "";

    /// <summary>Hex accent. Defaults to the Bambi pink the app already uses.</summary>
    public string AccentColor { get; set; } = "#FF69B4";

    public ProgramTier Tier { get; set; } = ProgramTier.Free;

    /// <summary>Authoritative length. Validated against the sum of chapter days on load.</summary>
    public int LengthDays { get; set; }

    public List<ProgramSessionTemplate> Templates { get; set; } = new();
    public List<ProgramChapter> Chapters { get; set; } = new();
    public ProgramRules Rules { get; set; } = new();

    /// <summary>Achievement id granted on graduation, if any.</summary>
    public string? GraduationBadgeId { get; set; }

    /// <summary>Copy shown on the enrollment ceremony's safety panel. Plain, out of character, once.</summary>
    public string SafetyNote { get; set; } = "";

    /// <summary>The phrase the user types to enroll. In the mod's voice.</summary>
    public string ContractPhrase { get; set; } = "";

    public IEnumerable<ProgramDay> AllDays => Chapters.SelectMany(c => c.Days).OrderBy(d => d.DayIndex);

    public ProgramDay? GetDay(int dayIndex) => AllDays.FirstOrDefault(d => d.DayIndex == dayIndex);

    public ProgramChapter? GetChapterForDay(int dayIndex) =>
        Chapters.FirstOrDefault(c => c.Days.Any(d => d.DayIndex == dayIndex));

    public ProgramSessionTemplate? GetTemplate(string templateId) =>
        Templates.FirstOrDefault(t => string.Equals(t.Id, templateId, StringComparison.OrdinalIgnoreCase));

    /// <summary>True when the day is the last of the program.</summary>
    public bool IsFinalDay(int dayIndex) => dayIndex >= LengthDays;

    /// <summary>
    /// Structural validation. Returns false with a reason so a bad community program is rejected at
    /// import rather than half-run.
    /// </summary>
    public bool Validate(out string error)
    {
        error = "";

        if (string.IsNullOrWhiteSpace(Id)) { error = "Program id is missing."; return false; }
        if (string.IsNullOrWhiteSpace(Title)) { error = "Program title is missing."; return false; }
        if (Chapters.Count == 0) { error = "Program has no chapters."; return false; }

        var templateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var template in Templates)
        {
            if (string.IsNullOrWhiteSpace(template.Id))
            {
                error = "A session template has no id.";
                return false;
            }

            // GetTemplate takes the first match, so a duplicate id would silently bind days to
            // whichever one happens to be declared first - a very quiet way to author the wrong run.
            if (!templateIds.Add(template.Id))
            {
                error = $"Duplicate session template id '{template.Id}'.";
                return false;
            }
        }

        var chapterIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var chapter in Chapters)
        {
            if (string.IsNullOrWhiteSpace(chapter.Id))
            {
                error = "A chapter has no id.";
                return false;
            }

            if (!chapterIds.Add(chapter.Id))
            {
                error = $"Duplicate chapter id '{chapter.Id}'.";
                return false;
            }
        }

        var days = AllDays.ToList();
        if (days.Count == 0) { error = "Program has no days."; return false; }

        if (LengthDays != days.Count)
        {
            error = $"Program declares {LengthDays} days but contains {days.Count}.";
            return false;
        }

        for (int i = 0; i < days.Count; i++)
        {
            if (days[i].DayIndex != i + 1)
            {
                error = $"Day indices must run 1..{days.Count} with no gaps (found {days[i].DayIndex} at position {i + 1}).";
                return false;
            }
        }

        foreach (var day in days)
        {
            if (GetTemplate(day.SessionTemplateId) == null)
            {
                error = $"Day {day.DayIndex} references unknown session template '{day.SessionTemplateId}'.";
                return false;
            }

            if (day.SessionMinutes <= 0)
            {
                error = $"Day {day.DayIndex} has a non-positive session duration.";
                return false;
            }

            if (day.SessionMinutes > Rules.MaxDailyMinutes)
            {
                error = $"Day {day.DayIndex} exceeds the {Rules.MaxDailyMinutes} minute daily cap.";
                return false;
            }

            if (day.Intensity < 0 || day.Intensity > 1)
            {
                error = $"Day {day.DayIndex} intensity {day.Intensity} is outside 0..1.";
                return false;
            }

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var task in day.Tasks)
            {
                if (string.IsNullOrWhiteSpace(task.Id))
                {
                    error = $"Day {day.DayIndex} has a task with no id.";
                    return false;
                }

                if (!ids.Add(task.Id))
                {
                    error = $"Day {day.DayIndex} has duplicate task id '{task.Id}'.";
                    return false;
                }

                if (task.Kind == ProgramTaskKind.AutoVerified && task.Verifier == null)
                {
                    error = $"Day {day.DayIndex} task '{task.Id}' is auto-verified but has no verifier.";
                    return false;
                }

                if (task.Kind == ProgramTaskKind.Ritual && string.IsNullOrWhiteSpace(task.RoadmapStepId))
                {
                    error = $"Day {day.DayIndex} task '{task.Id}' is a ritual but names no roadmap step.";
                    return false;
                }

                if (!IsTaskFeasible(day, task, out var why))
                {
                    error = $"Day {day.DayIndex} task '{task.Id}' can never be completed: {why}";
                    return false;
                }
            }

            if (!IsAmbientFeasible(day, out var ambientWhy))
            {
                error = $"Day {day.DayIndex} ambient layer can never be completed: {ambientWhy}";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The ±3 minute jitter <c>SessionEngine.RandomizeStartTimes</c> adds to delayed starts. A task
    /// that only just fits on paper fails a third of the time once the randomiser moves the start
    /// later, so the budget is checked against the worst case.
    /// </summary>
    private const int StartJitterMinutes = 3;

    // ---------------------------------------------------------------------------------------------
    // Engine clamps, mirrored.
    //
    // Every one of these lives on an AppSettings *setter*, and SessionEngine.ApplySessionSettings
    // writes the session's numbers straight through those setters - so a template authoring a value
    // above the clamp does not get that value, it gets the clamp, silently. A feasibility model that
    // did not clamp would promise supply the app is structurally incapable of producing. The flash
    // one is the load-bearing case: AppSettings.FlashFrequency clamps to 1..180 and SessionEngine
    // writes it twice (once at start, then every second of the ramp), so an authored 480/hour is
    // 180/hour and an authored 180 -> 480 ramp is a flat line.
    //
    // Raising any of these clamps is an owner call. Mirroring them here is not.
    // ---------------------------------------------------------------------------------------------
    internal const int MaxFlashPerHour = 180;         // AppSettings.FlashFrequency
    internal const int MaxSimultaneousImages = 20;    // AppSettings.SimultaneousImages
    internal const int MaxBubblesPerMinute = 60;      // AppSettings.BubblesFrequency (per MINUTE)
    internal const int MaxVideosPerHour = 20;         // AppSettings.VideosPerHour
    internal const int MaxBubbleCountPerHour = 10;    // AppSettings.BubbleCountFrequency
    internal const int MaxLockCardsPerHour = 10;      // AppSettings.LockCardFrequency

    /// <summary>
    /// Minutes of credited playback one mandatory video is modelled as worth.
    ///
    /// QuestCategory.Video credits *actual playback seconds* of the user's own files
    /// (VideoService.FinalizeWatchCredit -> AchievementService -> QuestService.TrackVideoMinutes),
    /// and the app puts no cap on clip length by default, so the true figure is content-dependent
    /// and unknowable from a definition. Two minutes is the deliberately conservative stand-in: it
    /// is short enough that a day passing this check will still pass for a user whose library is
    /// mostly short loops, which is the only direction that matters. A day that wants twenty
    /// minutes of playback out of a session running one video an hour is not a tuning question, it
    /// is a task belonging to the user's own day - mark it OutsideSession.
    /// </summary>
    internal const double ConservativeClipMinutes = 2.0;

    /// <summary>
    /// How much of a verifier's signal one session can produce, in the units the task counts in.
    /// Null means "not modellable" - the int? frequency fields fall through to the user's own
    /// dashboard value when a template leaves them null, and guessing at that would be worse than
    /// not checking.
    /// </summary>
    private delegate double? SupplyModel(SessionSettings settings, int startMinute, int sessionMinutes, int availableMinutes);

    /// <summary>Which session feature a verifier draws its credit from.</summary>
    /// <param name="Name">Human name for the error message.</param>
    /// <param name="MinuteDenominated">True when TargetValue counts minutes of runtime rather than events.</param>
    /// <param name="Supply">How much of the signal the session can produce - see <see cref="SupplyModel"/>.</param>
    private readonly record struct VerifierFeature(
        string Name,
        bool MinuteDenominated,
        Func<SessionSettings, bool> Enabled,
        Func<SessionSettings, int> StartMinute,
        SupplyModel Supply);

    private static readonly Dictionary<QuestCategory, VerifierFeature> SessionFeatureVerifiers = new()
    {
        [QuestCategory.Flash] = new("flash images", false, s => s.FlashEnabled, s => s.FlashStartMinute, FlashSupply),
        [QuestCategory.Video] = new("mandatory videos", true, s => s.MandatoryVideosEnabled, s => s.MandatoryVideosStartMinute, VideoSupply),
        [QuestCategory.Spiral] = new("spiral", true, s => s.SpiralEnabled, s => s.SpiralStartMinute, OverlayMinuteSupply),
        [QuestCategory.PinkFilter] = new("pink filter", true, s => s.PinkFilterEnabled, s => s.PinkFilterStartMinute, OverlayMinuteSupply),
        [QuestCategory.Bubbles] = new("bubbles", false, s => s.BubblesEnabled, s => s.BubblesStartMinute, BubbleSupply),
        [QuestCategory.LockCard] = new("lock cards", false, s => s.LockCardEnabled, s => s.LockCardStartMinute, LockCardSupply),
        [QuestCategory.BubbleCount] = new("bubble count", false, s => s.BubbleCountEnabled, s => s.BubbleCountStartMinute, BubbleCountSupply),
    };

    // ---------------------------------------------------------------------------------------------
    // Supply models. Each one answers "how much credit can this session hand out?" in the units the
    // task counts in, and each is written against the service that actually fires the Track* call -
    // not against what the setting is named. Two of those turned out to matter a great deal:
    //   - flash credit is per IMAGE, not per burst (FlashService.SpawnFlashWindow tracks once per
    //     spawned window, and a burst spawns FlashImages of them), so a burst of four counts four;
    //   - bubbles are credited on POP, not on spawn, and only a clickable bubble can be popped.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// SessionEngine ramps the flash rate linearly across the WHOLE session clock - not from the
    /// feature's own start minute - and writes it through the clamped AppSettings.FlashFrequency
    /// every second, so the honest figure is the mean of the *clamped* ramp over the minutes the
    /// feature is actually running, multiplied by the images each burst spawns.
    /// </summary>
    private static double? FlashSupply(SessionSettings s, int startMinute, int sessionMinutes, int availableMinutes)
    {
        var images = Math.Clamp(s.FlashImages, 1, MaxSimultaneousImages);
        return AverageFlashPerHour(s, startMinute, sessionMinutes) * images * availableMinutes / 60.0;
    }

    private static double AverageFlashPerHour(SessionSettings s, int startMinute, int sessionMinutes)
    {
        if (sessionMinutes <= 0) return 0;

        // Sampled rather than integrated: the clamp puts a kink in the line, and a loop that is
        // obviously right beats a closed form that needs its own proof.
        double sum = 0;
        var samples = 0;

        for (var minute = Math.Max(0, startMinute); minute < sessionMinutes; minute++)
        {
            var progress = (double)minute / sessionMinutes;
            var rate = s.FlashPerHour + ((s.FlashPerHourEnd - s.FlashPerHour) * progress);
            sum += Math.Clamp(rate, 1, MaxFlashPerHour);
            samples++;
        }

        return samples == 0 ? 0 : sum / samples;
    }

    /// <summary>
    /// Bubbles are credited on the pop, so the session's job is to put enough of them on screen.
    /// BubblesFrequency is per MINUTE (BubbleService schedules 60000/frequency ms), which is the
    /// one rate in this file that is not per hour.
    /// </summary>
    private static double? BubbleSupply(SessionSettings s, int startMinute, int sessionMinutes, int availableMinutes)
    {
        // BubbleService only awards a pop, and an unclickable bubble can never be popped - so a
        // session with clicking off produces exactly zero credit no matter how many it spawns.
        if (!s.BubblesClickable) return 0;

        // The intermittent path ignores BubblesFrequency entirely and spawns
        // BubblesBurstCount x BubblesPerBurst over the session (SessionEngine.ScheduleBubbleBursts).
        if (s.BubblesIntermittent)
            return (double)Math.Max(0, s.BubblesBurstCount) * Math.Max(1, s.BubblesPerBurst);

        return Math.Clamp(s.BubblesFrequency, 1, MaxBubblesPerMinute) * (double)availableMinutes;
    }

    /// <summary>
    /// Lock cards arrive at a known rate. The first card is guaranteed inside the window (#736 -
    /// LockCardService.ComputeFirstCardDelayMinutes); every later one costs a full interval.
    /// </summary>
    private static double? LockCardSupply(SessionSettings s, int startMinute, int sessionMinutes, int availableMinutes)
    {
        if (s.LockCardFrequency is not { } perHour) return null;

        var intervalMinutes = 60.0 / Math.Clamp(perHour, 1, MaxLockCardsPerHour);
        return 1 + Math.Floor(availableMinutes / intervalMinutes);
    }

    /// <summary>
    /// Bubble count games are scheduled a full interval apart with no guaranteed first game, and
    /// BubbleCountService refuses to run two inside a minute of each other.
    /// </summary>
    private static double? BubbleCountSupply(SessionSettings s, int startMinute, int sessionMinutes, int availableMinutes)
    {
        if (s.BubbleCountFrequency is not { } perHour) return null;

        var intervalMinutes = Math.Max(1.0, 60.0 / Math.Clamp(perHour, 1, MaxBubbleCountPerHour));
        return Math.Floor(availableMinutes / intervalMinutes);
    }

    /// <summary>
    /// Video is minute-denominated but event-produced: the session starts a clip VideosPerHour
    /// times an hour and credit is the playback that actually happens, so the supply is
    /// clips x <see cref="ConservativeClipMinutes"/> and NOT "every minute after the start minute".
    /// Treating it as continuous playback is what let three days ship asking for twenty to thirty
    /// minutes out of sessions that start one or two clips.
    /// </summary>
    private static double? VideoSupply(SessionSettings s, int startMinute, int sessionMinutes, int availableMinutes)
    {
        if (s.VideosPerHour is not { } perHour) return null;

        var clips = Math.Clamp(perHour, 1, MaxVideosPerHour) * availableMinutes / 60.0;
        return clips * ConservativeClipMinutes;
    }

    /// <summary>
    /// Pink filter and spiral really are continuous: AchievementService's one-second timer credits
    /// a minute per minute for as long as the feature is enabled and the overlay is running, so
    /// every minute from the (jittered) start to session end counts.
    /// </summary>
    private static double? OverlayMinuteSupply(SessionSettings s, int startMinute, int sessionMinutes, int availableMinutes)
        => availableMinutes;

    /// <summary>
    /// The settings the day will actually run, which is the only thing worth measuring against.
    ///
    /// Built through the real builder rather than by reading Floor/Ceiling by hand, because a day's
    /// <see cref="ProgramDay.Overrides"/> can switch a feature ON that the template leaves off
    /// (Presentation days 2, 8 and 9 all do), move a start minute, or switch one OFF - and a check
    /// that read the raw template judged all four cases wrong. It also means the check cannot drift
    /// from the builder's rounding, and that ApplyOverrides' JsonElement materialisation is reused
    /// rather than reimplemented (#429).
    /// </summary>
    private SessionSettings? EffectiveSettings(ProgramDay day)
    {
        var template = GetTemplate(day.SessionTemplateId);
        if (template == null) return null; // already reported by the caller

        var settings = Services.Program.ProgramSessionBuilder.LerpSettings(
            template.Floor, template.Ceiling, Math.Clamp(day.Intensity, 0.0, 1.0));

        if (day.Overrides is { Count: > 0 })
            Services.Program.ProgramSessionBuilder.ApplyOverrides(settings, day.Overrides);

        return settings;
    }

    /// <summary>
    /// #736/#747: can the day's own session actually deliver what the task asks for?
    ///
    /// Both bugs shipped because nothing asked this. Kept Day 1 required a lock card from a session
    /// that could not produce one, and Kept Day 2 required 20 minutes of pink filter from a session
    /// that only ever offered 16-21. Because those tasks are non-optional, the days were unpassable
    /// and the programs hard-blocked.
    ///
    /// Tasks flagged <see cref="ProgramTask.OutsideSession"/> are exempt - they are meant to be
    /// satisfied by the user's own dashboard time.
    /// </summary>
    internal bool IsTaskFeasible(ProgramDay day, ProgramTask task, out string why)
    {
        why = "";

        if (task.Kind != ProgramTaskKind.AutoVerified) return true;
        if (task.OutsideSession) return true;
        if (task.Verifier is not { } verifier) return true;

        return IsVerifierSatisfiable(day, verifier, task.TargetValue, "the task", out why);
    }

    /// <summary>
    /// The same budget check for the day's ambient layer. Ambient minutes were exempt from
    /// validation entirely, which is how Firmware cycles 11-14 shipped asking for sixty minutes of
    /// filter against 45-to-75-minute sessions: the day still completes, but
    /// <c>ProgramService.SettleAmbientShortfallDay</c> settles it at rollover *without the day XP*,
    /// so the user is quietly underpaid for a day they finished.
    ///
    /// Ambients flagged <see cref="ProgramAmbient.OutsideSession"/> are exempt, same as tasks.
    /// </summary>
    internal bool IsAmbientFeasible(ProgramDay day, out string why)
    {
        why = "";

        if (day.Ambient is not { RequiredMinutes: > 0 } ambient) return true;
        if (ambient.OutsideSession) return true;

        if (ambient.Verifier is not { } verifier)
        {
            why = $"it requires {ambient.RequiredMinutes} minutes but names no verifier, so nothing can " +
                  "ever credit it. Give it a verifier or mark the ambient OutsideSession.";
            return false;
        }

        return IsVerifierSatisfiable(day, verifier, ambient.RequiredMinutes, "the ambient layer", out why);
    }

    /// <summary>
    /// Shared budget check. <paramref name="subject"/> only shapes the advice at the end of the
    /// message, so a task and an ambient are told to fix themselves rather than each other.
    /// </summary>
    private bool IsVerifierSatisfiable(ProgramDay day, QuestCategory verifier, int target, string subject, out string why)
    {
        why = "";

        if (!SessionFeatureVerifiers.TryGetValue(verifier, out var feature)) return true;

        var settings = EffectiveSettings(day);
        if (settings == null) return true; // unknown template - already reported by the caller

        if (!feature.Enabled(settings))
        {
            why = $"the '{day.SessionTemplateId}' session has {feature.Name} switched off. " +
                  $"Enable it in the template, override it on for the day, retarget {subject}, or mark {subject} OutsideSession.";
            return false;
        }

        var startMinute = feature.StartMinute(settings);

        // A delayed start is only jittered when it is non-zero.
        var jitter = startMinute > 0 ? StartJitterMinutes : 0;
        var availableMinutes = day.SessionMinutes - startMinute - jitter;

        if (availableMinutes <= 0)
        {
            why = $"{feature.Name} starts at minute {startMinute} of a {day.SessionMinutes} minute session.";
            return false;
        }

        if (feature.Supply(settings, startMinute, day.SessionMinutes, availableMinutes) is not { } supply)
            return true; // not modellable - the rate falls through to the user's own dashboard value

        if (target <= supply) return true;

        var units = feature.MinuteDenominated ? $"{target} minutes of {feature.Name}" : $"{target} {feature.Name}";
        why = $"it needs {units} but the session can supply at most {Math.Floor(supply)} " +
              $"(starts minute {startMinute}, ±{StartJitterMinutes} jitter, of {day.SessionMinutes}).";
        return false;
    }
}
