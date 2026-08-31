using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services.Bark;
using Serilog;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// The bulk hook (MOMENTS 4.A). One line inside <c>BarkService.Raise</c> feeds EMI the ~25 app-wide
/// events the avatar's bark service already owns the subscription lifetime for.
///
/// <para><b>Why a mirror and not a second subscriber block.</b> BarkService's <c>Wire&lt;T&gt;</c>
/// already attaches, detaches and re-attaches every one of these across mod reloads. A parallel
/// block in <see cref="EmiDeskService"/> would double each handler and would have to re-solve the
/// same teardown; the mirror is a single call that inherits all of it.</para>
///
/// <para><b>The mirror never touches the bark's own context.</b> It builds its own
/// <see cref="BarkContext"/> from the same <c>fill</c> delegate, so a row that reads a value cannot
/// perturb the bark that is about to be matched against it.</para>
///
/// <para><b>Deliberately unmapped triggers.</b> The high-frequency tracking family (BubblePopped,
/// Blink, LongStare, GazePopped, FaceFound/Lost, MouthOpen, TongueOut, TrackingStateChanged) would
/// burn the global floor on nothing, and the chat / run family (UserMessageSent, WakeBambiRequested,
/// AvatarClicked, SettingChanged, TutorialCompleted, the ~35 Chaos* triggers) belongs to the avatar,
/// who would be talked over. Both are available to the owner later by adding a row.</para>
///
/// <para><b>Deliberately not mirrored although the table lists them.</b> The whole <c>session</c>
/// group: the bark contexts are lossy (SessionCompleted discards the XP, the elapsed time and the
/// pause count; SessionProgress carries the elapsed seconds but never the remaining), so those
/// moments are fired at SessionEngine's own raise points instead. <c>brainDrainOn</c> likewise: the
/// inline hook in OverlayService can see the intensity and the melt flag, and firing it here as well
/// would double it.</para>
/// </summary>
internal static class EmiBarkBridge
{
    /// <summary>
    /// One row of the table. <paramref name="Moment"/> is the default moment id; <paramref name="Pick"/>,
    /// when present, may choose a different one or return null to drop the fire entirely (a trigger
    /// that only matters in one direction, like an idle transition going idle);
    /// <paramref name="Ctx"/> builds the moment's payload; <paramref name="Side"/> runs an extra
    /// effect that is not a fire (releasing a hold).
    /// </summary>
    private sealed record Row(
        string Moment,
        Func<BarkContext, object?>? Ctx = null,
        Func<BarkContext, string?>? Pick = null,
        Action<BarkContext>? Side = null);

