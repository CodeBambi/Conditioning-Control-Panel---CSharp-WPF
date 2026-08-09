using System;
using System.Linq;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Companion.Brain;
using ConditioningControlPanel.Services.Deeper;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Single seam between feature modules and the achievement system. The bridge
/// SUBSCRIBES to events that feature services already raise and translates them
/// into achievement tracking (counter bumps on <see cref="AchievementService.Progress"/>
/// plus TryUnlock / TryUnlockExclusive). This is the ONLY place new gamification
/// wiring lives — feature modules are not allowed to call Track*/AddXP directly
/// (the sole exception is a handful of new EMIT events the modules raise, which the
/// bridge then consumes the same way).
///
/// Patron-exclusive achievements go through <see cref="AchievementService.TryUnlockExclusive"/>
/// so they only unlock for entitled users; everything cosmetic, no XP/skill points.
/// </summary>
public class GamificationBridge : IDisposable
{
    private bool _started;

    // --- tunable thresholds (chosen here, flagged for review) ---
    private const int BestFriendsCompanionLevel = 25;   // "reach a companion level milestone"
    private const int PillowTalkMessages = 100;          // "exchange 100 messages"
    private const int PavlovKeywordTriggers = 500;       // "fire 500 keyword triggers"
    private const int CuratorDistinctMods = 10;          // "activate 10 different mods"
    private const int MadScientistRules = 5;             // "build using 5+ triggers" (Rules)
    private const int PuppetStringsCommands = 100;        // "100 remote commands in one session"
    private const int ThrowAwayKeyMinutes = 60;           // "60+ minute lockdown"
    private const int CommunityModsCount = 3;             // "activate 3 community mods"
    private const int DownTheRabbitHolePlays = 25;        // "play 25 enhancements"
    private const int OnRailsTriggerTypes = 5;            // "5+ distinct trigger types"
    private const int HandsFreeGazePops = 50;             // "pop 50 bubbles by gaze"
    private const int HonorRollCategories = 3;            // "top marks in 3 different categories"
    private const int TeachersPetPasses = 25;             // "pass 25 graded runs"
    private const int HeldBackFailStreak = 3;             // "fail 3 in a row" (classic quiz only)

