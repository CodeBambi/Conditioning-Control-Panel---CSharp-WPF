using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.Program;

namespace ConditioningControlPanel.Services.Program;

/// <summary>
/// One possession the user owns, resolved for display. Built from the enrollment's banked ids and
/// the library, so it survives a restart, a restart-for-another-attempt, and the archive to History.
/// </summary>
public class ProgramRewardEntry
{
    public string RewardId { get; init; } = "";
    public string ProgramId { get; init; } = "";
    public string ProgramTitle { get; init; } = "";
    public string ChapterName { get; init; } = "";

    /// <summary>The authored reward line, verbatim. Not localized - the programs themselves are not.</summary>
    public string Description { get; init; } = "";

    /// <summary>When the grant was materialised. Null for a reward banked before grants existed.</summary>
    public DateTime? GrantedAt { get; init; }

    /// <summary>Name of the session this reward filed, when it files one.</summary>
    public string? SessionName { get; init; }

    /// <summary>Phrases this reward installs, if any.</summary>
    public IReadOnlyList<string> Phrases { get; init; } = Array.Empty<string>();
}

/// <summary>What one grant actually did. Returned so the caller can word the celebration.</summary>
public class ProgramRewardGrant
{
    /// <summary>Something was really written this pass - a session filed, phrases installed.</summary>
    public bool Materialised { get; set; }

    /// <summary>The session this reward owns, whether or not this pass had to write it.</summary>
    public string? SessionName { get; set; }

    /// <summary>The phrases this reward owns, whether or not this pass had to install them.</summary>
    public IReadOnlyList<string> Phrases { get; set; } = Array.Empty<string>();

