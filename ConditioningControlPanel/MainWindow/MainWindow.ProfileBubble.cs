using System;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The header profile bubble (top-right cluster, after the "?" help button) and its hover
    /// account menu - the desktop port of the dashboard site's AccountMenu. Three concerns:
    ///
    /// 1. The bubble face. Resolution order mirrors the Trainer Card's tri-state slot
    ///    (MainWindow.ProfileCosmetics.cs): shared Discord picture wins, then the equipped
    ///    preset bust, then initials on the leaderboard's deterministic gradient - so the
    ///    header, the roster and the card all show the same person the same way. The same
    ///    ShareProfilePicture gate applies: the header is on screen far more than the Profile
    ///    tab, so someone who turned sharing off (streamers) never gets their photo here either.
    ///
    /// 2. The hover menu. StaysOpen=true popup with open-delay/close-grace timers and
    ///    window-level watchers subscribed only while open - the HelpPopover recipe, inlined
    ///    because the body is a live account panel, not static help copy. A direct click on
    ///    the bubble skips the menu and jumps to the Profile tab (key "discord", never
    ///    "profile" - see MainWindow.TabNavigation.cs).
    ///
    /// 3. The live reactions. XP gain pulses, level-up bounces + bursts, a flash pop wobbles,
    ///    an achievement unlock flashes the gold ring, a subliminal shimmers. Service events
    ///    fire off the UI thread (same as AvatarTubeWindow's reaction hooks), so every handler
    ///    marshals first; transforms gate on MotionFx.AllowTransitions and bursts ride
    ///    FireBurstAt, which gates itself.
    /// </summary>
    public partial class MainWindow
    {
        private const int ProfileBubbleOpenDelayMs = 100;   // parity with HelpPopover
        private const int ProfileBubbleCloseGraceMs = 250;

        private DispatcherTimer? _profileBubbleOpenTimer;
        private DispatcherTimer? _profileBubbleCloseTimer;
        private bool _profileBubbleWatchersOn;

        /// <summary>URL of the photo currently painted (dedupe so RefreshProfileBubble - which
        /// runs on every auth/XP repaint - never refetches or flickers a loaded picture).</summary>
        private string? _profileBubbleAvatarUrl;
        private ImageBrush? _profileBubblePhotoBrush;

        // Reaction throttles: flashes and XP ticks can arrive several times a second during a
        // session; the bubble should breathe with the app, not vibrate.
        private DateTime _profileBubbleLastXpPulse;
        private DateTime _profileBubbleLastWobble;
        private DateTime _profileBubbleLastShimmer;

        private static readonly Color ProfileBubbleGold = Color.FromRgb(0xFF, 0xD7, 0x00);

        // ----- lifecycle ----------------------------------------------------------

        /// <summary>Wire timers, popup placement and service events. Called once from the
        /// MainWindow constructor; torn down by <see cref="CleanupProfileBubble"/>.</summary>
        private void InitializeProfileBubble()
        {
            try
            {
                _profileBubbleOpenTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ProfileBubbleOpenDelayMs) };
                _profileBubbleOpenTimer.Tick += OnProfileBubbleOpenTick;
                _profileBubbleCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ProfileBubbleCloseGraceMs) };
                _profileBubbleCloseTimer.Tick += OnProfileBubbleCloseTick;

                if (ProfileBubblePopup != null)
                {
                    // Right-align the panel's edge under the bubble's edge. Custom placement is
                    // the only variant that stays correct under the DesignCanvas Viewbox scale -
                    // both sizes arrive in the same (device) space, so no manual scale math.
                    ProfileBubblePopup.CustomPopupPlacementCallback = PlaceProfileBubblePopup;
                    ProfileBubblePopup.Closed += OnProfileBubblePopupClosed;
                }

                if (App.Progression != null)
                {
                    App.Progression.XPChanged += OnBubbleXPChanged;
                    App.Progression.LevelUp += OnBubbleLevelUp;
                }
                if (App.Achievements != null)
                    App.Achievements.AchievementUnlocked += OnBubbleAchievementUnlocked;
                if (App.Flash != null)
                    App.Flash.FlashDisplayed += OnBubbleFlashDisplayed;
                if (App.Subliminal != null)
                    App.Subliminal.SubliminalDisplayed += OnBubbleSubliminalDisplayed;
                if (App.Discord != null)
                    App.Discord.AuthenticationChanged += OnBubbleAuthChanged;

                // The spiral row's own subscription (BlockChanged). Idempotent, and taken
                // here so a block landing before the menu is ever opened still repaints it.
                WireProfileSpiral();

                RefreshProfileBubble();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "InitializeProfileBubble failed");
            }
        }

        /// <summary>Symmetric teardown; called from the window-close cleanup in
        /// MainWindow.WindowChrome.cs alongside the other service unsubscribes.</summary>
        private void CleanupProfileBubble()
        {
            try
            {
                if (App.Progression != null)
                {
                    App.Progression.XPChanged -= OnBubbleXPChanged;
                    App.Progression.LevelUp -= OnBubbleLevelUp;
                }
                if (App.Achievements != null)
                    App.Achievements.AchievementUnlocked -= OnBubbleAchievementUnlocked;
                if (App.Flash != null)
                    App.Flash.FlashDisplayed -= OnBubbleFlashDisplayed;
                if (App.Subliminal != null)
                    App.Subliminal.SubliminalDisplayed -= OnBubbleSubliminalDisplayed;
                if (App.Discord != null)
                    App.Discord.AuthenticationChanged -= OnBubbleAuthChanged;

                UnwireProfileSpiral();

                _profileBubbleOpenTimer?.Stop();
                _profileBubbleCloseTimer?.Stop();
                if (ProfileBubblePopup != null) ProfileBubblePopup.IsOpen = false;
                UnsubscribeProfileBubbleWatchers();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("CleanupProfileBubble: {E}", ex.Message);
            }
        }

        // ----- painting the bubble ------------------------------------------------

        /// <summary>
        /// Repaints the bubble face and (if open) the menu. Rides the same choke point as
        /// RefreshAccountChip (UpdateXPBarLoginState), so every auth change, tier change and
        /// XP repaint lands here without owning a single extra subscription.
        /// </summary>
        private void RefreshProfileBubble()
        {
            // Reachable from UpdateLevelDisplay during early startup, before InitializeComponent
            // has populated the name scope.
            if (BtnProfileBubble == null || ProfileBubbleFill == null || ProfileBubbleInitials == null) return;

            try
            {
                var loggedIn = App.IsLoggedIn;
                var name = App.UserDisplayName;

                // Base layer: initials on the roster's deterministic gradient - the same face
                // the leaderboard gives this user. Neutral slate "?" when signed out.
                if (loggedIn)
                {
                    ProfileBubbleInitials.Text = LeaderboardEntry.BuildInitials(name);
                    ProfileBubbleFill.Fill = LeaderboardEntry.BuildAvatarBrush(name);
                }
                else
                {
                    ProfileBubbleInitials.Text = "?";
                    ProfileBubbleFill.Fill = ProfileBubbleNeutralBrush;
                }
                ProfileBubbleInitials.Visibility = Visibility.Visible;

                // Equipped preset bust (Own It cosmetic) beats initials.
                var presetArt = CosmeticsCatalog.GetAvatarImage(App.Settings?.Current?.ProfileCosmetics?.AvatarId);
                if (presetArt != null)
                {
                    ProfileBubbleFill.Fill = new ImageBrush(presetArt) { Stretch = Stretch.UniformToFill };
                    ProfileBubbleInitials.Visibility = Visibility.Collapsed;
                }

                // Real picture beats both - same ShareProfilePicture gate as the Trainer Card.
                string? url = null;
                if (App.Settings?.Current?.ShareProfilePicture == true && App.Discord?.IsAuthenticated == true)
                    url = App.Discord.GetAvatarUrl(128);

                if (!string.IsNullOrEmpty(url))
                {
                    if (url == _profileBubbleAvatarUrl && _profileBubblePhotoBrush != null)
                    {
                        // Already fetched this exact picture - repaint without a network trip.
                        ProfileBubbleFill.Fill = _profileBubblePhotoBrush;
                        ProfileBubbleInitials.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        _ = LoadProfileBubblePhotoAsync(url);
                    }
                }
                else
                {
                    _profileBubbleAvatarUrl = null;
                    _profileBubblePhotoBrush = null;
                }

                if (ProfileBubblePopup?.IsOpen == true) RefreshProfileMenu();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("RefreshProfileBubble: {E}", ex.Message);
            }
        }

        private static readonly SolidColorBrush ProfileBubbleNeutralBrush = MakeFrozenBrush(0x3D, 0x3D, 0x60);

        private static SolidColorBrush MakeFrozenBrush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        /// <summary>
        /// Fetch + decode off the UI thread, then paint. HttpClient-to-bytes rather than a bare
        /// BitmapImage.UriSource: a remote UriSource downloads asynchronously even with OnLoad,
        /// so its 404/DNS failures escape the surrounding try/catch (see the known caveat on the
        /// Trainer Card's loader) - this way every failure lands in the catch and the bubble
        /// simply keeps its initials/preset face.
        /// </summary>
        private async System.Threading.Tasks.Task LoadProfileBubblePhotoAsync(string url)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var bytes = await http.GetByteArrayAsync(url);

                var bitmap = new BitmapImage();
                using (var stream = new MemoryStream(bytes))
                {
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad; // decode now, release the stream
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                }
                bitmap.Freeze(); // required for the cross-thread handoff

                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;
                await dispatcher.InvokeAsync(() =>
                {
                    _profileBubbleAvatarUrl = url;
                    _profileBubblePhotoBrush = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
                    if (ProfileBubbleFill != null)
                    {
                        ProfileBubbleFill.Fill = _profileBubblePhotoBrush;
                        ProfileBubbleInitials.Visibility = Visibility.Collapsed;
                    }
                });
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Profile bubble avatar load failed: {E}", ex.Message);
            }
        }

        // ----- painting the menu ---------------------------------------------------

        /// <summary>Fills the menu panel: name + tier badge, level/XP rail, achievements recap,
        /// and the logout-vs-sign-in caption. Called on open and on live XP ticks while open.</summary>
        private void RefreshProfileMenu()
        {
            if (ProfileMenuName == null) return;

            try
            {
                var loggedIn = App.IsLoggedIn;
                var name = App.UserDisplayName;

                ProfileMenuName.Text = loggedIn
                    ? (string.IsNullOrWhiteSpace(name) ? Loc.Get("account_chip_signed_in") : name)
                    : Loc.Get("account_chip_sign_in");

                // Same tier truth (and same emoji) as the account chip.
                if (App.Patreon?.HasLabAccess == true)
                {
                    ProfileMenuBadge.Text = "🧪";
                    ProfileMenuBadge.Visibility = Visibility.Visible;
                }
                else if (App.Patreon?.HasPremiumAccess == true)
                {
                    ProfileMenuBadge.Text = "🔒";
                    ProfileMenuBadge.Visibility = Visibility.Visible;
                }
                else
                {
                    ProfileMenuBadge.Visibility = Visibility.Collapsed;
                }

                var level = App.Settings?.Current?.PlayerLevel ?? 1;
                var xp = App.Settings?.Current?.PlayerXP ?? 0;
                var needed = App.Progression?.GetXPForLevel(level) ?? 0;
                ProfileMenuLevel.Text = $"{Loc.Get("label_level")} {level}";
                ProfileMenuXp.Text = $"{xp:F0} / {needed:F0} {Loc.Get("label_xp")}";

                var pct = needed > 0 ? Math.Clamp(xp / needed, 0.0, 1.0) : 0.0;
                if (ProfileMenuXpFillCol != null && ProfileMenuXpRestCol != null)
                {
                    ProfileMenuXpFillCol.Width = new GridLength(pct, GridUnitType.Star);
                    ProfileMenuXpRestCol.Width = new GridLength(1.0 - pct, GridUnitType.Star);
                }

                if (App.Achievements != null && ProfileMenuBadges != null)
                {
                    ProfileMenuBadges.Text = string.Format(
                        Loc.Get("profile_bubble_achievements"),
                        App.Achievements.GetUnlockedCount(), App.Achievements.GetTotalCount());
                }

                if (ProfileMenuAccountBtn != null)
                    ProfileMenuAccountBtn.Content = loggedIn ? Loc.Get("btn_logout") : Loc.Get("account_chip_sign_in");

                // THE SPIRAL ROW (MainWindow.ProfileSpiral.cs). Here rather than in
                // OpenProfileBubbleMenu because this method IS the menu's paint choke
                // point - it runs on open AND on every live tick while open, so a block
                // that landed while the popup was shut is picked up on the way in.
                RefreshProfileMenuSpiral();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("RefreshProfileMenu: {E}", ex.Message);
            }
        }

        // ----- hover open / close-grace (HelpPopover recipe) ------------------------

        private void ProfileBubble_MouseEnter(object sender, MouseEventArgs e)
        {
            _profileBubbleCloseTimer?.Stop();
            if (ProfileBubblePopup?.IsOpen == true) return;
            _profileBubbleOpenTimer?.Start();
        }

        private void ProfileBubble_MouseLeave(object sender, MouseEventArgs e)
        {
            _profileBubbleOpenTimer?.Stop();
            if (ProfileBubblePopup?.IsOpen == true) _profileBubbleCloseTimer?.Start();
        }

        private void ProfileBubblePopupRoot_MouseEnter(object sender, MouseEventArgs e)
        {
            _profileBubbleOpenTimer?.Stop();
            _profileBubbleCloseTimer?.Stop();
        }

        private void ProfileBubblePopupRoot_MouseLeave(object sender, MouseEventArgs e)
        {
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
            if (ProfileBubblePopup != null) ProfileBubblePopup.IsOpen = false;
        }

        private void OpenProfileBubbleMenu()
        {
            if (ProfileBubblePopup == null) return;
            try
            {
                RefreshProfileMenu();
                SubscribeProfileBubbleWatchers();
                ProfileBubblePopup.IsOpen = true;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("OpenProfileBubbleMenu: {E}", ex.Message);
            }
        }

        private void OnProfileBubblePopupClosed(object? sender, EventArgs e)
        {
            _profileBubbleOpenTimer?.Stop();
            _profileBubbleCloseTimer?.Stop();
            UnsubscribeProfileBubbleWatchers();
        }

        private CustomPopupPlacement[] PlaceProfileBubblePopup(Size popupSize, Size targetSize, Point offset)
        {
            return new[]
            {
                // Preferred: panel's right edge under the bubble's right edge (the web layout).
                new CustomPopupPlacement(new Point(targetSize.Width - popupSize.Width, targetSize.Height), PopupPrimaryAxis.Horizontal),
                // Fallback if that runs off-screen left: left-aligned instead.
                new CustomPopupPlacement(new Point(0, targetSize.Height), PopupPrimaryAxis.Horizontal),
            };
        }

        // Window-level watchers, subscribed only while the menu is open - the menu must never
        // outlive a minimize, an alt-tab or a click somewhere else in the window.
        private void SubscribeProfileBubbleWatchers()
        {
            if (_profileBubbleWatchersOn) return;
            _profileBubbleWatchersOn = true;
            Deactivated += OnProfileBubbleHostDeactivated;
            StateChanged += OnProfileBubbleHostStateChanged;
            // handledEventsToo: a click on a control that marks PreviewMouseDown handled must
            // still dismiss the menu. Clicks inside the popup render in its own HWND and never
            // reach this handler; a click on the bubble itself navigates-and-closes anyway.
            AddHandler(PreviewMouseDownEvent, new MouseButtonEventHandler(OnProfileBubbleWindowMouseDown), true);
        }

        private void UnsubscribeProfileBubbleWatchers()
        {
            if (!_profileBubbleWatchersOn) return;
            _profileBubbleWatchersOn = false;
            Deactivated -= OnProfileBubbleHostDeactivated;
            StateChanged -= OnProfileBubbleHostStateChanged;
            RemoveHandler(PreviewMouseDownEvent, new MouseButtonEventHandler(OnProfileBubbleWindowMouseDown));
        }

        private void OnProfileBubbleHostDeactivated(object? sender, EventArgs e)
        {
            if (ProfileBubblePopup != null) ProfileBubblePopup.IsOpen = false;
        }

        private void OnProfileBubbleHostStateChanged(object? sender, EventArgs e)
        {
            if (WindowState != WindowState.Normal && ProfileBubblePopup != null)
                ProfileBubblePopup.IsOpen = false;
        }

        private void OnProfileBubbleWindowMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (ProfileBubblePopup != null) ProfileBubblePopup.IsOpen = false;
        }

        // ----- navigation -----------------------------------------------------------

        private void BtnProfileBubble_Click(object sender, RoutedEventArgs e)
        {
            _profileBubbleOpenTimer?.Stop();
            _profileBubbleCloseTimer?.Stop();
            if (ProfileBubblePopup != null) ProfileBubblePopup.IsOpen = false;
            ShowTab("discord"); // "discord" IS the Profile tab; "profile" matches no case
        }

        private void ProfileMenuProfile_Click(object sender, RoutedEventArgs e)
        {
            if (ProfileBubblePopup != null) ProfileBubblePopup.IsOpen = false;
            ShowTab("discord");
        }

        private void ProfileMenuAchievements_Click(object sender, RoutedEventArgs e)
        {
            if (ProfileBubblePopup != null) ProfileBubblePopup.IsOpen = false;
            ShowTab("achievements");
        }

        private void ProfileMenuSettings_Click(object sender, RoutedEventArgs e)
        {
            if (ProfileBubblePopup != null) ProfileBubblePopup.IsOpen = false;
            ShowTab("appsettings");
        }

        private void ProfileMenuAccount_Click(object sender, RoutedEventArgs e)
        {
            if (ProfileBubblePopup != null) ProfileBubblePopup.IsOpen = false;
            if (App.IsLoggedIn)
            {
                // The full quick-logout flow (sync-before-clear, provider logouts, UI repaint).
                BtnQuickLogout_Click(this, new RoutedEventArgs());
            }
            else
            {
                // Same door the account chip opens: Settings scrolled to Account.
                ShowTab("appsettings");
                AppSettingsTab?.FocusSection("account");
            }
        }

        // ----- live reactions --------------------------------------------------------

        /// <summary>Service events fire off the UI thread; hop over (and swallow shutdown races).</summary>
        private void RunOnBubbleUi(Action action)
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;
                if (dispatcher.CheckAccess()) action();
                else dispatcher.BeginInvoke(action);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Profile bubble UI hop failed: {E}", ex.Message);
            }
        }

        private void OnBubbleXPChanged(object? sender, double xpIntoLevel)
        {
            RunOnBubbleUi(() =>
            {
                try
                {
                    if (ProfileBubblePopup?.IsOpen == true) RefreshProfileMenu();
                    if ((DateTime.UtcNow - _profileBubbleLastXpPulse).TotalMilliseconds < 900) return;
                    _profileBubbleLastXpPulse = DateTime.UtcNow;
                    PulseProfileBubble(1.10, 260);
                }
                catch (Exception ex) { App.Logger?.Debug("Bubble XP reaction: {E}", ex.Message); }
            });
        }

        private void OnBubbleLevelUp(object? sender, int newLevel)
        {
            RunOnBubbleUi(() =>
            {
                try
                {
                    PulseProfileBubble(1.35, 560);
                    FlashProfileBubbleGlow(ProfileBubbleGold);
                    // Second, smaller burst up here (CelebrateLevelUp already bursts the XP bar) -
                    // FireBurstAt gates itself on EventFxAllowed, so no extra checks needed.
                    FireBurstAt(BtnProfileBubble, count: 45);
                    if (ProfileBubblePopup?.IsOpen == true) RefreshProfileMenu();
                }
                catch (Exception ex) { App.Logger?.Debug("Bubble level-up reaction: {E}", ex.Message); }
            });
        }

        private void OnBubbleAchievementUnlocked(object? sender, Achievement achievement)
        {
            RunOnBubbleUi(() =>
            {
                try
                {
                    FlashProfileBubbleGlow(ProfileBubbleGold);
                    PulseProfileBubble(1.18, 380);
                    if (ProfileBubblePopup?.IsOpen == true) RefreshProfileMenu();
                }
                catch (Exception ex) { App.Logger?.Debug("Bubble achievement reaction: {E}", ex.Message); }
            });
        }

        private void OnBubbleFlashDisplayed(object? sender, EventArgs e)
        {
            RunOnBubbleUi(() =>
            {
                try
                {
                    if ((DateTime.UtcNow - _profileBubbleLastWobble).TotalMilliseconds < 2500) return;
                    _profileBubbleLastWobble = DateTime.UtcNow;
                    WobbleProfileBubble();
                }
                catch (Exception ex) { App.Logger?.Debug("Bubble flash reaction: {E}", ex.Message); }
            });
        }

        private void OnBubbleSubliminalDisplayed(object? sender, EventArgs e)
        {
            RunOnBubbleUi(() =>
            {
                try
                {
                    if ((DateTime.UtcNow - _profileBubbleLastShimmer).TotalMilliseconds < 4000) return;
                    _profileBubbleLastShimmer = DateTime.UtcNow;
                    ShimmerProfileBubble();
                }
                catch (Exception ex) { App.Logger?.Debug("Bubble subliminal reaction: {E}", ex.Message); }
            });
        }

        private void OnBubbleAuthChanged(object? sender, bool authenticated)
        {
            RunOnBubbleUi(RefreshProfileBubble);
        }

        // ----- the animations themselves ----------------------------------------------

        /// <summary>Heartbeat: scale up to <paramref name="peak"/> and settle back.</summary>
        private void PulseProfileBubble(double peak, int durationMs)
        {
            if (!MotionFx.AllowTransitions || ProfileBubbleScale == null) return;
            var half = TimeSpan.FromMilliseconds(durationMs / 2.0);
            var pulse = new DoubleAnimation(1.0, peak, half)
            {
                AutoReverse = true,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };
            ProfileBubbleScale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
            ProfileBubbleScale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
        }

        /// <summary>Flash-pop reaction: a quick decaying tilt, like the bubble got flicked.</summary>
        private void WobbleProfileBubble()
        {
            if (!MotionFx.AllowTransitions || ProfileBubbleTilt == null) return;
            var wobble = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromMilliseconds(480),
                FillBehavior = FillBehavior.Stop
            };
            wobble.KeyFrames.Add(new EasingDoubleKeyFrame(-12, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(90))));
            wobble.KeyFrames.Add(new EasingDoubleKeyFrame(9, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220))));
            wobble.KeyFrames.Add(new EasingDoubleKeyFrame(-5, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(340))));
            wobble.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(480))));
            ProfileBubbleTilt.BeginAnimation(RotateTransform.AngleProperty, wobble);
        }

        /// <summary>Subliminal reaction: a barely-there opacity dip - subliminal, fittingly.</summary>
        private void ShimmerProfileBubble()
        {
            if (!MotionFx.AllowTransitions || ProfileBubbleVisual == null) return;
            var dip = new DoubleAnimation(1.0, 0.55, TimeSpan.FromMilliseconds(300))
            {
                AutoReverse = true,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                FillBehavior = FillBehavior.Stop
            };
            ProfileBubbleVisual.BeginAnimation(OpacityProperty, dip);
        }

        /// <summary>Celebration ring: snap the glow ring visible, hold a beat, fade it out.</summary>
        private void FlashProfileBubbleGlow(Color color)
        {
            if (!MotionFx.AllowTransitions || ProfileBubbleGlowRing == null) return;
            ProfileBubbleGlowRing.Stroke = new SolidColorBrush(color);
            var flash = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromMilliseconds(1200),
                FillBehavior = FillBehavior.Stop
            };
            flash.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120))));
            flash.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(450))));
            flash.KeyFrames.Add(new EasingDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1200)),
                new QuadraticEase { EasingMode = EasingMode.EaseOut }));
            ProfileBubbleGlowRing.BeginAnimation(OpacityProperty, flash);
        }
    }
}
