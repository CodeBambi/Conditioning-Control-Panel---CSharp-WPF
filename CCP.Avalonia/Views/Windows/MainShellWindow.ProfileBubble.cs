// PORTED-IN-PART from ConditioningControlPanel/MainWindow/MainWindow.ProfileBubble.cs (705 lines).
//
// THE HOVER MENU IS LIVE. The header bubble's account menu opens on a 100ms hover delay, stays up
// across the 250ms close grace while the cursor crosses the popup's transparent 12px shell, and
// dismisses on deactivate, on minimise/maximise and on a click elsewhere in the window - the WPF
// recipe, timing for timing. Its navigation rows are real: Profile, Achievements and Settings are
// one ShowTab call each, exactly what they are on WPF, and a direct click on the bubble skips the
// menu and lands on the Profile tab (key "discord" - "profile" matches no case; see
// MainShellWindow.TabNavigation.cs).
//
// THE MENU IS PAINTED HONEST, NOT FULL. RefreshProfileMenu's identity half reads App.IsLoggedIn,
// App.UserDisplayName, App.Patreon.HasLabAccess/HasPremiumAccess and App.Achievements - none of
// which exist on this head - so the rows those would fill are HIDDEN rather than left showing the
// XAML's placeholders:
//   * ProfileMenuName + ProfileMenuBadge - no display name and no tier truth here.
//   * ProfileMenuXpBlock - AppSettings.PlayerLevel/PlayerXP ARE in Core, but the header's own LVL
//     chip and XP bar are still the XAML's static "Lvl 1" / "0/70 XP" (MainShellWindow.axaml's
//     TxtLevelLabel and XPBar, driven by MainWindow.HeroFx.cs, which is a stub). A menu reading
//     "Lvl 7" two inches under a header reading "Lvl 1" is a worse answer than no rail at all, so
//     the block is hidden until the header is live. It is one edit here to un-hide.
//   * ProfileMenuAccountBtn - the XAML ships it captioned "Log out". Whether that is even the
//     right word needs App.IsLoggedIn, and acting on it needs BtnQuickLogout_Click
//     (ConditioningControlPanel/MainWindow/MainWindow.Login.cs). A row that says "Log out" and
//     does nothing is the exact state-lie this port refuses, so the row is hidden and
//     ProfileMenuAccount_Click stays a stub.
// The result is a menu that shows only what it can prove: four working doors.
//
// Controls are reached with Named<T>(name). MainShellWindow loads with AvaloniaXamlLoader.Load,
// so a `ProfileBubblePopup` field would compile and be null forever.
//
// STILL HEAD-SIDE, each with the exact symbol and where it lives today:
//   InitializeProfileBubble / CleanupProfileBubble - the WPF ctor's one-line wire-up. Its BODY is
//     the service subscriptions below, so there is nothing for it to do yet; the two hover timers
//     are created lazily on first hover instead, which needs no constructor line at all.
//   RefreshProfileBubble, LoadProfileBubblePhotoAsync, _profileBubblePhotoBrush - the bubble FACE.
//     Its resolution order is the Trainer Card's tri-state slot (shared Discord picture -> the
//     equipped preset bust -> initials), which needs App.Discord, the ShareProfilePicture gate's
//     owner and MainWindow.ProfileCosmetics.cs's bust resolver. Painting only the initials half
//     would show "?" to a user whose photo is set and whose sharing is ON - a face that is wrong
//     about who is signed in - so it is not half-ported.
//   OnBubbleXPChanged / OnBubbleLevelUp / OnBubbleAchievementUnlocked / OnBubbleFlashDisplayed /
//     OnBubbleSubliminalDisplayed / OnBubbleAuthChanged, and the four animations they drive
//     (PulseProfileBubble, WobbleProfileBubble, ShimmerProfileBubble, FlashProfileBubbleGlow):
//     blocked at the SOURCE, not at the animation. CCP.Core/CoreProgression.cs is an AddXP
//     provider only - it raises no XPChanged, LevelUp or AchievementUnlocked event - so there is
//     nothing on this head to subscribe to. ProfileBubbleVisual already carries its Scale/Rotate
//     rig and ProfileBubbleGlowRing its parked gold ring, so each of the four is one keyframe
//     Animation once those events exist.
//   OpenPublicProfilePage / the outward half of ProfileMenuPublicProfile_Click - needs
//     Helpers.BrowserLauncher.OpenUrlOrPrompt (ConditioningControlPanel/Helpers/BrowserLauncher.cs)
//     and the ProfileSharingUrl constant (ConditioningControlPanel/MainWindow/
//     MainWindow.TabNavigation.cs:646). Same blocker as MainShellWindow.Marquee.cs's web link and
//     FeatureIntroPopup's "Open the web app". The row still CLOSES the menu, so it is not inert.
//   RefreshProfileShareButton - DiscordTabView's BtnProfileShare plus App.IsLoggedIn; the gate is
//     the same account truth as above.
//   PlaceProfileBubblePopup - WPF's CustomPopupPlacementCallback. Avalonia has no such callback,
//     and the XAML's Placement="BottomEdgeAlignedRight" + PlacementTarget is the same
//     right-aligned result declaratively, so the method is DROPPED rather than stubbed.
//   ProfileBubbleNeutralBrush / MakeFrozenBrush - Freeze() has no Avalonia twin, and the neutral
//     fill is already #3D3D60 in the XAML.
//
// Members of the WPF file still dropped (31): the three reaction throttles
// (_profileBubbleLastXpPulse / -LastWobble / -LastShimmer), _profileBubbleAvatarUrl,
// ProfileBubbleGold, and every member named above.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>Parity with HelpPopover, and with the WPF twin.</summary>
        private const int ProfileBubbleOpenDelayMs = 100;
        private const int ProfileBubbleCloseGraceMs = 250;

        private DispatcherTimer? _profileBubbleOpenTimer;
        private DispatcherTimer? _profileBubbleCloseTimer;
        private bool _profileBubbleWatchersOn;
        private bool _profileBubbleMenuPainted;

        private Popup? ProfileBubblePopupHost => Named<Popup>("ProfileBubblePopup");

        /// <summary>
        /// Creates the two hover timers on first use. WPF does this in the window constructor;
        /// here the constructor is MainShellWindow.axaml.cs's and belongs to another layer, and a
        /// timer nobody has hovered yet costs nothing to defer.
        /// </summary>
        private void EnsureProfileBubbleTimers()
        {
            if (_profileBubbleOpenTimer != null) return;
            _profileBubbleOpenTimer = new DispatcherTimer
            { Interval = TimeSpan.FromMilliseconds(ProfileBubbleOpenDelayMs) };
            _profileBubbleOpenTimer.Tick += OnProfileBubbleOpenTick;
            _profileBubbleCloseTimer = new DispatcherTimer
            { Interval = TimeSpan.FromMilliseconds(ProfileBubbleCloseGraceMs) };
            _profileBubbleCloseTimer.Tick += OnProfileBubbleCloseTick;
        }

        // ----- hover open / close-grace (the HelpPopover recipe) --------------------

        private void ProfileBubble_MouseEnter(object? sender, PointerEventArgs e)
        {
            EnsureProfileBubbleTimers();
            _profileBubbleCloseTimer?.Stop();
            if (ProfileBubblePopupHost?.IsOpen == true) return;
            _profileBubbleOpenTimer?.Start();
        }

        private void ProfileBubble_MouseLeave(object? sender, PointerEventArgs e)
        {
            _profileBubbleOpenTimer?.Stop();
            if (ProfileBubblePopupHost?.IsOpen == true) _profileBubbleCloseTimer?.Start();
        }

        private void ProfileBubblePopupRoot_MouseEnter(object? sender, PointerEventArgs e)
        {
            _profileBubbleOpenTimer?.Stop();
            _profileBubbleCloseTimer?.Stop();
        }

        private void ProfileBubblePopupRoot_MouseLeave(object? sender, PointerEventArgs e)
        {
            EnsureProfileBubbleTimers();
            _profileBubbleCloseTimer?.Start();
        }

        private void OnProfileBubbleOpenTick(object? sender, EventArgs e)
        {
            _profileBubbleOpenTimer?.Stop();
            OpenProfileBubbleMenu();
        }

        private void OnProfileBubbleCloseTick(object? sender, EventArgs e)
        {
            _profileBubbleCloseTimer?.Stop();
            CloseProfileBubbleMenu();
        }

        private void OpenProfileBubbleMenu()
        {
            var popup = ProfileBubblePopupHost;
            if (popup == null) return;
            try
            {
                RefreshProfileMenu();
                SubscribeProfileBubbleWatchers();
                // Subscribed per open and dropped again inside the handler, so this never
                // accumulates - Avalonia's Popup.Closed is a plain event with no dedupe.
                popup.Closed += OnProfileBubblePopupClosed;
                popup.IsOpen = true;
            }
            catch (Exception ex) { Log.Debug("OpenProfileBubbleMenu: {E}", ex.Message); }
        }

        private void CloseProfileBubbleMenu()
        {
            var popup = ProfileBubblePopupHost;
            if (popup != null) popup.IsOpen = false;
        }

        private void OnProfileBubblePopupClosed(object? sender, EventArgs e)
        {
            _profileBubbleOpenTimer?.Stop();
            _profileBubbleCloseTimer?.Stop();
            UnsubscribeProfileBubbleWatchers();
            var popup = sender as Popup ?? ProfileBubblePopupHost;
            if (popup != null) popup.Closed -= OnProfileBubblePopupClosed;
        }

        /// <summary>
        /// Hides every row whose truth source is not on this head, so the menu offers only doors
        /// it can honour. See the header for what each one needs. This is the menu's one paint
        /// choke point, so restoring any of them is an edit here and nowhere else.
        /// </summary>
        private void RefreshProfileMenu()
        {
            if (_profileBubbleMenuPainted) return;   // the hidden set cannot change yet
            _profileBubbleMenuPainted = true;
            try
            {
                Hide("ProfileMenuName");
                Hide("ProfileMenuBadge");
                Hide("ProfileMenuXpBlock");
                Hide("ProfileMenuAccountBtn");
            }
            catch (Exception ex) { Log.Debug("RefreshProfileMenu: {E}", ex.Message); }

            // Instrumented, not silently guarded. A Popup does not open a new namescope, so the
            // window's FindControl reaches its children - but "the x:Name lookup quietly returned
            // null" is this port's most expensive failure mode, and here it would mean the menu
            // opens showing the very rows this method exists to hide.
            void Hide(string name)
            {
                var c = Named<Control>(name);
                if (c == null) { Log.Warning("[ProfileBubble] {Name} not in the window namescope - the menu will show it", name); return; }
                c.IsVisible = false;
            }
        }

        // ----- window-level watchers, live only while the menu is open --------------
        // The menu must never outlive a minimize, an alt-tab or a click somewhere else in the
        // window. WPF's PreviewMouseDown is Avalonia's PointerPressed on the Tunnel strategy; its
        // StateChanged is a WindowState property change.

        private void SubscribeProfileBubbleWatchers()
        {
            if (_profileBubbleWatchersOn) return;
            _profileBubbleWatchersOn = true;
            Deactivated += OnProfileBubbleHostDeactivated;
            PropertyChanged += OnProfileBubbleHostPropertyChanged;
            AddHandler(PointerPressedEvent, OnProfileBubbleWindowPointerPressed,
                       RoutingStrategies.Tunnel, handledEventsToo: true);
        }

        private void UnsubscribeProfileBubbleWatchers()
        {
            if (!_profileBubbleWatchersOn) return;
            _profileBubbleWatchersOn = false;
            Deactivated -= OnProfileBubbleHostDeactivated;
            PropertyChanged -= OnProfileBubbleHostPropertyChanged;
            RemoveHandler(PointerPressedEvent, OnProfileBubbleWindowPointerPressed);
        }

        private void OnProfileBubbleHostDeactivated(object? sender, EventArgs e)
        {
            // Instrumented on purpose: an X11 popup that takes activation for itself would
            // deactivate the shell the instant the menu appeared, and this line is the only way
            // to tell that apart from a close-grace timer firing early. A render proves neither.
            Log.Debug("[ProfileBubble] shell deactivated - closing the account menu");
            CloseProfileBubbleMenu();
        }

        private void OnProfileBubbleHostPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == WindowStateProperty && WindowState != WindowState.Normal)
                CloseProfileBubbleMenu();
        }

        private void OnProfileBubbleWindowPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            // A click INSIDE the menu must not dismiss it before the row's Click runs. On a
            // separate-window popup this handler never sees popup content at all; with overlay
            // popups it does, which is exactly what the descendant test is for.
            if (ProfileBubblePopupHost?.Child is Visual child && e.Source is Visual src &&
                (ReferenceEquals(src, child) || child.IsVisualAncestorOf(src)))
                return;
            CloseProfileBubbleMenu();
        }

        // ----- navigation -----------------------------------------------------------

        private void BtnProfileBubble_Click(object? sender, RoutedEventArgs e)
        {
            _profileBubbleOpenTimer?.Stop();
            _profileBubbleCloseTimer?.Stop();
            CloseProfileBubbleMenu();
            ShowTab("discord");   // "discord" IS the Profile tab; "profile" matches no case
        }

        private void ProfileMenuProfile_Click(object? sender, RoutedEventArgs e)
        {
            CloseProfileBubbleMenu();
            ShowTab("discord");
        }

        private void ProfileMenuAchievements_Click(object? sender, RoutedEventArgs e)
        {
            CloseProfileBubbleMenu();
            ShowTab("achievements");
        }

        private void ProfileMenuSettings_Click(object? sender, RoutedEventArgs e)
        {
            CloseProfileBubbleMenu();
            ShowTab("appsettings");
        }

        /// <summary>Closes the menu and stops there: the door OUT of the app needs a URL launcher
        /// this head does not have yet. See OpenPublicProfilePage in the header.</summary>
        private void ProfileMenuPublicProfile_Click(object? sender, RoutedEventArgs e)
        {
            CloseProfileBubbleMenu();
        }

        /// <summary>
        /// Deliberately inert, and its row is hidden by RefreshProfileMenu. Signed in it must run
        /// the full quick-logout flow (sync-before-clear, provider logouts, repaint) in
        /// MainWindow.Login.cs's BtnQuickLogout_Click; signed out it opens Settings scrolled to
        /// Account. Which of the two it is needs App.IsLoggedIn, and guessing that branch either
        /// drops a logout on the floor or sends a signed-in user to a sign-in page.
        /// </summary>
        private void ProfileMenuAccount_Click(object? sender, RoutedEventArgs e) { }
    }
}
