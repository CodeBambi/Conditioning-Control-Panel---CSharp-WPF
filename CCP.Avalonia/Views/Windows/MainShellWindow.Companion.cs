// PORTED from ConditioningControlPanel/MainWindow/MainWindow.Companion.cs (312 lines) - the
// avatar tube's window management plus the XP-drain flash, sorted member by member.
//
// WHAT IS REAL HERE. The tube itself is on this head
// (CCP.Avalonia/Views/AvatarTube/AvatarTubeWindow), so the four members that only ever open,
// close, pose and wake it are real ports; so is FlashOverlay, whose WPF DoubleAnimation with
// AutoReverse becomes one Avalonia keyframe Animation of the same total 500ms.
//
// THE OWN-THREAD BRANCH IS GONE, not stubbed. WPF ran the tube on its own STA thread behind
// AppSettings.AvatarOwnThread and pumped a second Dispatcher; Avalonia has ONE UI thread per
// process, and the ported tube says so in its own header - RunOnAvatar marshals to
// Dispatcher.UIThread and the setting has no meaning on this head. There is nothing to port,
// so InitializeAvatarTube is the else-branch alone.
//
// STILL HEAD-SIDE, each with the exact symbol and where it lives today:
//   OnCompanionXPAwarded   - App.Companion's XPAwarded / LevelUp / XPDrained / CompanionSwitched
//   OnCompanionLevelUp       events (ConditioningControlPanel/Services/Companion/CompanionService.cs)
//   OnCompanionXPDrained     and UpdateCompanionCardsUI, which is itself blocked twice over -
//   OnCompanionSwitched      see MainShellWindow.CompanionTab.cs. The level-up half also needs
//                            _trayIcon.ShowNotification (here: Platform.OsNotifications.Show), PlayLevelUpSound (WPF MainWindow.xaml.cs, not ported) and
//                            UpdateLevelDisplay (MainShellWindow.Progression.cs), both stubs.
//   FlashXpBarOnDrain      - restored for the HEADER bar only. Its second overlay,
//                            CompanionTab.PrgCompanion0FlashOverlay, lives inside
//                            CCP.Avalonia/Views/Controls/Companion/CompanionHeroCard.axaml and
//                            CompanionTabView re-publishes none of the room's cell controls
//                            (MainShellWindow.CompanionTab.cs says why), so there is no accessor
//                            path to it. A future wiring reaches it through the hero card.
//   the mute half         - AvatarTubeWindow.SetMuteAvatar and Giggle are in the WPF Speech.cs
//                            partial that did not cross. Detach/Attach did (the tube's
//                            Windowing.cs partial), and OnShellStateForTube below drives them.
//
// _avatarTubeWindow IS DECLARED HERE. WPF declares it in MainWindow.xaml.cs:179, whose twin
// (MainShellWindow.axaml.cs) already lists it in its dropped ledger and is not this layer's to
// own. A partial-class field can be declared exactly once, so whoever restores that file must
// take the field from here rather than re-declare it.
//
// NO CALLER YET for any of it: the tray menu's "Wake Bambi Up!", the companion room's avatar
// toggle (MainShellWindow.CompanionRoom.cs, this layer) and the shell's minimise/restore path
// (MainShellWindow.axaml.cs) are the three call sites, and only the second is on this head.

