using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Services;

/// <summary>
/// The lifetime counters of WPF 7.1.5 <c>AchievementService</c> and <c>GamificationBridge</c> that the
/// port never moved (k23 audit, 10 Oct 2026): flashes, sessions, lock cards, panic / relapse, XP and
/// Sparkle Points earned, overlay minutes, mods, companion messages, the trainer and gaze counts.
/// Each method is the WPF body minus its quest call (the callers already credit quests beside it)
/// and minus the Chaster notes (the head books those).
///
/// <para>Conditioning time is NOT here on purpose: ONE tracker credits it (Core ConditioningTime,
/// started by CoreEngine), the 7.1.5 double-count fix. Nothing here writes settings; the counters ride
/// the dirty flag and the head's 30 s autosave, as WPF.</para>
/// </summary>
internal sealed partial class AchievementEngine
{
    /// <summary>The running engine, for Core call sites (SessionRunner, CoreEngine, SkillPointsBank).
    /// Null = no head seeded one: every call site is null-conditional.</summary>
    internal static volatile AchievementEngine? Current;

    /// <summary>Clock seam for the relapse window and Morning Glory (tests step it).</summary>
    internal Func<DateTime> Now = () => DateTime.Now;

    // ---- flashes ---------------------------------------------------------------------------------

    /// <summary>WPF TrackFlashImage (:580) + FlashService.cs:2171: one image reached the screen. The EMI
    /// moment is read AFTER the increment, so "== 1" is literally the first image this account was shown.</summary>
    public void TrackFlashImage()
    {
        Progress.TotalFlashImages++;
        _isDirty = true;
        if (Progress.TotalFlashImages >= AchievementRules.RetinalBurnFlashImages) TryUnlock("retinal_burn");
        if (Progress.TotalFlashImages == 1) EmiDeskBus.Fire("firstFlashEver", null);
    }

    // ---- sessions --------------------------------------------------------------------------------

    /// <summary>WPF TrackSessionStart (:900) + SessionEngine.cs:281: a session the player started. Called
    /// behind the <c>sessionStarted</c> moment on purpose (EMI's floor picks the once-ever line).</summary>
    public void TrackSessionStart()
    {
        Progress.ResetSessionTracking();
        Progress.TotalSessionsStarted++;
        CheckRelapse();
        _isDirty = true;
        if (Progress.TotalSessionsStarted == 1) EmiDeskBus.Fire("firstSessionEver", null);
    }

    /// <summary>WPF TrackSessionAbandoned (:1142).</summary>
    public void TrackSessionAbandoned()
    {
        Progress.TotalSessionsAbandoned++;
        _isDirty = true;
    }

    /// <summary>WPF TrackSessionComplete (:932). The no-panic and strict flags are the ones captured at
    /// session START (the restore has already run by the time a session ends).</summary>
    public void TrackSessionComplete(string? sessionName, double durationMinutes, bool noPanicEnabled, bool strictLockEnabled)
    {
        var name = sessionName ?? "";
        Progress.CompletedSessions.Add(name);
        if (durationMinutes > Progress.LongestSessionMinutes) Progress.LongestSessionMinutes = durationMinutes;
        if (durationMinutes >= 180) TryUnlock("deep_sleep");
        if (noPanicEnabled)
        {
            Progress.CompletedSessionWithNoPanic = true;
            TryUnlock("what_panic_button");
        }
        var lower = name.ToLowerInvariant();
        if (lower.Contains("distant doll")) TryUnlock("sofa_decor");
        if (lower.Contains("good girls") && strictLockEnabled)
        {
            Progress.CompletedGoodGirlsWithStrictLock = true;
            TryUnlock("look_but_dont_touch");
        }
        if (lower.Contains("morning drift"))
        {
            var hour = Now().Hour;
            if (hour >= 6 && hour < 9)
            {
                Progress.CompletedMorningDriftInMorning = true;
                TryUnlock("morning_glory");
            }
        }
        if (lower.Contains("gamer girl") && !Progress.AltTabPressedThisSession)
        {
            Progress.CompletedGamerGirlNoAltTab = true;
            TryUnlock("player_2_disconnected");
        }
        _isDirty = true;
    }

    // ---- panic / relapse -------------------------------------------------------------------------

