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
// THE ANSWER comes from Core: IsNavEntryLocked asks Models.ExclusiveFeature.All / GateState
// (CCP.Core/Models/ExclusiveFeature.cs) and ExclusiveFeature.IsFreeToday, i.e. CoreEntitlement,
// which App.axaml.cs seeds (account providers + DailyFreeService). On anything it cannot answer it
// fails to NO TAG, as the WPF original does.
//
// Repaint triggers: the rail init (MainShellWindow.NavRail.cs) and App.axaml.cs RepaintVeils (either
// provider's TierChanged + DailyFreeService.TodayChanged + IntakePass.PassStateChanged), WPF
// HookNavPremiumTags' subscriptions.
//
// Callers this layer does not own: InitializeNavRail and RefreshNavPremiumTags' repaint callers
// live in MainShellWindow.NavRail.cs / MainShellWindow.FavoritesRail.cs, and the collapse fade that
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
        /// <para>"fyp" deliberately has no row (a window launcher). WPF also tags "justdrop"
        /// (TagPremiumJustDrop on BtnNavJustDrop, Studio > Creator Tools, since 2026-09-11); that row
        /// is missing here until JustDropService/JustDropHostService exist on this head
        /// (MainShellWindow.JustDrop.cs).</para>
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
            // WPF :158: the dashboard's favorites chips read the same answer, so they repaint on
            // the same triggers instead of keeping hooks of their own.
            RefreshFavoritesRail();
        }

        /// <summary>WPF MainWindow.NavPremiumTags.cs:178, over Core's roster and the entitlement seam.
        /// Fails to NO TAG on anything unexpected, as WPF does.</summary>
        private static bool IsNavEntryLocked(string exclusiveKey)
        {
            try
            {
                var feature = Models.ExclusiveFeature.All.FirstOrDefault(f => f.Key == exclusiveKey);
                if (feature == null) return false;
                var state = feature.GateState();
                return state == Models.ExclusiveGateState.Locked && !feature.IsFreeToday(state);
            }
            catch { return false; }
        }
    }
}
