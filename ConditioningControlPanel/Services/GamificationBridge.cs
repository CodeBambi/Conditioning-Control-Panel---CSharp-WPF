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
    private const int HonorRollCategories = 3;            // "perfect 3 different categories"
    private const int TeachersPetPasses = 25;             // "pass 25 quizzes"
    private const int HeldBackFailStreak = 3;             // "fail 3 quizzes in a row"

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

            // ----- Quiz (patron: top_of_the_class, teachers_pet, honor_roll, held_back) -----
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
            // Long-time chatters would have to start over from zero, so reconstruct the counter
            // once from what the companion actually persisted.
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
    /// One-shot retroactive repair of <see cref="AchievementProgress.CompanionMessages"/> (#877).
    ///
    /// <para>Two independent records of "the user talked to her" already exist on disk, and the
    /// higher of them wins:</para>
    /// <list type="bullet">
    /// <item>the restored turn log — exact, but a rolling window
    /// (<see cref="CompanionSessionStore.MaxPersistedTurns"/> caps it at 50 user turns), so it is
    /// a FLOOR on the true count, never the whole history;</item>
    /// <item><see cref="MemoryStore"/>'s per-mod relationship counters — uncapped and monotonic,
    /// but only started accumulating when the brain shipped.</item>
    /// </list>
    ///
    /// <para>The counter is only ever raised, never lowered: a user who already earned messages the
    /// honest way cannot be demoted by a thinner record. Both sources are silent when the user has
    /// chat memory switched off, which is correct — that toggle exists precisely so those turns are
    /// not on disk to be counted.</para>
    ///
    /// <para>Latched via <see cref="AchievementProgress.CompanionChatBackfilled"/>, and the latch is
    /// only set once there was a brain to read: with the kill switch off (or a brain that failed to
    /// construct) there is no evidence to gather, and burning the one-shot on that would leave the
    /// user permanently un-backfilled. Running every launch instead of once is not an option —
    /// the window source would drag the counter back down to ~50 forever.</para>
    /// </summary>
    private void BackfillCompanionChatCount()
    {
        try
        {
            var p = Prog; if (p == null || p.CompanionChatBackfilled) return;

            var brain = App.Brain;
            if (brain == null) return; // no source yet — try again next launch

            var restoredTurns = 0;
            try { restoredTurns = brain.Session.Turns.Count(t => t != null && t.Kind == TurnKind.UserChat); }
            catch (Exception ex) { App.Logger?.Debug("GamificationBridge: turn-log backfill read failed: {Error}", ex.Message); }

            var relationshipTurns = 0;
            try
            {
                if (brain.Memory is MemoryStore store)
                    relationshipTurns = store.Relationships.Values.Sum(r => r?.ChatTurnsTotal ?? 0);
            }
            catch (Exception ex) { App.Logger?.Debug("GamificationBridge: relationship backfill read failed: {Error}", ex.Message); }

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

            if (p.CompanionMessages > 0)
                Ach?.TryUnlock("pleased_to_meet_you");
            if (p.CompanionMessages >= PillowTalkMessages)
                Ach?.TryUnlock("pillow_talk");
        }
        catch (Exception ex) { App.Logger?.Warning(ex, "GamificationBridge: companion chat backfill failed"); }
    }

    private void OnCompanionMessageSent(object? sender, EventArgs e)
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