    /// <summary>WPF TrackAltTab (:882).</summary>
    public void TrackAltTab()
    {
        Progress.AltTabPressedThisSession = true;
        _isDirty = true;
    }

    /// <summary>WPF TrackPanicPressed (:891).</summary>
    public void TrackPanicPressed()
    {
        Progress.LastPanicPressTime = Now();
        _isDirty = true;
    }

    /// <summary>WPF CheckRelapse (:917): a start inside ten seconds of a panic press.</summary>
    public void CheckRelapse()
    {
        if (Progress.LastPanicPressTime is not { } pressed) return;
        if ((Now() - pressed).TotalSeconds <= 10) TryUnlock("relapse");
    }

    // ---- lock card -------------------------------------------------------------------------------

    /// <summary>WPF TrackLockCardCompletion (:751). Saved at once so sync picks the count up.</summary>
    public void TrackLockCardCompletion(double seconds, int errors, int phrases)
    {
        Progress.TotalLockCardsCompleted++;
        if (errors == 0) Progress.HasPerfectLockCard = true;
        bool fast = phrases >= 3 && seconds < 15;
        if (fast && seconds < Progress.FastestLockCardSeconds) Progress.FastestLockCardSeconds = seconds;
        Save();
        if (Progress.TotalLockCardsCompleted >= AchievementRules.WordPerfectLockCards) TryUnlock("word_perfect");
        if (errors == 0) TryUnlock("typing_tutor");
        if (fast) TryUnlock("obedience_reflex");
    }

    // ---- earned totals ---------------------------------------------------------------------------

    /// <summary>WPF TrackXPEarned (:1151).</summary>
    public void TrackXPEarned(double amount)
    {
        if (amount <= 0) return;
        Progress.TotalXPEarned += amount;
        _isDirty = true;
    }

    /// <summary>WPF TrackSkillPointsEarned (:1160): a stat only. It never touches the wallet.</summary>
    public void TrackSkillPointsEarned(int amount)
    {
        if (amount <= 0) return;
        Progress.TotalSkillPointsEarned += amount;
        _isDirty = true;
    }

    // ---- overlay minutes (WPF TrackTimeBasedProgress :395-548) ------------------------------------

    public void TrackPinkFilterMinutes(double minutes)
    {
        if (minutes <= 0) return;
        Progress.TotalPinkFilterMinutes += minutes;
        _isDirty = true;
        if (Progress.TotalPinkFilterMinutes >= AchievementRules.RoseTintedPinkFilterMinutes) TryUnlock("rose_tinted_reality");
    }

    public void TrackSpiralMinutes(double minutes)
    {
        if (minutes <= 0) return;
        Progress.TotalSpiralMinutes += minutes;
        Progress.ContinuousSpiralMinutes += minutes;
        _isDirty = true;
        if (Progress.ContinuousSpiralMinutes >= 20) TryUnlock("spiral_eyes");
        if (Progress.TotalSpiralMinutes >= AchievementRules.ThreadbareSpiralMinutes) TryUnlock("threadbare");
    }

    /// <summary>The spiral left the screen: the continuous run starts over.</summary>
    public void ResetContinuousSpiral()
    {
        if (Progress.ContinuousSpiralMinutes == 0) return;
        Progress.ContinuousSpiralMinutes = 0;
        _isDirty = true;
    }

    public void TrackDeeperMinutes(double minutes)
    {
        if (minutes <= 0) return;
        Progress.DeeperMinutes += minutes;
        _isDirty = true;
        if (Progress.DeeperMinutes >= AchievementRules.PermanentResidentDeeperMinutes) TryUnlock("permanent_resident");
    }

    /// <summary>WPF :529-547: System Overload (bubbles + bouncing text + spiral) and Total Lockdown
    /// (Strict Lock + no panic + pink filter). Reads settings, never writes them.</summary>
    public void CheckSettingCombos(AppSettings? s)
    {
        if (s == null) return;
        if (!Progress.HasSystemOverload && s.BubblesEnabled && s.BouncingTextEnabled && s.SpiralEnabled)
        {
            Progress.HasSystemOverload = true;
            _isDirty = true;
            TryUnlock("system_overload");
        }
        if (!Progress.HasTotalLockdown && s.StrictLockEnabled && !s.PanicKeyEnabled && s.PinkFilterEnabled)
        {
            Progress.HasTotalLockdown = true;
            _isDirty = true;
            TryUnlock("total_lockdown");
        }
    }