    /// <summary>The phrases this pass actually wrote into the pool.</summary>
    public IReadOnlyList<string> InstalledPhrases { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Everything a reward grant is allowed to touch, behind one seam.
///
/// Exists so the grant RULES can be tested without a WPF app: the real implementation writes into
/// %APPDATA%\ConditioningControlPanel\CustomSessions and into the live AppSettings, neither of
/// which exists in a test host (App.Settings is null there, and writing the user's real session
/// folder from a unit test would be worse than useless).
/// </summary>
public interface IProgramRewardSurfaces
{
    /// <summary>Is this session already filed in the user's catalogue?</summary>
    bool SessionIsFiled(string sessionId);

    /// <summary>File a session permanently, so it shows up in the Sessions tab.</summary>
    void FileSession(Models.Session session);

    /// <summary>Is this phrase present in the user's subliminal pool at all (enabled or not)?</summary>
    bool PhraseIsInstalled(string phrase);

    /// <summary>
    /// Add the phrases to the user's pool. <paramref name="enableExisting"/> is true only on a first
    /// grant, where the reward line explicitly promises the phrases go live; every later pass leaves
    /// a phrase the user has switched off exactly where they put it.
    /// </summary>
    void InstallPhrases(IReadOnlyList<string> phrases, bool enableExisting);

    /// <summary>Show the celebration. Never called while materialising at startup.</summary>
    void Celebrate(string message);
}

/// <summary>
/// Turns a banked chapter reward into a thing the user actually has.
///
/// Before this existed, <c>ProgramService.CompleteChapter</c> appended the chapter's RewardId to
/// <see cref="ProgramEnrollment.BankedRewards"/> and nothing anywhere read the list back, so all
/// thirteen authored reward lines promised possessions that were never created.
///
/// A separate service rather than more of ProgramService, on purpose. ProgramService is the day
/// clock and the ledger; granting reaches into the session catalogue, the subliminal pool and the
/// toast surface, none of which the clock has any business knowing about - and all three of which
/// have to be faked to test the rules at all. Every method that does real work also has an overload
/// taking the state explicitly, so the rules are exercisable with no ProgramService in the room.
///
/// What each reward grants is authored on the chapter (<see cref="ProgramChapter.RewardSavesFinalSession"/>,
/// <see cref="ProgramChapter.RewardPhrases"/>), never switched on the reward id here - a community
/// program making the same promise gets the same grant.
/// </summary>
public class ProgramRewardService : IDisposable
{
    private readonly ProgramService? _programs;
    private readonly IProgramRewardSurfaces _surfaces;
    private bool _disposed;

    /// <summary>Prefix on every filed reward session's id.</summary>
    public const string SessionIdPrefix = "program_reward_";

    public ProgramRewardService(ProgramService? programs = null, IProgramRewardSurfaces? surfaces = null)
    {
        _programs = programs ?? App.Programs;
        _surfaces = surfaces ?? new AppProgramRewardSurfaces();

        if (_programs == null) return;

        _programs.ChapterCompleted += OnChapterCompleted;

        // Catch up on everything already banked. Covers three cases that look identical from here:
        // rewards banked by a build where nothing consumed the list, a programs.json restored onto a
        // machine that has never held the session file, and a user who deleted what was filed.
        // Silent by construction - see MaterialiseBanked.
        try { MaterialiseBanked(); }
        catch (Exception ex) { App.Logger?.Warning(ex, "Program reward catch-up failed"); }
    }

    // -------------------------------------------------------------------------------------------
    // Read model
    // -------------------------------------------------------------------------------------------

    /// <summary>Everything the user owns, newest enrollment first, deduplicated by reward id.</summary>
    public IReadOnlyList<ProgramRewardEntry> GetBankedRewards() =>
        GetBankedRewards(_programs?.State, id => _programs?.GetProgram(id));

    /// <summary>
    /// The active run and the history are both walked: a graduated run's possessions are the whole
    /// point of having finished it, and it moves to History the moment the panel is dismissed.
    /// </summary>
    public static IReadOnlyList<ProgramRewardEntry> GetBankedRewards(
        ProgramState? state, Func<string, ProgramDefinition?> lookup)
    {
        var entries = new List<ProgramRewardEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var enrollment in Enrollments(state))
        {
            var program = lookup(enrollment.ProgramId);
            if (program == null) continue;

            foreach (var rewardId in enrollment.BankedRewards)
            {
                if (string.IsNullOrWhiteSpace(rewardId)) continue;
                if (!seen.Add(rewardId)) continue;

                var chapter = FindChapter(program, rewardId);
                if (chapter == null) continue;

                entries.Add(new ProgramRewardEntry
                {
                    RewardId = rewardId,
                    ProgramId = program.Id,
                    ProgramTitle = program.Title,
                    ChapterName = chapter.Name,
                    Description = chapter.RewardDescription ?? "",
                    GrantedAt = enrollment.RewardGrantedAt.TryGetValue(rewardId, out var at) ? at : null,
                    SessionName = chapter.RewardSavesFinalSession
                        ? BankedSessionName(program, FinalDay(chapter))
                        : null,
                    Phrases = chapter.RewardPhrases.ToList()
                });
            }
        }

        return entries;
    }

    // -------------------------------------------------------------------------------------------
    // Granting
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Ensure every banked reward on every enrollment is materialised. Idempotent, and deliberately
    /// SILENT: this runs at startup, where a toast per chapter of a finished 28-day program would be
    /// a wall of confetti for possessions the user has had for weeks.
    /// </summary>
    /// <returns>How many rewards this pass had to materialise.</returns>
    public int MaterialiseBanked() =>
        MaterialiseBanked(_programs?.State, id => _programs?.GetProgram(id));

    /// <inheritdoc cref="MaterialiseBanked()"/>
    public int MaterialiseBanked(ProgramState? state, Func<string, ProgramDefinition?> lookup)
    {
        var materialised = 0;

        foreach (var enrollment in Enrollments(state))
        {
            var program = lookup(enrollment.ProgramId);
            if (program == null) continue;

            // Copied: the grant writes to the enrollment, so never enumerate the live list.
            foreach (var rewardId in enrollment.BankedRewards.ToList())
            {
                if (string.IsNullOrWhiteSpace(rewardId)) continue;

                var chapter = FindChapter(program, rewardId);
                if (chapter == null)
                {
                    App.Logger?.Debug("Banked reward '{Reward}' has no chapter in {Program} - skipped",
                        rewardId, program.Id);
                    continue;
                }

                var firstGrant = !enrollment.RewardGrantedAt.ContainsKey(rewardId);
                if (Grant(program, chapter, enrollment, firstGrant).Materialised) materialised++;
            }
        }

        if (materialised > 0)
            App.Logger?.Information("Program rewards: materialised {Count} banked reward(s)", materialised);

        return materialised;
    }

    /// <summary>
    /// Hand the user what the chapter promised.
    ///
    /// <paramref name="firstGrant"/> separates the moment the reward is earned from every later
    /// re-materialisation. Only a first grant may switch a phrase the user had turned OFF back on -
    /// the reward line says the phrases go live, and that is true exactly once. Later passes may
    /// only re-add a phrase that is missing entirely, so a launch can never re-enable something the
    /// user deliberately silenced.
    /// </summary>
    public ProgramRewardGrant Grant(
        ProgramDefinition program, ProgramChapter chapter, ProgramEnrollment? enrollment, bool firstGrant)
    {
        var grant = new ProgramRewardGrant();
        if (program == null || chapter == null) return grant;

        var rewardId = chapter.RewardId;
        if (string.IsNullOrWhiteSpace(rewardId)) return grant;

        if (chapter.RewardSavesFinalSession) FileRewardSession(program, chapter, rewardId!, grant);
        if (chapter.RewardPhrases.Count > 0) InstallRewardPhrases(chapter, rewardId!, firstGrant, grant);

        // Stamped even for a reward whose whole grant is the toast and the ledger line - the
        // possessions list wants a date on every row, not only the ones that wrote a file.
        if (enrollment != null && !enrollment.RewardGrantedAt.ContainsKey(rewardId!))
        {
            enrollment.RewardGrantedAt[rewardId!] = DateTime.Now;
            _programs?.MarkDirty();
        }

        return grant;
    }

    private void FileRewardSession(
        ProgramDefinition program, ProgramChapter chapter, string rewardId, ProgramRewardGrant grant)
    {
        var day = FinalDay(chapter);
        if (day == null)
        {
            App.Logger?.Warning("Chapter '{Chapter}' promises a saved session but has no days", chapter.Id);
            return;
        }

        // Reported whether or not this pass had to write it: the toast is telling the user what they
        // now own, and "you own it, and it was already there" is the same sentence.
        grant.SessionName = BankedSessionName(program, day);

        var sessionId = SessionIdPrefix + rewardId;
        if (_surfaces.SessionIsFiled(sessionId)) return;

        try
        {
            var session = BuildBankedSession(program, day, sessionId, grant.SessionName);
            _surfaces.FileSession(session);
            grant.Materialised = true;

            App.Logger?.Information("Program reward '{Reward}' filed session '{Name}' ({Id})",
                rewardId, session.Name, session.Id);
        }
        catch (Exception ex)
        {
            // The reward stays banked and the next launch tries again. Losing the file must never
            // lose the possession.
            App.Logger?.Warning(ex, "Program reward '{Reward}' could not file its session", rewardId);
        }
    }

    private void InstallRewardPhrases(
        ProgramChapter chapter, string rewardId, bool firstGrant, ProgramRewardGrant grant)
    {
        var authored = chapter.RewardPhrases.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        grant.Phrases = authored;

        var pending = firstGrant
            ? authored
            : authored.Where(p => !_surfaces.PhraseIsInstalled(p)).ToList();

        if (pending.Count == 0) return;

        try
        {
            _surfaces.InstallPhrases(pending, enableExisting: firstGrant);
            grant.InstalledPhrases = pending;
            grant.Materialised = true;

            App.Logger?.Information("Program reward '{Reward}' installed {Count} phrase(s)",
                rewardId, pending.Count);
        }
        catch (Exception ex)
        {
            App.Logger?.Warning(ex, "Program reward '{Reward}' could not install its phrases", rewardId);
        }
    }

    private void OnChapterCompleted(object? sender, ProgramChapterEventArgs e)
    {
        try
        {
            if (e?.Chapter == null || string.IsNullOrWhiteSpace(e.RewardId)) return;

            // The enrollment already held this - a later attempt re-finishing a chapter it banked on
            // an earlier one. No grant, and no toast: congratulating someone on a possession they
            // have had since attempt one reads as a bug, because it is one.
            if (e.AlreadyBanked) return;

            var grant = Grant(e.Program, e.Chapter, _programs?.ActiveEnrollment, firstGrant: true);
            _surfaces.Celebrate(CelebrationText(e.Program, e.Chapter, grant));
        }
        catch (Exception ex)
        {
            App.Logger?.Warning(ex, "Program chapter reward grant failed");
        }
    }

    /// <summary>
    /// The celebration copy. One toast, one line per thing the user now has - the authored reward
    /// line first (it is the program's own voice), then the concrete surfaces they can go and open.
    /// </summary>
    public static string CelebrationText(
        ProgramDefinition program, ProgramChapter chapter, ProgramRewardGrant grant)
    {
        var lines = new List<string>
        {
            Loc.GetF("programs_reward_toast", program.Title, chapter.RewardDescription ?? chapter.Name)
        };

        if (!string.IsNullOrWhiteSpace(grant.SessionName))
            lines.Add(Loc.GetF("programs_reward_toast_session", grant.SessionName!));

        if (grant.Phrases.Count > 0)
            lines.Add(Loc.GetF("programs_reward_toast_phrases", string.Join(", ", grant.Phrases)));

        return string.Join("\n", lines);
    }

    // -------------------------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------------------------

    private static IEnumerable<ProgramEnrollment> Enrollments(ProgramState? state)
    {
        if (state == null) yield break;

        if (state.Active != null) yield return state.Active;

        // Newest first, so a reward banked by two runs of the same program reports the most recent
        // grant date rather than the oldest.
        for (var i = state.History.Count - 1; i >= 0; i--)
            yield return state.History[i];
    }

    private static ProgramChapter? FindChapter(ProgramDefinition program, string rewardId) =>
        program.Chapters.FirstOrDefault(c =>
            !string.IsNullOrWhiteSpace(c.RewardId) &&
            string.Equals(c.RewardId, rewardId, StringComparison.OrdinalIgnoreCase));

    private static ProgramDay? FinalDay(ProgramChapter chapter) =>
        chapter.Days.Count == 0 ? null : chapter.Days.OrderByDescending(d => d.DayIndex).First();

    /// <summary>
    /// "Kept · Day 28". Names the PROGRAM, not the template, because the user meets this in a
    /// Sessions list beside their own work and "Program · Day 28" would say nothing about which
    /// program it came from.
    ///
    /// Screened through the same reserved-substring check the transient program session uses:
    /// AchievementService.TrackSessionComplete identifies built-in sessions by lowercase substring,
    /// so a program whose title contained one would falsely unlock an achievement on every replay.
    /// </summary>
    public static string BankedSessionName(ProgramDefinition program, ProgramDay? day)
    {
        if (day == null) return program.Title;

        var name = $"{program.Title} · Day {day.DayIndex}";
        if (!ProgramSessionBuilder.ContainsReserved(name)) return name;

        return $"{program.Id} · Day {day.DayIndex}";
    }

    /// <summary>
    /// The chapter's final day, built at its authored intensity and re-badged as a keepsake.
    ///
    /// The id is deliberately NOT the builder's <c>program_&lt;id&gt;_day&lt;n&gt;</c>. ProgramService
    /// pins that id in <c>_expectedSessionId</c> to decide whether a finishing session should tick
    /// today's slot, so a saved copy carrying it could credit a program day just for replaying a
    /// keepsake - the exact opposite of what a keepsake is for.
    /// </summary>
    public static Models.Session BuildBankedSession(
        ProgramDefinition program, ProgramDay day, string sessionId, string name)
    {
        var session = ProgramSessionBuilder.Build(program, day);
        session.Id = sessionId;
        session.Name = name;
        session.Source = SessionSource.Custom;
        return session;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_programs != null) _programs.ChapterCompleted -= OnChapterCompleted;
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// The real surfaces. Each write reuses the path the app already has for that job rather than
/// inventing a second one - the session goes through SessionFileService and the same
/// RegisterExternallySavedSession hop the Graded Intake uses (#614), and the phrase install ASSIGNS
/// the pool rather than mutating it, because only an assignment raises INPC.
/// </summary>
internal sealed class AppProgramRewardSurfaces : IProgramRewardSurfaces
{
    public bool SessionIsFiled(string sessionId)
    {
        try { return File.Exists(PathFor(sessionId)); }
        catch (Exception ex)
        {
            // An unreadable folder must not make us re-file forever, nor claim the file is there.
            App.Logger?.Debug("Program reward session probe failed: {Error}", ex.Message);
            return false;
        }
    }

    public void FileSession(Models.Session session)
    {
        var fileService = new SessionFileService();
        fileService.EnsureCustomFolderExists();

        var path = PathFor(session.Id);
        session.Source = SessionSource.Custom;
        session.SourceFilePath = path;
        fileService.ExportSession(session, path);

        // TELL THE RUNNING APP, NOT JUST THE DISK. SessionManager.LoadAllSessions runs exactly once
        // per launch, so a file dropped into CustomSessions afterwards is invisible in the Sessions
        // tab until the next start - which reads as "the reward did nothing" (#614, same trap).
        //
        // Separate try/catch: the file is already safely written, and a list refresh that cannot
        // happen (no MainWindow yet at startup, or a torn-down one) must never be reported as a
        // failed grant.
        try
        {
            var main = App.MainWindowRef;
            if (main != null) main.RegisterExternallySavedSession(session, path);
            else App.Logger?.Debug(
                "Program reward session '{Name}' filed before MainWindow exists; the Sessions tab "
                + "picks it up when it loads.", session.Name);
        }
        catch (Exception ex)
        {
            App.Logger?.Warning(ex, "Program reward session saved but the Sessions list refresh failed");
        }
    }

    public bool PhraseIsInstalled(string phrase)
    {
        var pool = App.Settings?.Current?.SubliminalPool;
        return pool != null && pool.ContainsKey(phrase);
    }

    public void InstallPhrases(IReadOnlyList<string> phrases, bool enableExisting)
    {
        var settings = App.Settings?.Current;
        if (settings == null || phrases == null || phrases.Count == 0) return;

        var pool = new Dictionary<string, bool>(settings.SubliminalPool ?? new Dictionary<string, bool>());
        var changed = false;

        foreach (var phrase in phrases)
        {
            if (string.IsNullOrWhiteSpace(phrase)) continue;

            if (!pool.TryGetValue(phrase, out var enabled))
            {
                pool[phrase] = true;
                changed = true;
            }
            else if (enableExisting && !enabled)
            {
                pool[phrase] = true;
                changed = true;
            }
        }

        if (!changed) return;

        // ASSIGN, never mutate in place. The assignment is what raises INPC, and INPC is what makes
        // ModService fold the edit into a running session's restore snapshot (#906) and mirror it
        // into the active mod's per-mod backup. Mutating settings.SubliminalPool directly would
        // install phrases that the end of the session, or the next launch, silently reverted.
        settings.SubliminalPool = pool;
    }

    public void Celebrate(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        // The toast surface builds WPF elements, and a chapter can complete off the UI thread
        // (a rollover tick, a deferred day settle). RunOnUI no-ops during shutdown.
        DispatcherHelper.RunOnUI(() =>
            App.Notifications?.Show(message, NotificationType.Success, TimeSpan.FromSeconds(12)));
    }

    /// <summary>
    /// Deterministic, so "have I already filed this?" is one File.Exists rather than a full parse of
    /// every session in the folder. Reward ids are authored identifiers (lower case, underscores),
    /// so the id is already a legal file name; anything else is scrubbed rather than trusted.
    /// </summary>
    private static string PathFor(string sessionId)
    {
        var safe = string.Join("_",
            sessionId.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        return Path.Combine(SessionFileService.CustomSessionsFolder, safe + ".session.json");
    }
}
