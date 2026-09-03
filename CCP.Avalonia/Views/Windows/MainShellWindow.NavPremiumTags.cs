// PORTED from ConditioningControlPanel/MainWindow/MainWindow.NavPremiumTags.cs (191 lines).
//
// The little gold ★ that rides after a rail entry's label while that entry's door is shut to this
// account, and disappears the moment it opens.
//
// WHAT CROSSES: the JOIN. "Which rail row is which sold feature" is a fact about
// MainShellWindow.axaml and lives nowhere else, so it is the one thing this file has always
// owned - eight tag Borders keyed by their ShowTab key, which is also the feature's roster key.
// The pills are reached with Named<Border>() because this window's generated x:Name fields are
// never assigned (see MainShellWindow.TabNavigation.cs).
//
// WHAT DOES NOT: the ANSWER. IsNavEntryLocked asks Models.ExclusiveFeature.All /
// ExclusiveFeature.GateState (ConditioningControlPanel/Models/ExclusiveFeature.cs) and
// DailyFreeService.IsFreeToday through App.DailyFree; the roster type is still WPF-side and this
// head seeds no DailyFreeService instance, so there is no entitlement to read. That is NOT a
// reason to invent one: the WPF original already fails to NO TAG on anything it cannot answer,
// deliberately, because "an un-starred row that turns out to be locked costs a TierGate toast the
// user was going to see anyway, while a starred row that is actually open is the rail lying about
// what somebody already paid for". A head with no entitlement service is exactly that case, so
// the honest answer here is the original's own fallback and the pills stay as authored
// (IsVisible=False on the NavEntryPremiumTag theme).
//
// Also head-side: HookNavPremiumTags and QueueNavPremiumTagRefresh, whose four subscriptions are
// App.Patreon.TierChanged, App.SubscribeStar.TierChanged, App.DailyFree.TodayChanged and
// App.IntakePass.PassStateChanged - four services, none of them on this head. The marshal they
// wrap is CoreDispatch/Dispatcher.UIThread.Post here, one line, once there is something to
// subscribe to.
//
// Callers this layer does not own: InitializeNavRail and RefreshNavPremiumTags' repaint callers
// live in MainShellWindow.NavRail.cs / MainShellWindow.PremiumRail.cs, and the collapse fade that
// reads NavPremiumTagElements is SetNavRailExpanded in MainShellWindow.NavRail.cs.

using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>
        /// The join: one premium tag Border per rail entry, keyed by the entry's ShowTab key -
        /// which is also its ExclusiveFeature key. Adding a rail row for a sold feature means
        /// adding its pill in MainShellWindow.axaml and one row here; nothing else.
        ///
        /// <para>Two roster entries deliberately have no row: "fyp" and "justdrop" are window
        /// launchers, not rail entries, so there is nothing to tag.</para>
        /// </summary>
        private IEnumerable<(Border? Tag, string Key)> NavPremiumTagMap
        {
            get
            {
                yield return (Named<Border>("TagPremiumHaptics"), "haptics");
                yield return (Named<Border>("TagPremiumTakeover"), "bambitakeover");
                yield return (Named<Border>("TagPremiumSheListening"), "shelistening");
                yield return (Named<Border>("TagPremiumAwareness"), "awareness");
                yield return (Named<Border>("TagPremiumGradedIntake"), "gradedintake");
                yield return (Named<Border>("TagPremiumLockdown"), "lockdown");
                yield return (Named<Border>("TagPremiumBlinkTrainer"), "blinktrainer");
                yield return (Named<Border>("TagPremiumRemoteControl"), "remotecontrol");
            }
        }

        /// <summary>The pills, for the rail's collapse fade. Non-null only; a tag whose control
        /// failed to resolve simply is not faded, because it is not on screen either.</summary>
        internal IEnumerable<Control> NavPremiumTagElements =>
            NavPremiumTagMap.Select(t => t.Tag).Where(t => t is not null)!;

        /// <summary>
        /// Paints every rail premium tag from the roster. Cheap enough to be unconditional: eight
        /// namescope lookups, no allocation that matters, no clock started. Never throws - the
        /// rail's chrome must not be able to break a tab switch.
        /// </summary>
        internal void RefreshNavPremiumTags()
        {
            foreach (var (tag, key) in NavPremiumTagMap)
            {
                if (tag is null) continue;
                tag.IsVisible = IsNavEntryLocked(key);
            }
        }

        /// <summary>
        /// "Is this door shut to this account right now?" - see the header for why this head can
        /// only answer no. Kept as its own member rather than folded into the loop above so the
        /// roster lookup lands in one place when ExclusiveFeature crosses.
        /// </summary>
        private static bool IsNavEntryLocked(string exclusiveKey)
        {
            // ponytail: needs Models.ExclusiveFeature.All + GateState
            // (ConditioningControlPanel/Models/ExclusiveFeature.cs) and DailyFreeService.IsFreeToday
            // for the feature's DailyFreeKey. WPF's own fallback for an unanswerable key is "not
            // locked", which is what an entitlement-less head is.
            _ = exclusiveKey;
            return false;
        }
    }
}