using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Styling;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>The companion's tube, or null before it has ever been opened. See the header:
        /// this is the one declaration of the field for the whole partial class.</summary>
        private AvatarTubeWindow? _avatarTubeWindow;

        /// <summary>WPF MainWindow.BuildingHiddenForBoot: set while a launcher boot shows the panel only so
        /// its Opened work runs (App.axaml.cs). Nobody sees the panel then, so the tube stays put.</summary>
        internal bool BuildingHiddenForBoot { get; set; }

        // ============================== the XP-drain flash ==============================

        /// <summary>
        /// Pulses the pink overlay on the header XP bar each time Brain Parasite drains. Fired
        /// whatever tab is visible - animating an off-screen Border is harmless.
        /// </summary>
        internal void FlashXpBarOnDrain()
        {
            Log.Information("XP drain flash fired");
            FlashOverlay(Named<Border>("XPBarFlashOverlay"));
        }

        /// <summary>
        /// One 0 -> 1 -> 0 opacity pulse. WPF ran a 250ms DoubleAnimation with AutoReverse; the
        /// Avalonia twin is one 500ms keyframe animation with the peak at the halfway cue, which
        /// is the same curve and leaves the overlay back at its XAML opacity afterwards (FillMode
        /// None) rather than pinned by a local value.
        /// </summary>
        private static void FlashOverlay(Border? overlay)
        {
            if (overlay == null) return;
            var anim = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(500),
                Easing = new QuadraticEaseOut(),
                FillMode = FillMode.None,
                Children =
                {
                    new KeyFrame { Cue = new Cue(0d),   Setters = { new Setter(Visual.OpacityProperty, 0d) } },
                    new KeyFrame { Cue = new Cue(0.5d), Setters = { new Setter(Visual.OpacityProperty, 1d) } },
                    new KeyFrame { Cue = new Cue(1d),   Setters = { new Setter(Visual.OpacityProperty, 0d) } },
                },
            };
            _ = anim.RunAsync(overlay);
        }

        // ============================== the avatar tube ==============================

        /// <summary>
        /// Builds the tube once. Never auto-shows a companion the user has dismissed (#888):
        /// callers that mean to show it anyway flip AvatarEnabled back on first, which is exactly
        /// what <see cref="WakeBambiUp"/> and the room's toggle do.
        /// </summary>
        private void InitializeAvatarTube()
        {
            if (_avatarTubeWindow != null)
            {
                Log.Warning("InitializeAvatarTube called but window already exists");
                return;
            }

            try
            {
                bool showNow = IsVisible && !BuildingHiddenForBoot
                               && WindowState != WindowState.Minimized
                               && CoreSettings.Current.AvatarEnabled;

                _avatarTubeWindow = new AvatarTubeWindow(this);
                PropertyChanged += (_, e) => { if (e.Property == WindowStateProperty) OnShellStateForTube(); };

                // ponytail: AvatarMuted has nowhere to land - AvatarTubeWindow.SetMuteAvatar is in
                // the Speech.cs partial that did not cross. The setting is still read and saved by
                // the room's mute toggle, so it is honoured the moment that partial lands.

                if (showNow)
                {
                    _avatarTubeWindow.Show();
                    _avatarTubeWindow.StartPoseAnimation();
                }

                Log.Information("Avatar Tube Window initialized");
            }
            catch (Exception ex)
            {
                Log.Error("Failed to initialize Avatar Tube Window: {Error}", ex.Message);
            }
        }

        /// <summary>Show her, unless she has been dismissed. Recreates the tube if it was closed.</summary>
        public void ShowAvatarTube()
        {
            if (!CoreSettings.Current.AvatarEnabled) return;

            if (_avatarTubeWindow == null)
            {
                InitializeAvatarTube();
                return;
            }

            // ShowSafe is the ported twin of WPF's ShowTube: it marshals to the UI thread and
            // shows. Z-order pairing is the tube's own business (see its OnOpened).
            _avatarTubeWindow.ShowSafe();
            _avatarTubeWindow.StartPoseAnimation();
        }

        private bool _avatarWasAttachedBeforeMaximize;

        /// <summary>WPF MainWindow.WindowChrome.cs:68-92,414-455: minimise hides an attached tube,
        /// maximise detaches it, restore shows it and re-attaches what maximise detached.</summary>
        private void OnShellStateForTube()
        {
            var tube = _avatarTubeWindow;
            if (tube == null) return;
            Log.Information("AvatarTube: shell state {State}", WindowState);
            if (WindowState == WindowState.Minimized) { HideAvatarTube(); return; }
            if (WindowState == WindowState.Maximized && !tube.IsDetached)
            {
                _avatarWasAttachedBeforeMaximize = true;
                tube.Detach();
            }
            ShowAvatarTube();
            if (WindowState == WindowState.Normal && _avatarWasAttachedBeforeMaximize && tube.IsDetached)
            {
                tube.Attach();
                _avatarWasAttachedBeforeMaximize = false;
            }
        }

        /// <summary>Park her. Skipped for a DETACHED tube so a floating companion survives the
        /// shell minimising (WPF HideAvatarTube).</summary>
        public void HideAvatarTube()
        {
            if (_avatarTubeWindow == null) return;
            if (_avatarTubeWindow.IsDetached && CoreSettings.Current.AvatarEnabled) return;   // dismiss still hides
            _avatarTubeWindow.StopPoseAnimation();
            _avatarTubeWindow.HideSafe(!CoreSettings.Current.AvatarEnabled ? "dismissed"
                : WindowState == WindowState.Minimized ? "main minimised"
                : !IsVisible ? "main hidden" : "shell request");
        }

        /// <summary>
        /// The tray's "Wake Bambi Up!". Waking her is the opposite decision to Dismiss and has to
        /// survive a restart the same way (#888), so AvatarEnabled is written BEFORE the tube is
        /// built - creation and the speech gate both read it.
        /// </summary>
        public void WakeBambiUp()
        {
            try
            {
                if (!CoreSettings.Current.AvatarEnabled)
                {
                    CoreSettings.Current.AvatarEnabled = true;
                    CoreSettings.Save();
                }
            }
            catch (Exception ex)
            {
                Log.Warning("Failed to persist avatar wake: {Error}", ex.Message);
            }

            if (_avatarTubeWindow == null) InitializeAvatarTube();
            if (_avatarTubeWindow != null)
            {
                _avatarTubeWindow.ShowSafe();
                _avatarTubeWindow.StartPoseAnimation();
                _avatarTubeWindow.Detach();
                // ponytail: WPF also has her Giggle("Good morning~!") - Speech.cs has not crossed.
            }
            SyncHero();   // WPF CompanionRoom?.SyncHero(): every on/off label follows the switch
        }

        /// <summary>Force a pose. Poses themselves load in the tube's Avatar.cs partial, which has
        /// not crossed, so on this head this cycles the placeholder set.</summary>
        public void SetAvatarPose(int poseNumber) => _avatarTubeWindow?.SetPose(poseNumber);
    }
}