    /// <summary>
    /// How many times each tab has been opened this launch, so <c>featureOpened</c> can become
    /// <c>featureOpenedRepeat</c> on the second visit and hand the line an honest <c>{n}</c>.
    /// Per launch on purpose: "you keep going back there" is a statement about this sitting.
    /// </summary>
    private static readonly Dictionary<string, int> _tabOpens = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, Row> _table = new(StringComparer.Ordinal)
    {
        // ---- arrival, navigation -------------------------------------------------------
        ["AppOpened"] = new("appOpened",
            c => new { target = c.GetString("away_bucket") }),

        ["TabNavigated"] = new("featureOpened",
            Ctx: c =>
            {
                var tab = c.GetString("tab");
                var n = CountTab(tab);
                return n > 1
                    ? (object)new { target = EmiNames.Feature(tab), n }
                    : new { target = EmiNames.Feature(tab) };
            },
            Pick: c => CountTab(c.GetString("tab"), peek: true) > 1 ? "featureOpenedRepeat" : "featureOpened"),

        ["FeatureOpened"] = new("featureOpened",
            c => new { target = EmiNames.Feature(c.GetString("feature")) }),

        // ---- video ---------------------------------------------------------------------
        ["VideoStarted"] = new("videoRunning",
            _ => new { target = EmiNames.VideoName(App.Video?.LastVideoTitle ?? App.Video?.LastVideoPath) }),
        ["VideoEnded"] = new("videoEnded"),

        // ---- attention checks ----------------------------------------------------------
        ["AttentionCheckPass"] = new("attentionCheckPassed"),
        ["AttentionCheckFail"] = new("attentionCheckFailed",
            c => c.TryGetNumber("fail_count", out var n) ? new { n = (int)n } : null),

        // ---- the toys ------------------------------------------------------------------
        ["BubbleCountCompleted"] = new("bubbleCountWon"),
        ["BubbleCountFailed"] = new("bubbleCountLost"),
        ["BlinkTrainerStateChanged"] = new("blinkTrainerStarted",
            Ctx: c => new { running = c.TryGetBool("running", out var r) && r },
            // Only the switching-on edge is a moment. "It stopped" is the afterEffect pool's job and
            // this trigger fires for both edges.
            Pick: c => c.TryGetBool("running", out var r) && r ? "blinkTrainerStarted" : null),
        ["MantraCompleted"] = new("mantraCompleted"),
        // No {streak}: MantraService.BreakStreak() zeroes Streak BEFORE it invokes StreakBroken, so
        // the number the line wants is already gone by the time anything downstream can read it.
        // The one line that asks for it is skipped; the other seven carry the beat.
        ["MantraStreakBroken"] = new("mantraStreakBroken"),

        // ---- lockdown ------------------------------------------------------------------
        // The countdown tick is a HOLD, so it fires on every tick and the engine collapses the
        // repeats into one live hold. The release is the deactivation's side effect, below: without
        // it a lockdown that ends would leave her silently held forever.
        ["LockdownActivated"] = new("lockdownArmed"),
        ["LockdownCountdownTick"] = new("lockdownCountdown"),
        ["LockdownDeactivated"] = new("lockdownEnded",
            Side: _ => { try { EmiLineEngine.Instance.ReleaseHold("lockdownCountdown"); } catch { } }),

        // ---- progression ---------------------------------------------------------------
        ["LevelUp"] = new("levelUp",
            c => c.TryGetNumber("level", out var lvl) ? new { level = (int)lvl } : null),
        ["AchievementUnlocked"] = new("achievementUnlocked",
            c => new { target = EmiNames.Achievement(c.GetString("achievement")) }),
        ["QuestCompleted"] = new("questCompleted"),
        ["SkillUnlocked"] = new("skillUnlocked"),
        ["PinkRushStarted"] = new("pinkRushStarted"),
        // No {minutes}: a rush is a flat 60 seconds (SkillTreeService.StartPinkRush), so the one
        // line in the pool that asks for it would always read "1 minutes". Omitted ctx skips that
        // line and keeps the other seven, which is the schema's own answer to a missing token.
        ["PinkRushEnded"] = new("pinkRushEnded"),
        ["LuckyProc"] = new("luckyProc"),
        ["StreakMilestone"] = new("streakMilestone",
            c => c.TryGetNumber("streak_days", out var d) ? new { streak = (int)d } : null),
        ["CompanionLevelUp"] = new("companionLevelUp",
            c => c.TryGetNumber("level", out var lvl) ? new { level = (int)lvl } : null),

        // ---- the rest of the app (wave 3) ----------------------------------------------
        ["EnhancementApplied"] = new("enhancementApplied",
            c => new { target = c.GetString("enhancement_id")?.ToLowerInvariant() }),
        ["ModChanged"] = new("modChanged",
            c => new { target = c.GetString("mod")?.ToLowerInvariant() }),

        // ---- intake --------------------------------------------------------------------
        // The graded result, not the window closing: the close hook only drops the HOLD, because a
        // quiz that was abandoned is not a result and she should not comment on it.
        ["QuizCompleted"] = new("intakeClosed",
            c => new
            {
                passed = c.TryGetBool("passed", out var p) && p,
                perfect = c.TryGetBool("perfect", out var pf) && pf,
            }),

        // ---- account, app --------------------------------------------------------------
        ["UpdateAvailable"] = new("updateAvailable"),
        ["PatreonTierChanged"] = new("tierUp",
            Ctx: c => new { target = c.GetString("tier")?.ToLowerInvariant() },
            Pick: c => c.TryGetBool("tier_up", out var up) && up ? "tierUp" : "tierLapse"),

        // Only the going-idle edge. Coming back is `backSoon` / `appOpened` territory and this
        // trigger fires for both.
        ["IdleStateChanged"] = new("appIdleLong",
            Pick: c => c.TryGetBool("idle", out var idle) && idle ? "appIdleLong" : null),

        // ---- safety --------------------------------------------------------------------
        // NotifyPanic() routes here; the remote and the hotkey each have their own inline call so
        // the silence is armed even when the ladder throws before this point.
        ["Panic"] = new("panicPressed"),
    };

    /// <summary>
    /// The mirror. Called from the top of <c>BarkService.Raise</c>, before the rule lookup that
    /// early-returns for triggers no mod has a rule for. Swallows everything: a throwing EMI must
    /// never be able to break a bark.
    /// </summary>
    public static void Mirror(string? trigger, Action<BarkContext>? fill)
    {
        if (string.IsNullOrEmpty(trigger)) return;
        try
        {
            if (App.EmiDesk == null) return;
            if (!_table.TryGetValue(trigger!, out var row) || row == null) return;

            // Our own context: reading the bark's would mean sharing a mutable bag with the matcher
            // that is about to run against it.
            var ctx = new BarkContext(trigger!);
            if (fill != null)
            {
                try { fill(ctx); }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] mirror fill for {Trigger} threw", trigger); }
            }

            var moment = row.Pick != null ? row.Pick(ctx) : row.Moment;

            if (row.Side != null)
            {
                try { row.Side(ctx); }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] mirror side effect for {Trigger} threw", trigger); }
            }

            if (string.IsNullOrEmpty(moment)) return;

            object? payload = null;
            if (row.Ctx != null)
            {
                try { payload = row.Ctx(ctx); }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] mirror ctx for {Trigger} threw", trigger); }
            }

            App.EmiDesk.Fire(moment!, payload);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] bark mirror for {Trigger} failed", trigger);
        }
    }

    /// <summary>
    /// Count this launch's opens of a tab. <paramref name="peek"/> reads without counting, so the
    /// row's <c>Pick</c> and its <c>Ctx</c> see the same number for one navigation.
    /// </summary>
    private static int CountTab(string? tab, bool peek = false)
    {
        if (string.IsNullOrWhiteSpace(tab)) return 0;
        lock (_tabOpens)
        {
            _tabOpens.TryGetValue(tab!, out var n);
            if (peek) return n + 1;
            n += 1;
            _tabOpens[tab!] = n;
            return n;
        }
    }
}