    /// <summary>
    /// Wire up all subscriptions. Safe to call once; idempotent. Must run after the
    /// feature services it references have been constructed (late in App.OnStartup).
    /// </summary>
    public void Start()
    {
        if (_started) return;
        _started = true;

        try
        {
            // ----- Keyword triggers (free: magic_word, pavlov) -----
            if (App.KeywordTriggers != null)
                App.KeywordTriggers.TriggerFired += OnKeywordTriggerFired;

            // ----- Companion (free: best_friends via level, pleased_to_meet_you / pillow_talk via chat) -----
            if (App.Companion != null)
            {
                App.Companion.CompanionLevelUp += OnCompanionLevelUp;
                App.Companion.UserMessageSent += OnCompanionMessageSent;
            }

            // ----- Mods (free: modder via install, curator + community_supported via activation) -----
            if (App.Mods != null)
            {
                App.Mods.ModChanged += OnModChanged;
                App.Mods.ModInstalled += OnModInstalled;
            }

            // ----- Deeper editor saves (free: not_a_video_editor, mad_scientist) -----
            TutorialEventBus.Event += OnTutorialEvent;

            // ----- Deeper playback (free: going_deeper, down_the_rabbit_hole, on_rails, wired_in, dont_look_away, directors_cut) -----
            if (App.DeeperHost != null)
                App.DeeperHost.EnhancementCompleted += OnEnhancementCompleted;

            // ----- Gaze pops (patron: hands_free) -----
            if (App.GazeFocus != null)
                App.GazeFocus.GazePopped += OnGazePopped;

            // ----- Catalogue publish (free: on_the_shelf) -----
            if (App.Catalogue != null)
                App.Catalogue.SubmissionSucceeded += OnCatalogueSubmitted;

            // ----- Graded runs (patron: top_of_the_class, teachers_pet, honor_roll, held_back) -----
            // Raised by IntakeHostService on a completed Graded Intake, and still by QuizWindow
            // for the classic quiz (whose launcher is hidden but whose handler is intact).
            QuizService.QuizCompleted += OnQuizCompleted;

            // ----- Local AI persistent memory (patron: she_remembers) -----
            LocalAiService.PersistentMemoryRecalled += OnPersistentMemoryRecalled;

            // ----- Blink trainer (patron: blink_and_youll_miss_it) -----
            if (App.Webcam != null)
                App.Webcam.OnBlink += OnWebcamBlink;

            // ----- Lockdown (patron: locked_in, throw_away_the_key) -----
            if (App.Lockdown != null)
            {
                App.Lockdown.LockdownActivated += OnLockdownActivated;
                App.Lockdown.LockdownDeactivated += OnLockdownDeactivated;
            }

            // ----- Remote control (patron: hand_over_control, puppet_strings) -----
            if (App.RemoteControl != null)
            {
                App.RemoteControl.SessionStarted += OnRemoteSessionStarted;
                App.RemoteControl.SessionEnded += OnRemoteSessionEnded;
                App.RemoteControl.CommandReceived += OnRemoteCommand;
            }

            // Retroactive: best_friends only fires on the CompanionLevelUp *event*, so a
            // user who already maxed their companion(s) before this achievement existed (or
            // before the bridge subscribed) never re-triggers it (#308). Check current
            // companion levels once at startup and unlock if the milestone is already met.
            CheckExistingCompanionMilestone();

            // Retroactive: the companion-chat counter was fed by exactly one call site (the
            // tube's legacy send handler), so every message that went through the modern brain
            // funnel — and everything ever typed in Her Room — counted for nothing (#877).
            // Long-time chatters would otherwise start over from zero, so put a BEST-EFFORT
            // FLOOR under the counter once, from the little the companion happened to persist.
            // It does not recover the true history (nothing on disk can) — see the method
            // summary for exactly how far it reaches and what it cannot restore.
            BackfillCompanionChatCount();

            App.Logger?.Information("GamificationBridge started — achievement subscriptions wired");
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "GamificationBridge failed to start");
        }
    }

    public void Stop()
    {
        if (!_started) return;
        _started = false;

        try
        {
            if (App.KeywordTriggers != null)
                App.KeywordTriggers.TriggerFired -= OnKeywordTriggerFired;

            if (App.Companion != null)
            {
                App.Companion.CompanionLevelUp -= OnCompanionLevelUp;
                App.Companion.UserMessageSent -= OnCompanionMessageSent;
            }

            if (App.Mods != null)
            {
                App.Mods.ModChanged -= OnModChanged;
                App.Mods.ModInstalled -= OnModInstalled;
            }

            TutorialEventBus.Event -= OnTutorialEvent;

            if (App.DeeperHost != null)
                App.DeeperHost.EnhancementCompleted -= OnEnhancementCompleted;

            if (App.GazeFocus != null)
                App.GazeFocus.GazePopped -= OnGazePopped;

            if (App.Catalogue != null)
                App.Catalogue.SubmissionSucceeded -= OnCatalogueSubmitted;

            QuizService.QuizCompleted -= OnQuizCompleted;

            LocalAiService.PersistentMemoryRecalled -= OnPersistentMemoryRecalled;

            if (App.Webcam != null)
                App.Webcam.OnBlink -= OnWebcamBlink;

            if (App.Lockdown != null)
            {
                App.Lockdown.LockdownActivated -= OnLockdownActivated;
                App.Lockdown.LockdownDeactivated -= OnLockdownDeactivated;
            }

            if (App.RemoteControl != null)
            {
                App.RemoteControl.SessionStarted -= OnRemoteSessionStarted;
                App.RemoteControl.SessionEnded -= OnRemoteSessionEnded;
                App.RemoteControl.CommandReceived -= OnRemoteCommand;
            }
        }
        catch (Exception ex)
        {
            App.Logger?.Warning(ex, "GamificationBridge failed to stop cleanly");
        }
    }

    // ===================== handlers =====================

    private static AchievementService? Ach => App.Achievements;
    private static AchievementProgress? Prog => App.Achievements?.Progress;

    private void OnKeywordTriggerFired(object? sender, KeywordTrigger e)
    {
        try
        {
            var p = Prog; if (p == null) return;
            p.KeywordTriggersFired++;
            Ach?.MarkDirty();
            Ach?.TryUnlock("magic_word");
            if (p.KeywordTriggersFired >= PavlovKeywordTriggers)
                Ach?.TryUnlock("pavlov");
        }
        catch (Exception ex) { App.Logger?.Warning(ex, "GamificationBridge: keyword handler failed"); }
    }

    private void OnCompanionLevelUp(object? sender, (CompanionId Companion, int NewLevel) e)
    {
        try
        {
            if (e.NewLevel >= BestFriendsCompanionLevel)
                Ach?.TryUnlock("best_friends");
        }
        catch (Exception ex) { App.Logger?.Warning(ex, "GamificationBridge: companion level handler failed"); }
    }

    /// <summary>
    /// Unlock best_friends retroactively if any companion already sits at/above the
    /// milestone level. The CompanionLevelUp event only fires on a transition, so
    /// companions maxed before this achievement (or before the bridge subscribed)
    /// would otherwise never award it (#308). TryUnlock is idempotent.
    /// </summary>
    private void CheckExistingCompanionMilestone()
    {
        try
        {
            var data = App.Settings?.Current?.CompanionProgressData;
            if (data == null) return;
            foreach (var progress in data.Values)
            {
                if (progress != null && progress.Level >= BestFriendsCompanionLevel)
                {
                    Ach?.TryUnlock("best_friends");
                    return;
                }
            }
        }
        catch (Exception ex) { App.Logger?.Warning(ex, "GamificationBridge: retroactive companion milestone check failed"); }
    }

    /// <summary>
    /// One-shot BEST-EFFORT floor under <see cref="AchievementProgress.CompanionMessages"/> (#877).
    /// This is not a reconstruction of the true history — that history was never written down, and
    /// nothing on disk can recover it. Read the limits below before trusting the number.
    ///
    /// <para>Two records of "the user talked to her" exist on disk, and the higher wins. NEITHER is
    /// a full count:</para>
    /// <list type="bullet">
    /// <item>the restored turn log — exact for what it holds, but a rolling window
    /// (<see cref="CompanionSessionStore.MaxPersistedTurns"/> keeps the last 100 turns, i.e. ~50
    /// user turns), so it is a FLOOR, never the whole history;</item>
    /// <item><see cref="MemoryStore"/>'s per-mod relationship counters — uncapped, but NOT an
    /// independent source: <c>MemorySignalWriter</c> increments them from the very same
    /// <c>CompanionService.UserMessageSent</c> event that fed <c>CompanionMessages</c>, and it only
    /// started doing so when the brain shipped. So it under-counts by at least as much as the
    /// counter it is meant to repair (relationship turns &lt;= CompanionMessages in practice), and
    /// the <see cref="Math.Max"/> below almost always collapses to the turn-log floor.</item>
    /// </list>
    ///
    /// <para>Consequences, stated plainly so nobody re-derives them from a wrong comment:
    /// the realistic ceiling of this backfill is the ~50-turn window, so
    /// <c>pleased_to_meet_you</c> (1 message) does get restored for anyone who ever chatted, and
    /// <c>pillow_talk</c> (<see cref="PillowTalkMessages"/>) will NOT be backfilled for anyone — it
    /// accrues live from 6.7.5 onward now that <c>CompanionBrain.ChatAsync</c> raises the signal
    /// for every surface. Long-time chatters are therefore still short of it, and that is a known,
    /// accepted loss rather than something this method is failing to do.</para>
    ///
    /// <para>The counter is only ever raised, never lowered: a user who already earned messages the
    /// honest way cannot be demoted by a thinner record. Both sources are silent when the user has
    /// chat memory switched off, which is correct — that toggle exists precisely so those turns are
    /// not on disk to be counted.</para>
    ///
    /// <para>Latched via <see cref="AchievementProgress.CompanionChatBackfilled"/>. The latch is set
    /// only when there was a brain to read AND at least one of the two reads came back without
    /// throwing: with the kill switch off, a brain that failed to construct, or both reads faulted,
    /// there is no evidence at all, and burning the one-shot on that would leave the user
    /// permanently un-backfilled. Running every launch instead of once is not an option — the
    /// window source would drag the counter back down to ~50 forever.</para>
    /// </summary>
    private void BackfillCompanionChatCount()
    {
        try
        {
            var p = Prog; if (p == null || p.CompanionChatBackfilled) return;

            var brain = App.Brain;
            if (brain == null) return; // no source yet — try again next launch

            // Track whether we actually managed to LOOK. A read that throws is not evidence of
            // "nothing to find" — if both fault we have seen nothing at all, and burning the
            // one-shot latch on that would strand the user un-backfilled forever.
            var readSomething = false;

            var restoredTurns = 0;
            try
            {
                restoredTurns = brain.Session.Turns.Count(t => t != null && t.Kind == TurnKind.UserChat);
                readSomething = true;
            }
            catch (Exception ex) { App.Logger?.Debug("GamificationBridge: turn-log backfill read failed: {Error}", ex.Message); }

            var relationshipTurns = 0;
            try
            {
                if (brain.Memory is MemoryStore store)
                    relationshipTurns = store.Relationships.Values.Sum(r => r?.ChatTurnsTotal ?? 0);
                readSomething = true;   // a non-MemoryStore memory is a real answer: no such record
            }
            catch (Exception ex) { App.Logger?.Debug("GamificationBridge: relationship backfill read failed: {Error}", ex.Message); }

            if (!readSomething)
            {
                App.Logger?.Debug("GamificationBridge: chat backfill saw no readable source — latch left unset");
                return; // try again next launch
            }

            p.CompanionChatBackfilled = true;

            var evidence = Math.Max(restoredTurns, relationshipTurns);
            if (evidence > p.CompanionMessages)
            {
                App.Logger?.Information(
                    "GamificationBridge: companion chat count backfilled {Old} -> {New} (turns={Turns}, relationship={Rel})",
                    p.CompanionMessages, evidence, restoredTurns, relationshipTurns);
                p.CompanionMessages = evidence;
            }

            Ach?.MarkDirty();

            // pleased_to_meet_you (1 message) is the one this reliably restores. The pillow_talk
            // check is kept only so a user whose counter was ALREADY at or past the bar isn't left
            // holding an unlocked-but-unawarded achievement — the backfill itself cannot get anyone
            // there (see the summary: the evidence ceiling is the ~50-turn window).
            if (p.CompanionMessages > 0)
                Ach?.TryUnlock("pleased_to_meet_you");
            if (p.CompanionMessages >= PillowTalkMessages)
                Ach?.TryUnlock("pillow_talk");
        }
        catch (Exception ex) { App.Logger?.Warning(ex, "GamificationBridge: companion chat backfill failed"); }
    }

    private void OnCompanionMessageSent(object? sender, EventArgs e)
    {
        // Since #877 the signal is raised from inside CompanionBrain.ChatAsync, past an awaited
        // semaphore — under gate contention that resumes on a threadpool thread, so this handler
        // is no longer guaranteed to be on the UI thread. Marshal before touching progression
        // state or the unlock/popup path (same reason as OnPersistentMemoryRecalled below).
        // Marshalled HERE rather than at the emit so every other subscriber (barks,
        // MemorySignalWriter) keeps its send-time ordering; RunOnUI runs inline when we are
        // already on the UI thread, which is the common case.
        DispatcherHelper.RunOnUI(() =>
        {
            try
            {
                var p = Prog; if (p == null) return;
                p.CompanionMessages++;
                Ach?.MarkDirty();
                Ach?.TryUnlock("pleased_to_meet_you");
                if (p.CompanionMessages >= PillowTalkMessages)
                    Ach?.TryUnlock("pillow_talk");
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "GamificationBridge: companion message handler failed"); }
        });
    }

    private void OnModChanged(object? sender, ModPackage mod)
    {
        try
        {
            var p = Prog; if (p == null || mod == null) return;

            var isNewDistinct = p.ActivatedModIds.Add(mod.Id);

            // "community_supported" — running community (non-builtin) mods. Authorship
            // can't be determined (created-mod ids aren't persisted anywhere), so this
            // counts distinct community mods activated rather than a fake author check.
            var isNewCommunity = !mod.IsBuiltIn && p.CommunityModIds.Add(mod.Id);
            Ach?.MarkDirty();

            // "curator" — activate N different mods (distinct ids, builtin or not)
            if (isNewDistinct && p.ActivatedModIds.Count >= CuratorDistinctMods)
                Ach?.TryUnlock("curator");

            // "community_supported" — activate N distinct community mods
            if (isNewCommunity && p.CommunityModIds.Count >= CommunityModsCount)
                Ach?.TryUnlock("community_supported");
        }
        catch (Exception ex) { App.Logger?.Warning(ex, "GamificationBridge: mod changed handler failed"); }
    }

    private void OnModInstalled(object? sender, ModPackage mod)
    {
        try
        {
            var p = Prog; if (p == null) return;
            p.ModsInstalled++;
            Ach?.MarkDirty();
            Ach?.TryUnlock("modder");
        }
        catch (Exception ex) { App.Logger?.Warning(ex, "GamificationBridge: mod install handler failed"); }
    }

    private void OnTutorialEvent(object? sender, string name)
    {
        if (name != "FileSaved") return;
        try
        {
            var p = Prog; if (p == null) return;
            p.EnhancementsBuilt++;
            Ach?.MarkDirty();
            Ach?.TryUnlock("not_a_video_editor");

            // "mad_scientist" — built with 5+ triggers. FileSaved carries no count, so
            // read the just-saved file and count its rules (each rule has a trigger).
            var path = TutorialEventBus.LastSavedEnhancementPath;
            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    var enh = EnhancementSerializer.LoadFromFile(path);
                    if (enh?.Rules != null && enh.Rules.Count >= MadScientistRules)
                        Ach?.TryUnlock("mad_scientist");
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug(ex, "GamificationBridge: could not read saved enhancement for mad_scientist");
                }
            }
        }
        catch (Exception ex) { App.Logger?.Warning(ex, "GamificationBridge: tutorial event handler failed"); }
    }

    private void OnWebcamBlink()
    {
        // Webcam events already marshal to the UI thread today; RunOnUI is a no-cost
        // guard (short-circuits via CheckAccess) against future off-thread refactors.
        DispatcherHelper.RunOnUI(() =>
        {
            try
            {
                if (App.BlinkTrainer?.IsRunning != true) return; // attribute blink to the trainer only
                var p = Prog; if (p == null) return;
                p.BlinkTrainerBlinks++;
                Ach?.MarkDirty();
                if (p.BlinkTrainerBlinks >= 100)
                    Ach?.TryUnlockExclusive("blink_and_youll_miss_it");
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "GamificationBridge: blink handler failed"); }
        });
    }

    private void OnGazePopped()
    {
        // Gaze pops fire on the UI thread today (DispatcherTimer / webcam Dispatch);
        // marshal defensively all the same.
        DispatcherHelper.RunOnUI(() =>
        {
            try
            {
                var p = Prog; if (p == null) return;
                p.GazePops++;
                Ach?.MarkDirty();
                if (p.GazePops >= HandsFreeGazePops)
                    Ach?.TryUnlockExclusive("hands_free");
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "GamificationBridge: gaze-pop handler failed"); }
        });
    }

    private void OnLockdownActivated()
    {
        try
        {
            Ach?.TryUnlockExclusive("locked_in");
        }
        catch (Exception ex) { App.Logger?.Warning(ex, "GamificationBridge: lockdown activated handler failed"); }
    }

    private void OnLockdownDeactivated()
    {
        try
        {
            // Read the authoritative duration off the service (computed in Deactivate)
            // rather than tracking our own start time — the two can't desync, and it
            // survives the bridge being stopped/restarted mid-lockdown.
            var elapsed = App.Lockdown?.LastActiveDuration ?? TimeSpan.Zero;
            if (elapsed.TotalMinutes >= ThrowAwayKeyMinutes)
                Ach?.TryUnlockExclusive("throw_away_the_key");
        }
        catch (Exception ex) { App.Logger?.Warning(ex, "GamificationBridge: lockdown deactivated handler failed"); }
    }

    private void OnRemoteSessionStarted(object? sender, EventArgs e)
    {
        try
        {
            var p = Prog; if (p == null) return;
            p.RemoteCommandsThisSession = 0;
            Ach?.TryUnlockExclusive("hand_over_control");
        }
        catch (Exception ex) { App.Logger?.Warning(ex, "GamificationBridge: remote start handler failed"); }
    }

    private void OnRemoteSessionEnded(object? sender, EventArgs e)
    {
        if (Prog != null) Prog.RemoteCommandsThisSession = 0;
    }

    private void OnRemoteCommand(object? sender, string action)
    {
        // Remote poll loop is on the UI thread today (DispatcherTimer, no ConfigureAwait);
        // marshal defensively in case the polling is moved to a background task later.
        DispatcherHelper.RunOnUI(() =>
        {
            try
            {
                var p = Prog; if (p == null) return;
                p.RemoteCommandsThisSession++;
                if (p.RemoteCommandsThisSession >= PuppetStringsCommands)
                    Ach?.TryUnlockExclusive("puppet_strings");
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "GamificationBridge: remote command handler failed"); }
        });
    }

    private void OnCatalogueSubmitted(object? sender, SubmissionResult.Success e)
    {
        try { Ach?.TryUnlock("on_the_shelf"); }
        catch (Exception ex) { App.Logger?.Warning(ex, "GamificationBridge: catalogue submit handler failed"); }
    }

    /// <summary>
    /// Graded-run results. Since 6.7.x the live source is a completed Graded Intake
    /// (<c>IntakeHostService</c>) rather than the classic AI quiz, whose launcher is hidden —
    /// which is why these four sat unobtainable (#870). The handler is source-agnostic on
    /// purpose: it reads a grade and a category and does not care which surface produced them.
    ///
    /// <list type="bullet">
    /// <item><c>teachers_pet</c> — <see cref="TeachersPetPasses"/> completed runs.</item>
    /// <item><c>top_of_the_class</c> — one run graded at or above the top-marks bar (90%).</item>
    /// <item><c>honor_roll</c> — top marks in <see cref="HonorRollCategories"/> distinct
    /// categories; from the intake that is distinct niches, which follow the active mod.</item>
    /// <item><c>held_back</c> — still fail-streak only. An intake has no fail state, so this can
    /// only ever come from the classic quiz. Left as-is deliberately (product decision).</item>
    /// </list>
    /// </summary>
    private void OnQuizCompleted(object? sender, QuizCompletedEventArgs e)
    {
        try
        {
            var p = Prog; if (p == null) return;

            if (e.Passed)
            {
                p.QuizFailStreak = 0;
                p.QuizzesPassed++;
                if (p.QuizzesPassed >= TeachersPetPasses)
                    Ach?.TryUnlockExclusive("teachers_pet");
            }
            else
            {
                p.QuizFailStreak++;
                if (p.QuizFailStreak >= HeldBackFailStreak)
                    Ach?.TryUnlockExclusive("held_back");
            }

            if (e.Perfect)
            {
                Ach?.TryUnlockExclusive("top_of_the_class");
                // honor_roll: a perfect score in N distinct categories ("clearing" them).
                if (!string.IsNullOrEmpty(e.Category) && p.PerfectedQuizCategories.Add(e.Category)
                    && p.PerfectedQuizCategories.Count >= HonorRollCategories)
                {
                    Ach?.TryUnlockExclusive("honor_roll");
                }
            }

            Ach?.MarkDirty();
        }
        catch (Exception ex) { App.Logger?.Warning(ex, "GamificationBridge: quiz handler failed"); }
    }

    private void OnPersistentMemoryRecalled(object? sender, EventArgs e)
    {
        // May resume on a background continuation; marshal before any unlock/popup path.
        DispatcherHelper.RunOnUI(() =>
        {
            try { Ach?.TryUnlockExclusive("she_remembers"); }
            catch (Exception ex) { App.Logger?.Warning(ex, "GamificationBridge: memory-recall handler failed"); }
        });
    }

    private void OnEnhancementCompleted(object? sender, EnhancementCompletedEventArgs e)
    {
        try
        {
            var p = Prog; if (p == null) return;
            p.EnhancementsPlayed++;
            Ach?.MarkDirty();

            Ach?.TryUnlock("going_deeper");
            if (p.EnhancementsPlayed >= DownTheRabbitHolePlays)
                Ach?.TryUnlock("down_the_rabbit_hole");
            if (e.DistinctTriggerTypes >= OnRailsTriggerTypes)
                Ach?.TryUnlock("on_rails");
            if (e.WebcamTriggerUsed)
                Ach?.TryUnlock("wired_in");
            if (e.GazeHeldFull)
                Ach?.TryUnlock("dont_look_away");
            if (e.Featured)
                Ach?.TryUnlock("directors_cut");
        }
        catch (Exception ex) { App.Logger?.Warning(ex, "GamificationBridge: enhancement completed handler failed"); }
    }

    public void Dispose() => Stop();
}
