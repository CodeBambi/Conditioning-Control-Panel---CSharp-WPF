// PORTED from ConditioningControlPanel/MainWindow/MainWindow.ProfileBubble.cs (719 lines).
//
// The hover menu opens on a 100ms hover delay, survives a 250ms close grace and dismisses on
// deactivate, minimise/maximise and a click elsewhere - the WPF recipe. RefreshProfileMenu paints
// the name + tier emoji, the reachable achievement count and the Log out / Sign in caption from
// CoreAccount and App.Achievements; the Level/XP rail is painted by UpdateLevelDisplay
// (MainShellWindow.HeroFx.cs). RefreshProfileBubble paints the face (Discord photo when shared ->
// initials on the roster gradient -> "?") and the tier badge on the rim, on sign-in/out
// (UpdateQuickLoginUI) and tier changes (App RepaintVeils). XP / level-up / achievement events
// pulse and glow the bubble (WPF OnBubble*).
//
// THE MENU IS PAINTED IN FULL (wave A, shell#18 / social#21): name, tier badge, achievement count and
// the Log out / Sign in row are painted by RefreshProfileMenu in MainShellWindow.AccountChip.cs.
//
// Controls are reached with Named<T>(name): the window loads with AvaloniaXamlLoader.Load.

using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Avalonia.Threading;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
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

        internal void OpenProfileBubbleMenu()
        {
            var popup = ProfileBubblePopupHost;
            if (popup == null) return;
            try
            {
                RefreshProfileMenu();
                RefreshProfileMenuSpiral();   // WPF RefreshProfileMenu paints the spiral row on the way in
                UpdateLevelDisplay();   // the rail's "Level"/"XP" words follow a language switch
                SubscribeProfileBubbleWatchers();
                // Subscribed per open and dropped again inside the handler, so this never
                // accumulates - Avalonia's Popup.Closed is a plain event with no dedupe.
                popup.Closed += OnProfileBubblePopupClosed;
                popup.IsOpen = true;
                ShowProfileTierBig();
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
            HideProfileTierBig();
            var popup = sender as Popup ?? ProfileBubblePopupHost;
            if (popup != null) popup.Closed -= OnProfileBubblePopupClosed;
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
            // A click INSIDE the menu must not dismiss it before the row's Click runs. Unlike
            // WPF's HWND popup - whose content never reached the owner window at all, which is why
            // the WPF twin needs no such test - Avalonia routes a PopupRoot's input through the
            // Popup to its logical parent, in overlay and separate-window mode alike. So this
            // handler DOES see row presses, and the descendant test is what keeps them.
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

        private void ProfileMenuPublicProfile_Click(object? sender, RoutedEventArgs e)
        {
            CloseProfileBubbleMenu();
            OpenPublicProfilePage();
        }

        /// <summary>WPF OpenPublicProfilePage: the dashboard's sharing page, never /u/&lt;slug&gt; (the app holds
        /// no slug). Shared by the menu row and the Trainer Card's Share Profile button.</summary>
        internal void OpenPublicProfilePage()
        {
            try { OpenProfileLink(ProfileSharingUrl); }
            catch (Exception ex) { Log.Warning(ex, "OpenPublicProfilePage failed"); }
        }

        /// <summary>Test seam: the sandbox guard refuses web links under test.</summary>
        internal static Func<string, bool> OpenProfileLink = ExternalOpener.Open;

        /// <summary>WPF MainWindow.TabNavigation.cs:676.</summary>
        internal const string ProfileSharingUrl = "https://app.cclabs.app/dashboard/profile-sharing";

        /// <summary>WPF RefreshProfileShareButton: visible but disabled signed out, the tooltip says why.</summary>
        internal void RefreshProfileShareButton()
        {
            if (ProfilePage?.FindControl<Button>("BtnProfileShare") is not { } btn) return;
            bool signedIn = CoreAccount.IsLoggedIn;
            btn.IsEnabled = signedIn;
            btn.Opacity = signedIn ? 1.0 : 0.55;
            ToolTip.SetTip(btn, Loc.Get(signedIn ? "profile_btn_share_tip" : "profile_btn_share_tip_locked"));
        }

        // ----- the bubble face ---------------------------------------------------------

        private static readonly Color ProfileBubbleGold = Color.FromRgb(0xFF, 0xD7, 0x00);
        private string? _profileBubbleAvatarUrl;
        private IBrush? _profileBubblePhotoBrush;
        private DateTime _profileBubbleLastXpPulse;
        private DateTime _profileBubbleLastWobble;
        private DateTime _profileBubbleLastShimmer;

        /// <summary>True while the face is the equipped preset bust (tests).</summary>
        internal bool ProfileBubbleShowsBust { get; private set; }
        /// <summary>Wobbles and shimmers started (tests).</summary>
        internal int ProfileBubbleWobbles { get; private set; }
        internal int ProfileBubbleShimmers { get; private set; }

        /// <summary>WPF InitializeProfileBubble's service half: the reaction events (static or app-lived,
        /// so they come off when the window closes, P41) and the first paint.</summary>
        private void InitializeProfileBubble()
        {
            Action<double, string> awarded = (_, _) => Dispatcher.UIThread.Post(OnBubbleXPChanged);
            Action<int> levelUp = _ => Dispatcher.UIThread.Post(OnBubbleLevelUp);
            EventHandler<Achievement> unlocked = (_, _) => Dispatcher.UIThread.Post(OnBubbleAchievementUnlocked);
            var engine = App.Achievements;
            ProgressionBank.Awarded += awarded;
            ProgressionBank.LevelUp += levelUp;
            if (engine != null) engine.Unlocked += unlocked;
            // WPF App.Flash.FlashDisplayed / App.Subliminal.SubliminalDisplayed: Core raises both moments here.
            Action flash = () => Dispatcher.UIThread.Post(OnBubbleFlashDisplayed);
            Action subliminal = () => Dispatcher.UIThread.Post(OnBubbleSubliminalDisplayed);
            CoreTubeEvents.FlashAboutToDisplay += flash;
            CoreTubeEvents.SubliminalDisplayed += subliminal;
            Closed += (_, _) =>
            {
                CoreTubeEvents.FlashAboutToDisplay -= flash;
                CoreTubeEvents.SubliminalDisplayed -= subliminal;
                ProgressionBank.Awarded -= awarded;
                ProgressionBank.LevelUp -= levelUp;
                if (engine != null) engine.Unlocked -= unlocked;
            };
            RefreshProfileBubble();
        }

        /// <summary>WPF RefreshProfileBubble: Discord photo (ShareProfilePicture on) beats initials on the
        /// roster gradient; "?" on slate signed out. Then the tier badge and, if open, the menu.</summary>
        internal void RefreshProfileBubble()
        {
            if (Named<Ellipse>("ProfileBubbleFill") is not { } fill || Named<TextBlock>("ProfileBubbleInitials") is not { } initials) return;
            try
            {
                var loggedIn = CoreAccount.IsLoggedIn;
                var name = CoreAccount.DisplayName;
                initials.Text = loggedIn ? LeaderboardEntryData.BuildInitials(name) : "?";
                fill.Fill = loggedIn ? Tabs.LeaderboardRow.BuildAvatarBrush(name) : ProfileBubbleNeutralBrush;
                initials.IsVisible = true;

                // The equipped preset bust (Own It cosmetic) beats initials (WPF CosmeticsCatalog.GetAvatarImage).
                var bust = Helpers.ModArt.AvatarPreset(CoreSettings.Current.ProfileCosmetics?.AvatarId);
                ProfileBubbleShowsBust = bust != null;
                if (bust != null)
                {
                    fill.Fill = new ImageBrush(bust) { Stretch = Stretch.UniformToFill };
                    initials.IsVisible = false;
                }

                string? url = null;
                if (CoreSettings.Current.ShareProfilePicture && AccountSeed.Discord?.IsAuthenticated == true)
                    url = AccountSeed.Discord.GetAvatarUrl(128);
                if (string.IsNullOrEmpty(url)) { _profileBubbleAvatarUrl = null; _profileBubblePhotoBrush = null; }
                else if (url == _profileBubbleAvatarUrl && _profileBubblePhotoBrush != null) PaintBubblePhoto(_profileBubblePhotoBrush);
                else _ = LoadProfileBubblePhotoAsync(url);

                RefreshProfileBubbleTierBadge();
                RefreshProfileShareButton();
                if (ProfileBubblePopupHost?.IsOpen == true) RefreshProfileMenu();
            }
            catch (Exception ex) { Log.Debug("RefreshProfileBubble: {E}", ex.Message); }
        }

        private void PaintBubblePhoto(IBrush brush)
        {
            if (Named<Ellipse>("ProfileBubbleFill") is { } fill) fill.Fill = brush;
            if (Named<TextBlock>("ProfileBubbleInitials") is { } initials) initials.IsVisible = false;
        }

        /// <summary>Fetch + decode off the UI thread; any failure keeps the initials face.</summary>
        private async Task LoadProfileBubblePhotoAsync(string url)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var bytes = await http.GetByteArrayAsync(url);
                var bitmap = await Task.Run(() => { using var ms = new MemoryStream(bytes); return new Bitmap(ms); });
                Dispatcher.UIThread.Post(() =>
                {
                    _profileBubbleAvatarUrl = url;
                    _profileBubblePhotoBrush = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
                    PaintBubblePhoto(_profileBubblePhotoBrush);
                });
            }
            catch (Exception ex) { Log.Debug("Profile bubble avatar load failed: {E}", ex.Message); }
        }

        // ----- live reactions (WPF OnBubble*) -------------------------------------------

        private void OnBubbleXPChanged()
        {
            if (ProfileBubblePopupHost?.IsOpen == true) RefreshProfileMenu();
            if ((DateTime.UtcNow - _profileBubbleLastXpPulse).TotalMilliseconds < 900) return;
            _profileBubbleLastXpPulse = DateTime.UtcNow;
            PulseProfileBubble(1.10, 260);
        }

        /// <summary>WPF OnBubbleFlashDisplayed: a flash pop wobbles the bubble, at most once per 2.5 s.</summary>
        internal void OnBubbleFlashDisplayed()
        {
            if ((DateTime.UtcNow - _profileBubbleLastWobble).TotalMilliseconds < 2500) return;
            _profileBubbleLastWobble = DateTime.UtcNow;
            WobbleProfileBubble();
        }

        /// <summary>WPF OnBubbleSubliminalDisplayed: a subliminal dims the bubble once, at most once per 4 s.</summary>
        internal void OnBubbleSubliminalDisplayed()
        {
            if ((DateTime.UtcNow - _profileBubbleLastShimmer).TotalMilliseconds < 4000) return;
            _profileBubbleLastShimmer = DateTime.UtcNow;
            ShimmerProfileBubble();
        }

        /// <summary>WPF WobbleProfileBubble: -12, 9, -5, 0 degrees at 90 / 220 / 340 / 480 ms, back to rest.</summary>
        private void WobbleProfileBubble()
        {
            if (!AmbientFxCanvas.Env.AllowTransitions || Named<Grid>("ProfileBubbleVisual") is not { } visual) return;
            var tilt = (visual.RenderTransform as TransformGroup)?.Children.OfType<RotateTransform>().FirstOrDefault();
            if (tilt == null) return;
            ProfileBubbleWobbles++;
            Helpers.TransformTween.Run(tilt, TimeSpan.FromMilliseconds(480), new (double, AvaloniaProperty, double)[]
            {
                (0, RotateTransform.AngleProperty, 0), (90 / 480.0, RotateTransform.AngleProperty, -12),
                (220 / 480.0, RotateTransform.AngleProperty, 9), (340 / 480.0, RotateTransform.AngleProperty, -5),
                (1, RotateTransform.AngleProperty, 0),
            });
        }

        /// <summary>WPF ShimmerProfileBubble: opacity 1 to 0.55 and back, 300 ms each way, sine.</summary>
        private void ShimmerProfileBubble()
        {
            if (!AmbientFxCanvas.Env.AllowTransitions || Named<Grid>("ProfileBubbleVisual") is not { } visual) return;
            ProfileBubbleShimmers++;
            Helpers.TransformTween.Run(visual, TimeSpan.FromMilliseconds(600), new (double, AvaloniaProperty, double)[]
            {
                (0, OpacityProperty, 1.0), (0.5, OpacityProperty, 0.55), (1, OpacityProperty, 1.0),
            }, new SineEaseInOut());
        }

        private void OnBubbleLevelUp()
        {
            PulseProfileBubble(1.35, 560);
            FlashProfileBubbleGlow();
            if (ProfileBubblePopupHost?.IsOpen == true) RefreshProfileMenu();
        }

        private void OnBubbleAchievementUnlocked()
        {
            FlashProfileBubbleGlow();
            PulseProfileBubble(1.18, 380);
            if (ProfileBubblePopupHost?.IsOpen == true) RefreshProfileMenu();
        }

        /// <summary>1 -> peak -> 1 over <paramref name="durationMs"/>, QuadraticEaseOut, auto-reversed.</summary>
        private void PulseProfileBubble(double peak, int durationMs)
        {
            if (!AmbientFxCanvas.Env.AllowTransitions || Named<Grid>("ProfileBubbleVisual") is not { } visual) return;
            _ = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(durationMs / 2.0),
                IterationCount = new IterationCount(2),
                PlaybackDirection = PlaybackDirection.Alternate,
                Easing = new QuadraticEaseOut(),
                Children =
                {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(ScaleTransform.ScaleXProperty, 1.0), new Setter(ScaleTransform.ScaleYProperty, 1.0) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(ScaleTransform.ScaleXProperty, peak), new Setter(ScaleTransform.ScaleYProperty, peak) } },
                },
            }.RunAsync(visual);
        }

        /// <summary>Gold ring: up in 120ms, held to 450ms, out by 1200ms.</summary>
        private void FlashProfileBubbleGlow()
        {
            if (!AmbientFxCanvas.Env.AllowTransitions || Named<Ellipse>("ProfileBubbleGlowRing") is not { } ring) return;
            ring.Stroke = new ImmutableSolidColorBrush(ProfileBubbleGold);
            _ = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(1200),
                Children =
                {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 0.0) } },
                    new KeyFrame { Cue = new Cue(0.1), Setters = { new Setter(OpacityProperty, 1.0) } },
                    new KeyFrame { Cue = new Cue(0.375), Setters = { new Setter(OpacityProperty, 1.0) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 0.0) } },
                },
            }.RunAsync(ring);
        }
    }
}