    // ---- WPF GamificationBridge ------------------------------------------------------------------

    /// <summary>OnModChanged (:403): distinct mods activated, and the community ones among them.</summary>
    public void TrackModActivated(string? modId, bool builtIn)
    {
        if (string.IsNullOrEmpty(modId)) return;
        bool newDistinct = Progress.ActivatedModIds.Add(modId);
        bool newCommunity = !builtIn && Progress.CommunityModIds.Add(modId);
        if (!newDistinct && !newCommunity) return;
        _isDirty = true;
        if (newDistinct && Progress.ActivatedModIds.Count >= AchievementRules.CuratorDistinctMods) TryUnlock("curator");
        if (newCommunity && Progress.CommunityModIds.Count >= AchievementRules.CommunityModsCount) TryUnlock("community_supported");
    }

    /// <summary>OnModInstalled (:428).</summary>
    public void TrackModInstalled()
    {
        Progress.ModsInstalled++;
        _isDirty = true;
        TryUnlock("modder");
    }

    /// <summary>OnCompanionMessageSent (:381).</summary>
    public void TrackCompanionMessage()
    {
        Progress.CompanionMessages++;
        _isDirty = true;
        TryUnlock("pleased_to_meet_you");
        if (Progress.CompanionMessages >= AchievementRules.PillowTalkMessages) TryUnlock("pillow_talk");
    }

    /// <summary>OnWebcamBlink (:470): the caller counts a blink only while the trainer runs.</summary>
    public void TrackBlinkTrainerBlink()
    {
        Progress.BlinkTrainerBlinks++;
        _isDirty = true;
        if (Progress.BlinkTrainerBlinks >= AchievementRules.BlinkAndYoullMissItBlinks) TryUnlockExclusive("blink_and_youll_miss_it");
    }

    /// <summary>OnGazePopped (:490).</summary>
    public void TrackGazePop()
    {
        Progress.GazePops++;
        _isDirty = true;
        if (Progress.GazePops >= AchievementRules.HandsFreeGazePops) TryUnlockExclusive("hands_free");
    }

    /// <summary>OnEnhancementCompleted (:657): the play count and its two count badges. The per-play
    /// badges (on_rails, wired_in, dont_look_away, directors_cut) read the event's own flags.</summary>
    public void TrackEnhancementPlayed(int distinctTriggerTypes = 0)
    {
        Progress.EnhancementsPlayed++;
        _isDirty = true;
        TryUnlock("going_deeper");
        if (Progress.EnhancementsPlayed >= AchievementRules.DownTheRabbitHolePlays) TryUnlock("down_the_rabbit_hole");
        if (distinctTriggerTypes >= AchievementRules.OnRailsTriggerTypes) TryUnlock("on_rails");
    }

    // ---- Core event wiring -----------------------------------------------------------------------

    private static Action<double, string>? _xpHandler;
    private static EventHandler<ModPackage>? _modHandler;

    /// <summary>Make <paramref name="engine"/> the running one and hook the Core events it counts from
    /// (XP banked, mod switched). Idempotent; null detaches (tests, shutdown).</summary>
    internal static void Attach(AchievementEngine? engine)
    {
        if (_xpHandler != null) { ProgressionBank.Awarded -= _xpHandler; _xpHandler = null; }
        if (_modHandler != null) { CoreMods.ModChanged -= _modHandler; _modHandler = null; }
        Current = engine;
        if (engine == null) return;
        _xpHandler = (amount, _) => { try { engine.TrackXPEarned(amount); } catch (Exception ex) { Log.Debug(ex, "achievement xp total"); } };
        _modHandler = (_, mod) =>
        {
            try { if (mod != null) engine.TrackModActivated(mod.Id, mod.IsBuiltIn); }
            catch (Exception ex) { Log.Debug(ex, "achievement mod count"); }
        };
        ProgressionBank.Awarded += _xpHandler;
        CoreMods.ModChanged += _modHandler;
    }
}
