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
        }

        return true;
    }

    /// <summary>
    /// The ±3 minute jitter <c>SessionEngine.RandomizeStartTimes</c> adds to delayed starts. A task
    /// that only just fits on paper fails a third of the time once the randomiser moves the start
    /// later, so the budget is checked against the worst case.
    /// </summary>
    private const int StartJitterMinutes = 3;

    /// <summary>Which session feature a verifier draws its credit from.</summary>
    /// <param name="Name">Human name for the error message.</param>
    /// <param name="MinuteDenominated">True when TargetValue counts minutes of runtime rather than events.</param>
    private readonly record struct VerifierFeature(
        string Name,
        bool MinuteDenominated,
        Func<SessionSettings, bool> Enabled,
        Func<SessionSettings, int> StartMinute);

    private static readonly Dictionary<QuestCategory, VerifierFeature> SessionFeatureVerifiers = new()
    {
        [QuestCategory.Flash] = new("flash images", false, s => s.FlashEnabled, s => s.FlashStartMinute),
        [QuestCategory.Video] = new("mandatory videos", true, s => s.MandatoryVideosEnabled, s => s.MandatoryVideosStartMinute),
        [QuestCategory.Spiral] = new("spiral", true, s => s.SpiralEnabled, s => s.SpiralStartMinute),
        [QuestCategory.PinkFilter] = new("pink filter", true, s => s.PinkFilterEnabled, s => s.PinkFilterStartMinute),
        [QuestCategory.Bubbles] = new("bubbles", false, s => s.BubblesEnabled, s => s.BubblesStartMinute),
        [QuestCategory.LockCard] = new("lock cards", false, s => s.LockCardEnabled, s => s.LockCardStartMinute),
        [QuestCategory.BubbleCount] = new("bubble count", false, s => s.BubbleCountEnabled, s => s.BubbleCountStartMinute),
    };

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
        if (!SessionFeatureVerifiers.TryGetValue(verifier, out var feature)) return true;

        var template = GetTemplate(day.SessionTemplateId);
        if (template == null) return true; // already reported by the caller

        // Booleans come from Floor - which features a template runs is the template's identity.
        if (!feature.Enabled(template.Floor))
        {
            why = $"the '{template.Id}' session has {feature.Name} switched off. " +
                  "Enable it in the template, retarget the task, or mark the task OutsideSession.";
            return false;
        }

        var startMinute = Services.Program.ProgramSessionBuilder.LerpInt(
            feature.StartMinute(template.Floor), feature.StartMinute(template.Ceiling), day.Intensity);

        // A delayed start is only jittered when it is non-zero.
        var jitter = startMinute > 0 ? StartJitterMinutes : 0;
        var availableMinutes = day.SessionMinutes - startMinute - jitter;

        if (availableMinutes <= 0)
        {
            why = $"{feature.Name} starts at minute {startMinute} of a {day.SessionMinutes} minute session.";
            return false;
        }

        if (feature.MinuteDenominated && task.TargetValue > availableMinutes)
        {
            why = $"it needs {task.TargetValue} minutes of {feature.Name} but the session offers at most " +
                  $"{availableMinutes} (starts minute {startMinute}, ±{StartJitterMinutes} jitter, of {day.SessionMinutes}).";
            return false;
        }

        // Lock cards arrive at a known rate, so an event count can be checked too. The first card is
        // guaranteed inside the window (#736); every later one costs a full interval.
        if (verifier == QuestCategory.LockCard && task.TargetValue > 1)
        {
            var perHour = template.Floor.LockCardFrequency;
            if (perHour is > 0)
            {
                var intervalMinutes = 60.0 / perHour.Value;
                var achievable = 1 + (int)Math.Floor(availableMinutes / intervalMinutes);
                if (task.TargetValue > achievable)
                {
                    why = $"it needs {task.TargetValue} lock cards but {perHour}/hour over {availableMinutes} " +
                          $"minutes can deliver at most {achievable}.";
                    return false;
                }
            }
        }

        return true;
    }
}
