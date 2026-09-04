using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services.Bark;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The bark seam: the companion's reactive dialogue engine, told that something happened.
    ///
    /// <para><c>BarkService</c> itself does NOT move. It subscribes to some fifty head services
    /// (<c>App.Webcam</c>, <c>App.GazeFocus</c>, <c>App.Video</c>, <c>App.Bubbles</c>,
    /// <c>App.SkillTree</c>, the tray icon, the session engine…) and speaks through the avatar
    /// window; a dry-run <c>git mv</c> into Core failed on <c>DescentBarkDecision</c>,
    /// <c>LockCardCompletedEventArgs</c>, <c>BarkState</c>, <c>TrayIconService</c>,
    /// <c>SessionEngine</c>, <c>ContextFrame</c> and <c>System.Windows.Threading</c> before Roslyn
    /// even reached the method bodies. <c>CoreModsHooks.ReloadBarkRules</c> says the same thing
    /// from the other side: the head owns rule loading. So the engine stays in the head and this
    /// is the doorbell.</para>
    ///
    /// <para>Every member is fire-and-forget. Nothing here gates anything: a bark is a line the
    /// companion may say, chanced and cooled down inside the rules, and losing one costs a voiced
    /// reaction and never a file, a session or a consent. That is why an unseeded no-op is the
    /// honest answer rather than a degraded one - and why the one member that returns data,
    /// <see cref="AllLines"/>, returns an EMPTY list rather than null: a Phrase Manager on a head
    /// with no bark engine has no bark rows, which is the truth.</para>
    /// </summary>
    public static class CoreBark
    {
        /// <summary>A UI action worth reacting to, by its bark key ("minimize", "open_assets"…).</summary>
        public static volatile Action<string?>? UiAction;

        /// <summary>The user navigated to a tab, by the tab's BARK key (see the head's alias map).</summary>
        public static volatile Action<string?>? TabNavigated;

        /// <summary>A studio feature was opened, by the feature's rule key ("SchedulerRamp"…).</summary>
        public static volatile Action<string?>? FeatureOpened;

        /// <summary>The avatar/logo was clicked - drives the rolling 60s click-escalation eggs.</summary>
        public static volatile Action? AvatarClicked;

        /// <summary>Chaos Mode: the draft timed out and picked for the player.</summary>
        public static volatile Action? ChaosDraftAutopick;

        /// <summary>Chaos Mode: the end-of-run recap is on screen. Signature copied verbatim.</summary>
        public static volatile Action<double, double, double, bool, double, double, int, string>? ChaosResultsShown;

        /// <summary>Chaos Mode: the run crossed into a new rank, by its lowercase name.</summary>
        public static volatile Action<string>? ChaosRankUp;

        /// <summary>Every inline bark line of the active mod's rule set, for the Phrase Manager.</summary>
        public static volatile Func<IReadOnlyList<BarkLineInfo>>? AllLinesProvider;

        public static void NotifyUiAction(string? action)
        { try { UiAction?.Invoke(action); } catch { } }

        public static void NotifyTabNavigated(string? tab)
        { try { TabNavigated?.Invoke(tab); } catch { } }

        public static void NotifyFeatureOpened(string? feature)
        { try { FeatureOpened?.Invoke(feature); } catch { } }

        public static void NotifyAvatarClicked()
        { try { AvatarClicked?.Invoke(); } catch { } }

        public static void NotifyChaosDraftAutopick()
        { try { ChaosDraftAutopick?.Invoke(); } catch { } }

        public static void NotifyChaosResultsShown(double score, double bestScore, double pbDelta, bool isPb,
            double defused, double detonated, int bestCombo, string difficulty)
        {
            try { ChaosResultsShown?.Invoke(score, bestScore, pbDelta, isPb, defused, detonated, bestCombo, difficulty); }
            catch { }
        }

        public static void NotifyChaosRankUp(string rank)
        { try { ChaosRankUp?.Invoke(rank); } catch { } }

        /// <summary>Never null: an unseeded seam, or a provider that throws, answers "no bark lines".</summary>
        public static IReadOnlyList<BarkLineInfo> AllLines
        {
            get
            {
                try { return AllLinesProvider?.Invoke() ?? Array.Empty<BarkLineInfo>(); }
                catch { return Array.Empty<BarkLineInfo>(); }
            }
        }
    }
}
