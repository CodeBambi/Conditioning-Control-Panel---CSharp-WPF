using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel
{
    /// <summary>
    /// LAYER A of Mort's rail re-tree (community proposal, accepted 2026-08-25): the little gold
    /// star that rides after a rail entry's label while that entry's door is closed to this
    /// account, and disappears the moment it opens.
    ///
    /// <para><b>There is no second list.</b> "Which rail rows are sold" is answered by
    /// <see cref="ExclusiveFeature.All"/> - the roster the Exclusives shelf, the Play card wall
    /// and the dashboard's price tags already read - joined to the rail by the one identity both
    /// sides already share, the ShowTab key. A hand-kept roster here is exactly how the Lab
    /// smokescreen and the launch handlers drifted apart, and it would drift the same way the
    /// first time an exclusive is added or retired: the shelf would move and the rail would not.
    /// The only thing this file owns is the JOIN (tag control -> ShowTab key), which is a fact
    /// about MainWindow.xaml and lives nowhere else.</para>
    ///
    /// <para><b>Why "locked", not "premium".</b> The tag answers "can you open this right now?",
    /// which is what a user reads it as - so it clears for a patron, for a free account holding
    /// an unspent weekly intake pass (Graded Intake's own probe, in the roster), and on the day
    /// <see cref="Services.DailyFreeService"/> rotates the row's pool key in. That is the same
    /// three-part answer the dashboard lockbands give (MainWindow.PremiumRail.cs,
    /// RefreshRailLockbands), so a row cannot wear a star over a door that is standing open.</para>
    ///
    /// <para><b>Collapsed rail: no tags.</b> The 56px strip shows icons only. The pills are
    /// nowhere near it - the nearest one starts ~63px in, after the shortest premium label in the
    /// nine locales (zh "触觉") - so the rail's ClipToBounds already hides them, but only by 7px,
    /// and a shorter translation would break that. So they are faded on the rail's own clock
    /// instead, alongside the entry labels: see <see cref="NavPremiumTagElements"/> and the fade
    /// loop in <see cref="SetNavRailExpanded"/>. The pill's inner TextBlock is ALSO in
    /// <c>_navRailLabels</c> (CacheNavRailParts sweeps up every rail TextBlock); two clocks on
    /// two different Opacity properties of nested elements is harmless - they agree - and it
    /// keeps this file from having to teach CacheNavRailParts a new exception.</para>
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>
        /// The join: one premium tag control per rail entry, keyed by the entry's ShowTab key -
        /// which is also its <see cref="ExclusiveFeature.Key"/>. Adding a rail row for a sold
        /// feature means adding its pill in MainWindow.xaml and one row here; nothing else.
        ///
        /// <para>Two roster entries deliberately have no row: "fyp" and "justdrop" are window
        /// launchers, not rail entries, so there is nothing to tag.</para>
        ///
        /// <para>A property rather than a static field: the tag controls are x:Named members and
        /// do not exist until InitializeComponent has run.</para>
        /// </summary>
        private IEnumerable<(Border? Tag, string Key)> NavPremiumTagMap
        {
            get
            {
                yield return (TagPremiumHaptics, "haptics");
                yield return (TagPremiumTakeover, "bambitakeover");
                yield return (TagPremiumSheListening, "shelistening");
                yield return (TagPremiumAwareness, "awareness");
                yield return (TagPremiumGradedIntake, "gradedintake");
                yield return (TagPremiumLockdown, "lockdown");
                yield return (TagPremiumBlinkTrainer, "blinktrainer");
                yield return (TagPremiumRemoteControl, "remotecontrol");
            }
        }

        /// <summary>The pills, for the rail's collapse fade. Non-null only; a tag whose control
        /// failed to resolve simply is not faded, because it is not on screen either.</summary>
        internal IEnumerable<FrameworkElement> NavPremiumTagElements =>
            NavPremiumTagMap.Select(t => t.Tag).Where(t => t != null)!;

        // Per-service latches rather than one: the services come up in different orders (App
        // .OnStartup builds Patreon early and IntakePass late), and a single latch set on the
        // first call would permanently orphan whichever one was still null at that moment. Same
        // idiom as EnsureIntakePassRailHooked in MainWindow.PremiumRail.cs.
        private bool _navTagPatreonHooked;
        private bool _navTagSubStarHooked;
        private bool _navTagDailyFreeHooked;
        private bool _navTagIntakePassHooked;

        /// <summary>
        /// Subscribes the tags to every event that can change the answer. Idempotent and
        /// re-entrant: called once from <see cref="InitializeNavRail"/> and again at the top of
        /// every <see cref="RefreshNavPremiumTags"/>, so a service that was still null on the
        /// Loaded path gets picked up by the first refresh after it exists.
        /// </summary>
        private void HookNavPremiumTags()
        {
            try
            {
                if (!_navTagPatreonHooked && App.Patreon != null)
                {
                    _navTagPatreonHooked = true;
                    App.Patreon.TierChanged += (_, __) => QueueNavPremiumTagRefresh();
                }
                // SubscribeStar is the second entitlement provider and raises its own TierChanged;
                // HasPremiumAccess folds both, so both have to wake the rail or a SubscribeStar
                // upgrade would leave eight stars standing until the next launch.
                if (!_navTagSubStarHooked && App.SubscribeStar != null)
                {
                    _navTagSubStarHooked = true;
                    App.SubscribeStar.TierChanged += (_, __) => QueueNavPremiumTagRefresh();
                }
                if (!_navTagDailyFreeHooked && App.DailyFree != null)
                {
                    _navTagDailyFreeHooked = true;
                    App.DailyFree.TodayChanged += QueueNavPremiumTagRefresh;
                }
                if (!_navTagIntakePassHooked && App.IntakePass != null)
                {
                    _navTagIntakePassHooked = true;
                    App.IntakePass.PassStateChanged += (_, __) => QueueNavPremiumTagRefresh();
                }
            }
            catch (Exception ex) { App.Logger?.Debug("HookNavPremiumTags: {E}", ex.Message); }
        }

        /// <summary>
        /// Marshals a refresh onto the UI thread. Every one of the four sources above can fire
        /// from a background poll or an HTTP continuation, and the shutdown guards are the house
        /// pattern for a fire-and-forget callback that touches windows (CLAUDE.md, Async/Threading
        /// §6 and §8).
        /// </summary>
        private void QueueNavPremiumTagRefresh()
        {
            try
            {
                if (Application.Current?.Dispatcher == null) return;
                if (Dispatcher.HasShutdownStarted) return;
                Dispatcher.BeginInvoke(new Action(RefreshNavPremiumTags));
            }
            catch (Exception ex) { App.Logger?.Debug("QueueNavPremiumTagRefresh: {E}", ex.Message); }
        }

        /// <summary>
        /// Paints every rail premium tag from the roster. Cheap enough to be unconditional:
        /// eight dictionary-free lookups over a ten-row static list, no allocation that matters,
        /// no clock started.
        /// </summary>
        internal void RefreshNavPremiumTags()
        {
            HookNavPremiumTags();
            try
            {
                foreach (var (tag, key) in NavPremiumTagMap)
                {
                    if (tag == null) continue;
                    tag.Visibility = IsNavEntryLocked(key) ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            catch (Exception ex) { App.Logger?.Debug("RefreshNavPremiumTags: {E}", ex.Message); }
        }

        /// <summary>
        /// "Is this door shut to this account right now?" - three questions, all borrowed:
        /// <list type="number">
        /// <item>the roster's own probe (<see cref="ExclusiveFeature.GateState"/>), which is the
        /// plain premium bar for nine of the ten entries and Graded Intake's weekly-pass check
        /// for the tenth;</item>
        /// <item>the daily rotation, which opens one pool door a day and is the reason
        /// RefreshRailLockbands passes a key to TierGate rather than using the bare overload;</item>
        /// <item>nothing else. A key with no roster row is not sold, so it wears no star - which
        /// is the correct answer for Deeper, Available Subjects, Presets and the rest.</item>
        /// </list>
        /// Fails to NO TAG on anything unexpected, deliberately: an un-starred row that turns out
        /// to be locked costs a TierGate toast the user was going to see anyway, while a starred
        /// row that is actually open is the rail lying about what somebody already paid for.
        /// </summary>
        private static bool IsNavEntryLocked(string exclusiveKey)
        {
            try
            {
                var feature = ExclusiveFeature.All.FirstOrDefault(
                    f => string.Equals(f.Key, exclusiveKey, StringComparison.Ordinal));
                if (feature == null) return false;
                if (feature.GateState() != ExclusiveGateState.Locked) return false;
                if (feature.DailyFreeKey != null &&
                    App.DailyFree?.IsFreeToday(feature.DailyFreeKey) == true) return false;
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("IsNavEntryLocked({Key}): {E}", exclusiveKey, ex.Message);
                return false;
            }
        }
    }
}
