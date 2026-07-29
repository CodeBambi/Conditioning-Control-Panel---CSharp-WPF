using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Rectangle = System.Windows.Shapes.Rectangle;
using NAudio.Wave;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    // Marquee/banner system: banner rotation, marquee banner + animation, server update banner, server announcements.
    public partial class MainWindow
    {
        #region Banner Rotation

        private void InitializeBannerRotation()
        {
            // Start the rotation timer (switches every 4 seconds)
            _bannerRotationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(4)
            };
            _bannerRotationTimer.Tick += BannerRotationTimer_Tick;

            // Update welcome message based on login status
            UpdateBannerWelcomeMessage();

            // Always start rotation now (we have 3 messages including the thanks message)
            _bannerRotationTimer.Start();
        }

        private void UpdateBannerWelcomeMessage()
        {
            // Check offline mode first
            if (App.Settings?.Current?.OfflineMode == true &&
                !string.IsNullOrWhiteSpace(App.Settings?.Current?.OfflineUsername))
            {
                TxtBannerSecondary.Text = Loc.GetF("label_welcome_back_0_offline_mode", App.Settings.Current.OfflineUsername);
                return;
            }

            // Check unified display name first, then fall back to provider-specific
            var displayName = App.Settings?.Current?.UserDisplayName
                           ?? App.Patreon?.DisplayName
                           ?? App.Discord?.DisplayName;
            if (!string.IsNullOrEmpty(displayName))
            {
                TxtBannerSecondary.Text = Loc.GetF("label_welcome_back_0", displayName);
            }
            else
            {
                // Not logged in - show generic welcome
                TxtBannerSecondary.Text = Loc.Get("label_welcome_consider_logging_in_with_patreon_for");
            }
        }

        /// <summary>
        /// Shows the "Welcome Back, Pioneer!" popup for Season 0 OG users
        /// </summary>
        private void ShowOgWelcomePopup()
        {
            try
            {
                var dialog = new Window
                {
                    Title = Loc.Get("title_welcome_back"),
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    SizeToContent = SizeToContent.WidthAndHeight,
                    ResizeMode = ResizeMode.NoResize,
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = System.Windows.Media.Brushes.Transparent
                };

                var border = new System.Windows.Controls.Border
                {
                    BorderBrush = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0xFF, 0xD7, 0x00)), // Gold
                    BorderThickness = new Thickness(2),
                    CornerRadius = new CornerRadius(10),
                    Background = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0x1A, 0x1A, 0x2E)),
                    Padding = new Thickness(30)
                };

                var stack = new System.Windows.Controls.StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    MaxWidth = 400
                };

                // Star header
                stack.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = "⭐ Welcome Back, Pioneer! ⭐",
                    FontSize = 24,
                    FontWeight = FontWeights.Bold,
                    Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0xFF, 0xD7, 0x00)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 15)
                });

                // Message
                stack.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = "You've been recognized as a Season 0 OG.\n\n" +
                           "Your account has been reset for Season 1, but your legacy lives on:\n\n" +
                           "  ⭐ Your name now has a star icon on the leaderboard\n" +
                           "  ✨ Your row is highlighted in gold\n" +
                           "  👑 Everyone will know you were here from the beginning\n\n" +
                           "Your unlocks and achievements have been preserved.\n" +
                           "Good luck climbing the leaderboard again!",
                    FontSize = 13,
                    Foreground = System.Windows.Media.Brushes.White,
                    TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 0, 0, 20)
                });

                // Continue button
                var button = new System.Windows.Controls.Button
                {
                    Content = "Continue",
                    Padding = new Thickness(30, 10, 30, 10),
                    Background = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0xFF, 0x69, 0xB4)),
                    Foreground = System.Windows.Media.Brushes.White,
                    BorderThickness = new Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                button.Click += (s, e) => dialog.Close();
                stack.Children.Add(button);

                border.Child = stack;
                dialog.Content = border;
                dialog.MouseLeftButtonDown += (s, e) =>
                {
                    if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
                        dialog.DragMove();
                };

                dialog.ShowDialog();

                // Mark as shown so we don't show again
                if (App.Settings?.Current != null)
                {
                    App.Settings.Current.HasShownOgWelcome = true;
                    App.Settings.Save();
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to show OG welcome popup");
            }
        }

        /// <summary>
        /// Flag to indicate when a startup dialog (What's New) is showing.
        /// Used to prevent update dialog from showing behind it.
        /// </summary>
        public static bool IsStartupDialogShowing { get; set; } = false;

        /// <summary>
        /// Shows a "What's New" dialog if the app was updated since last launch
        /// </summary>
        // Season Recap is shown at most once per app run; guards the two trigger paths
        // (startup month-check and the server-reset nudge from ProfileSyncService).
        private bool _seasonRecapShown;

        /// <summary>
        /// Presents the Season Recap card when the user has been reset. Triggers on EITHER:
        ///   • a monthly rollover (UTC month != LastSeasonResetSeen) — fires on any day of the
        ///     new month, not just the 1st; or
        ///   • a server-driven reset (AppSettings.SeasonResetPending, set by ProfileSyncService
        ///     when the server returns level_reset) — this is how an admin reset of a single
        ///     account surfaces the card mid-month, and makes the feature testable.
        ///
        /// Snapshots the just-ended season BEFORE clearing its counters, then shows the card
        /// (or the legacy textual notice when there's no season data yet). The actual level/XP/
        /// streak reset still happens via the server + SkillTreeService — this only wraps it.
        /// Safe to call repeatedly; shows at most once per app run. Public so ProfileSyncService
        /// can nudge it the moment a reset arrives.
        /// </summary>
        public void TryPresentSeasonRecap()
        {
            try
            {
                if (_seasonRecapShown) return;
                if (App.Settings?.Current == null) return;

                // Season boundary is server-authoritative (SeasonRecapService.CurrentSeasonKey prefers the
                // server's CurrentSeason over wall-clock). Using wall-clock here made the recap fire on the
                // local 1st-of-month before the server actually ended the season, rolling the bucket early
                // and losing the real month's totals.
                var currentSeason = Services.SeasonRecapService.CurrentSeasonKey;
                var lastSeasonSeen = App.Settings.Current.LastSeasonResetSeen ?? "";
                var highestLevel = App.Settings.Current.HighestLevelEver;
                var resetPending = App.Settings.Current.SeasonResetPending;

                // Brand-new users (never leveled up) skip this. They'll see it once they progress.
                if (highestLevel < 2) return;

                // Seasons only ever move FORWARD. Fire only when the current season is strictly AFTER the
                // last one we showed a recap for — never on an equal or backward (desynced) key. Ordinal
                // compare is chronological for zero-padded yyyy-MM; an empty lastSeasonSeen (first run) is
                // "before" any real key, so first-timers still fire.
                var monthRolled = string.CompareOrdinal(currentSeason, lastSeasonSeen) > 0;

                // A replayed server level_reset can leave SeasonResetPending set on an upgrade launch even
                // when the season didn't actually change. Only treat it as a real pending reset if the current
                // (server) season is strictly ahead of the live stats bucket; otherwise clear the stale latch
                // and skip, so we don't fire a spurious "Season N ended" recap for the in-progress month (#450)
                // or a backward one during a wall-clock/server desync.
                var statsSeason = App.Settings.Current.SeasonStatsSeason ?? "";
                var reallyPending = resetPending && string.CompareOrdinal(currentSeason, statsSeason) > 0;
                if (!monthRolled && !reallyPending)
                {
                    if (resetPending)
                    {
                        App.Settings.Current.SeasonResetPending = false;
                        App.Settings.Save();
                    }
                    return;
                }

                _seasonRecapShown = true;
                App.Logger?.Information("Presenting season recap (monthRolled={Month}, resetPending={Pending}, last={Old}, current={New}, highestLevel={Highest})",
                    monthRolled, resetPending, string.IsNullOrEmpty(lastSeasonSeen) ? "(none)" : lastSeasonSeen, currentSeason, highestLevel);

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        IsStartupDialogShowing = true;

                        // Snapshot the just-ended season BEFORE its counters are cleared, then roll
                        // the bucket. CaptureAndRollover writes the JSON first and only then clears —
                        // order is load-bearing (an empty snapshot = an empty card).
                        var snapshot = Services.SeasonRecapService.CaptureAndRollover(currentSeason);

                        // Advance the persisted idempotency latch IMMEDIATELY after the
                        // destructive roll and BEFORE presenting the card. CaptureAndRollover
                        // has already written the snapshot (if any) and cleared the live
                        // counters. If we deferred this write until after ShowDialog and the
                        // window threw (XAML resource lookups in a DataTemplate are a known
                        // hazard in this codebase), the catch below would swallow it, the latch
                        // would never advance, and the next launch would re-roll the now-empty
                        // season — permanently losing the real recap. Persist the latch first.
                        App.Settings.Current.LastSeasonResetSeen = currentSeason;
                        App.Settings.Current.SeasonResetPending = false;
                        App.Settings.Save();

                        if (snapshot != null)
                        {
                            var vm = new ViewModels.SeasonRecapViewModel(snapshot);
                            var recapWindow = new Controls.SeasonRecapWindow(vm) { Owner = this };
                            recapWindow.ShowDialog();
                        }
                        else
                        {
                            // No meaningful season data yet (e.g. first reset after this feature
                            // shipped, before any tracking accrued) — fall back to the legacy notice
                            // so the user still understands what happened.
                            var message =
                                "The monthly leaderboard season has rotated. This happens at the start of every month so everyone has a fresh chance to climb the rankings.\n\n" +
                                "What resets:\n" +
                                "  - Current Level and XP\n" +
                                "  - Daily quest streak\n" +
                                "  - Monthly leaderboard position\n" +
                                "  - Mechanical enhancements (re-buy them to raise your Prestige)\n\n" +
                                "What's preserved:\n" +
                                "  - All achievements\n" +
                                "  - Highest Level Ever (yours: " + highestLevel + ")\n" +
                                "  - Your sparkle points balance\n" +
                                "  - Permanent stat enhancements and your Prestige\n" +
                                "  - Total lifetime XP\n" +
                                "  - Patreon perks and whitelist\n\n" +
                                "Welcome to season " + currentSeason + "!";

                            MessageBox.Show(
                                message,
                                "New Season Started",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning(ex, "Failed to present season recap");
                    }
                    finally
                    {
                        IsStartupDialogShowing = false;
                    }
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Error checking for season recap");
            }
        }

        private void ShowWhatsNewIfNeeded()
        {
            try
            {
                var currentVersion = Services.UpdateService.AppVersion;
                var lastSeenVersion = App.Settings?.Current?.LastSeenVersion ?? "";

                // If versions differ, show the patch notes
                if (lastSeenVersion != currentVersion)
                {
                    App.Logger?.Information("Version changed from {OldVersion} to {NewVersion}, showing What's New",
                        string.IsNullOrEmpty(lastSeenVersion) ? "(none)" : lastSeenVersion, currentVersion);

                    // Delay slightly to let the window fully load
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            // Set flag BEFORE showing MessageBox so update dialog knows to wait
                            IsStartupDialogShowing = true;
                            App.Logger?.Information("What's New dialog showing, setting IsStartupDialogShowing=true");

                            var whatsNew = new WhatsNewDialog(
                                $"What's New in v{currentVersion}",
                                Services.UpdateService.CurrentPatchNotes)
                            {
                                Owner = this
                            };
                            whatsNew.ShowDialog();

                            // Update the last seen version
                            if (App.Settings?.Current != null)
                            {
                                App.Settings.Current.LastSeenVersion = currentVersion;
                                App.Settings.Save();
                            }
                        }
                        catch (Exception ex)
                        {
                            App.Logger?.Warning(ex, "Failed to show What's New dialog");
                        }
                        finally
                        {
                            // Clear flag AFTER MessageBox is dismissed
                            IsStartupDialogShowing = false;
                            App.Logger?.Information("What's New dialog dismissed, setting IsStartupDialogShowing=false");
                        }
                    }), System.Windows.Threading.DispatcherPriority.Loaded);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Error checking for What's New");
            }
        }

        private void BannerRotationTimer_Tick(object? sender, EventArgs e)
        {
            // Get the 3 banner textblocks
            var banners = new[] { TxtBannerPrimary, TxtBannerSecondary, TxtBannerTertiary };

            // Determine which one to fade out and which to fade in
            var fadeOutTarget = banners[_bannerCurrentIndex];
            var nextIndex = (_bannerCurrentIndex + 1) % 3;
            var fadeInTarget = banners[nextIndex];

            // Create fade animations
            var fadeOut = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(500),
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
            };

            var fadeIn = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(500),
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
            };

            // Apply animations
            fadeOutTarget.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            fadeInTarget.BeginAnimation(UIElement.OpacityProperty, fadeIn);

            // Disable hit testing on faded-out banner so hyperlinks don't capture clicks
            // (hyperlinks can still receive clicks even at Opacity=0)
            fadeOutTarget.IsHitTestVisible = false;
            fadeInTarget.IsHitTestVisible = true;

            _bannerCurrentIndex = nextIndex;
        }

        /// <summary>
        /// Set a temporary announcement message to display in the banner rotation
        /// </summary>
        public void SetBannerAnnouncement(string message)
        {
            if (string.IsNullOrEmpty(message)) return;

            TxtBannerSecondary.Text = message;

            // Ensure timer is running
            if (_bannerRotationTimer != null && !_bannerRotationTimer.IsEnabled)
            {
                _bannerRotationTimer.Start();
            }
        }

        #endregion

        #region Marquee Banner

        private void InitializeMarqueeBanner()
        {
            try
            {
                // Migrate old message to new default if needed
                var currentSaved = App.Settings.Current.MarqueeMessage;
                if (string.IsNullOrWhiteSpace(currentSaved) ||
                    currentSaved.Contains("WELCOME TO YOUR CONDITIONING") ||
                    currentSaved.Contains("RELAX AND SUBMIT"))
                {
                    App.Settings.Current.MarqueeMessage = "GOOD GIRLS CONDITION DAILY     ❤️🔒";
                }

                // Need to wait for layout to measure text width
                SettingsTab.MarqueeText.Loaded += (s, e) => StartMarqueeAnimation();
                SettingsTab.MarqueeCanvas.SizeChanged += (s, e) => StartMarqueeAnimation();

                // Start immediately if already loaded
                if (SettingsTab.MarqueeText.IsLoaded)
                {
                    Dispatcher.BeginInvoke(new Action(StartMarqueeAnimation), System.Windows.Threading.DispatcherPriority.Loaded);
                }

                // Fetch from server on startup (with short delay)
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _ = Task.Delay(3000).ContinueWith(_ =>
                    {
                        if (Application.Current?.Dispatcher?.HasShutdownStarted == true) return;
                        Dispatcher.Invoke(RefreshMarqueeFromSettings);
                    });
                }));

                // Check for server-controlled update banner (fallback for when auto-update fails)
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _ = Task.Delay(5000).ContinueWith(_ =>
                    {
                        if (Application.Current?.Dispatcher?.HasShutdownStarted == true) return;
                        Dispatcher.Invoke(CheckServerUpdateBanner);
                    });
                }));

                // Check for server-triggered announcement popup
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _ = Task.Delay(7000).ContinueWith(_ =>
                    {
                        if (Application.Current?.Dispatcher?.HasShutdownStarted == true) return;
                        Dispatcher.Invoke(CheckServerAnnouncement);
                    });
                }));

                // Weekly intake pass nudge. Deliberately LAST and well behind the server
                // announcement's 7s: the two use the same popup window, and stacking them on
                // one launch turns a nudge into an ambush. CheckIntakePassNudge bails outright
                // if the announcement above actually fired.
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _ = Task.Delay(14000).ContinueWith(_ =>
                    {
                        if (Application.Current?.Dispatcher?.HasShutdownStarted == true) return;
                        Dispatcher.Invoke(CheckIntakePassNudge);
                    });
                }));

                // Start 5-minute refresh timer to check for server-side message updates
                _marqueeRefreshTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMinutes(5)
                };
                _marqueeRefreshTimer.Tick += (s, e) => RefreshMarqueeFromSettings();
                _marqueeRefreshTimer.Start();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to initialize marquee banner: {Error}", ex.Message);
            }
        }

        private async void RefreshMarqueeFromSettings()
        {
            try
            {
                // Fetch marquee message from server
                using var httpClient = new System.Net.Http.HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(10)
                };

                var response = await httpClient.GetAsync("https://codebambi-proxy.vercel.app/config/marquee");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var result = System.Text.Json.JsonSerializer.Deserialize<MarqueeResponse>(json);
                    var newMessage = result?.message;

                    if (!string.IsNullOrWhiteSpace(newMessage) && newMessage != _currentMarqueeMessage)
                    {
                        App.Logger?.Information("Marquee message updated from server: {Message}", newMessage);
                        App.Settings.Current.MarqueeMessage = newMessage;
                        Dispatcher.Invoke(() => StartMarqueeAnimation());
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to refresh marquee from server: {Error}", ex.Message);
            }
        }

        private class MarqueeResponse
        {
            public string? message { get; set; }
        }

        #endregion

        #region Server-Controlled Update Banner

        private class UpdateBannerResponse
        {
            public bool enabled { get; set; }
            public string? version { get; set; }
            public string? message { get; set; }
            public string? url { get; set; }
        }

        // Store the server-provided update URL for redirect
        private string? _serverUpdateUrl;

        /// <summary>
        /// Check server for forced update banner configuration.
        /// This is a fallback when automatic update detection fails.
        /// </summary>
        private async void CheckServerUpdateBanner()
        {
            try
            {
                using var httpClient = new System.Net.Http.HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(10)
                };

                var response = await httpClient.GetAsync("https://codebambi-proxy.vercel.app/config/update-banner");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var result = System.Text.Json.JsonSerializer.Deserialize<UpdateBannerResponse>(json);

                    if (result?.enabled == true && !string.IsNullOrWhiteSpace(result.version))
                    {
                        // Check if user is on an older version than the one in the banner
                        var currentVersion = Services.UpdateService.GetCurrentVersion();
                        if (Version.TryParse(result.version, out var bannerVersion) && bannerVersion > currentVersion)
                        {
                            App.Logger?.Information("Server update banner enabled: version={Version}, message={Message}",
                                result.version, result.message);

                            // Store the URL if provided
                            _serverUpdateUrl = result.url;

                            // Update the button on UI thread
                            Dispatcher.Invoke(() =>
                            {
                                if (BtnUpdateAvailable != null)
                                {
                                    BtnUpdateAvailable.Tag = "UrgentUpdate";
                                    BtnUpdateAvailable.Content = $"UPDATE AVAILABLE v{result.version}";
                                    BtnUpdateAvailable.ToolTip = !string.IsNullOrEmpty(result.url)
                                        ? $"Version {result.version} is available - Click to visit download page!"
                                        : $"Version {result.version} is available - Click to update!";
                                }
                            });
                        }
                        else
                        {
                            App.Logger?.Debug("Server update banner: user already on version {Current}, banner is for {Banner}",
                                currentVersion, result.version);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to check server update banner: {Error}", ex.Message);
            }
        }

        #endregion

        #region Server-Triggered Announcement

        private class AnnouncementResponse
        {
            public bool enabled { get; set; }
            public string? id { get; set; }
            public string? title { get; set; }
            public string? message { get; set; }
            public string? image_url { get; set; }
            public string? link_url { get; set; }
            public string? theme { get; set; }
        }

        /// <summary>
        /// Check server for a triggered announcement popup. Shows once per unique announcement ID.
        /// </summary>
        private async void CheckServerAnnouncement()
        {
            try
            {
                using var httpClient = new System.Net.Http.HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(10)
                };

                var url = "https://codebambi-proxy.vercel.app/config/announcement";
                var unifiedId = App.Settings?.Current?.UnifiedId;
                if (!string.IsNullOrWhiteSpace(unifiedId))
                {
                    url += $"?unified_id={Uri.EscapeDataString(unifiedId)}";
                }

                var response = await httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var result = System.Text.Json.JsonSerializer.Deserialize<AnnouncementResponse>(json);

                    if (result?.enabled == true
                        && !string.IsNullOrWhiteSpace(result.id)
                        && !string.IsNullOrWhiteSpace(result.title)
                        && result.id != App.Settings?.Current?.DismissedAnnouncementId)
                    {
                        App.Logger?.Information("Server announcement received: id={Id}, title={Title}", result.id, result.title);

                        Dispatcher.Invoke(() =>
                        {
                            // Claim the launch's one popup slot so the weekly intake nudge
                            // stands down - see CheckIntakePassNudge.
                            _serverAnnouncementShownThisLaunch = true;

                            var popup = new AnnouncementPopup(
                                result.id!,
                                result.title!,
                                result.message ?? "",
                                result.image_url,
                                result.link_url,
                                result.theme);
                            popup.Show();
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to check server announcement: {Error}", ex.Message);
            }
        }

        #endregion

        #region Weekly Intake Pass Nudge

        /// <summary>A server announcement already used this launch's popup budget.</summary>
        private bool _serverAnnouncementShownThisLaunch;

        /// <summary>
        /// Once a week, tell a free user their Graded Intake pass is waiting.
        ///
        /// This is deliberately LOCAL rather than riding the server announcement pipeline, for
        /// two reasons. The endpoint serves ONE global announcement, so it cannot express
        /// "whichever week this particular user is on"; and the client remembers exactly one
        /// <c>DismissedAnnouncementId</c>, so a recurring popup routed through it would eat the
        /// slot the real announcements need. It borrows only the window - with its own dismissal
        /// record, via the popup's onDismiss hook.
        ///
        /// Every condition below is a reason NOT to interrupt someone. A weekly popup earns its
        /// place by being rare and well-timed; one that fires during a session, or twice in a
        /// launch, or after being told no, is just noise the user will file a bug about.
        /// </summary>
        private void CheckIntakePassNudge()
        {
            try
            {
                var settings = App.Settings?.Current;
                var pass = App.IntakePass;
                if (settings == null || pass == null) return;

                // Turned off by the user.
                if (!settings.IntakeNudgeEnabled) return;

                // Nothing to advertise: patron, signed out, or already ran it this week.
                if (!pass.IsPassAvailable) return;

                // Already dismissed for this week.
                var week = Services.IntakePassService.CurrentWeekKey();
                if (string.Equals(settings.IntakeNudgeDismissedWeek, week, StringComparison.Ordinal)) return;

                // One popup per launch, and the server's announcement outranks ours.
                if (_serverAnnouncementShownThisLaunch) return;

                // Never on top of something the user is in the middle of. The intake itself is
                // in the list because the nudge would otherwise fire behind a run already in
                // progress and be waiting when they came back out.
                if (App.IsSessionRunning) return;
                if (Services.Chaos.DtrhHostService.IsActive) return;
                if (Services.Quiz.IntakeHostService.IsActive) return;
                // Catch-all for the rest of the WebView2 game hosts (Bureau, Loom, the DtRH web
                // page). They are all fullscreen-ish and all sit above MainWindow, so a Topmost
                // popup would land on top of whatever the user is actually doing.
                if (ChaosWebViewHost.AnyHostActive) return;

                _serverAnnouncementShownThisLaunch = true;   // our popup now owns the slot too

                // Card art for the user's CURRENT mod, via the shared IntakeNiche mapping so the
                // face they see is the intake they would actually get. Null is a normal outcome
                // (resource missing / mod override broken) and drops the popup back to the
                // text-only layout rather than rendering an empty frame.
                var cardArt = Services.Quiz.IntakeNiche.PassCardImage();

                var popup = new AnnouncementPopup(
                    $"intake-pass-{week}",
                    LocOr("intake_nudge_title", "Your weekly intake pass is ready"),
                    // Deliberately a NEW key rather than a rewrite of "intake_nudge_body": that key
                    // already carries the old terse copy in en.json, so reusing it would keep
                    // showing the old line until someone remembered to edit the value, whereas a
                    // key that does not exist yet falls through LocOr to the English below.
                    // "intake_nudge_body" is now orphaned and can be deleted.
                    LocOr("intake_nudge_pitch",
                        "Your free Graded Intake just unlocked. It interviews you, drafts a session tuned to you - and stamps your punch card when you run it."),
                    imageUrl: null,
                    linkUrl: null,
                    theme: null,
                    onDismiss: () =>
                    {
                        // OUR record, not DismissedAnnouncementId.
                        try
                        {
                            var s = App.Settings?.Current;
                            if (s == null) return;
                            s.IntakeNudgeDismissedWeek = week;
                            App.Settings?.Save();
                        }
                        catch (Exception ex) { App.Logger?.Debug("Intake nudge dismiss: {E}", ex.Message); }
                    },
                    cardImage: cardArt,
                    actionText: LocOr("intake_nudge_action", "Start my intake"),
                    onAction: StartIntakeFromNudge,
                    dismissText: LocOr("intake_nudge_later", "Maybe later"))
                {
                    Owner = this,
                };
                popup.Show();

                App.Logger?.Information("Intake pass nudge shown for {Week} (art={HasArt})", week, cardArt != null);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("CheckIntakePassNudge failed: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// The nudge's call to action. Routed through the SAME entry point as the Exclusives
        /// button rather than calling IntakeHostService directly, so the login / AI-availability /
        /// pass gates stay in exactly one place - a second launch path is how "the popup let me in
        /// but the button didn't" bugs are born.
        /// </summary>
        private void StartIntakeFromNudge()
        {
            try
            {
                if (Application.Current?.Dispatcher == null) return;
                if (Application.Current.Dispatcher.HasShutdownStarted) return;
                BtnStartIntake_Click(this, new RoutedEventArgs());
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Intake nudge launch failed: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// <c>Loc.Get</c> returns the KEY when a string is missing, which would put
        /// "intake_nudge_action" on a button face. These keys are added to the language files in a
        /// separate pass, so fall back to the shipped English until they land.
        /// </summary>
        private static string LocOr(string key, string english)
        {
            try
            {
                var value = Loc.Get(key);
                return string.IsNullOrEmpty(value) || string.Equals(value, key, StringComparison.Ordinal)
                    ? english
                    : value;
            }
            catch { return english; }
        }

        #endregion

        #region Marquee Animation

        private void StartMarqueeAnimation()
        {
            try
            {
                // Stop existing animation
                _marqueeStoryboard?.Stop();

                var canvasWidth = SettingsTab.MarqueeCanvas.ActualWidth;
                if (canvasWidth <= 0) return;

                // Get the original message
                var message = App.Settings.Current.MarqueeMessage;
                if (string.IsNullOrWhiteSpace(message))
                {
                    message = "GOOD GIRLS CONDITION DAILY     ❤️🔒";
                }
                message = message.ToUpperInvariant();

                // Track current message for refresh detection
                _currentMarqueeMessage = message;

                // Create single segment with separator (doubled message + spacing)
                var separator = "          "; // 10 spaces between repetitions
                var singleSegment = message + separator + message + separator;

                // Measure single segment width
                var tempBlock = new TextBlock
                {
                    Text = singleSegment,
                    FontFamily = SettingsTab.MarqueeText.FontFamily,
                    FontSize = SettingsTab.MarqueeText.FontSize,
                    FontWeight = SettingsTab.MarqueeText.FontWeight
                };
                tempBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var segmentWidth = tempBlock.DesiredSize.Width;

                if (segmentWidth <= 0) return;

                // Calculate how many segments needed to fill canvas + one extra for seamless loop
                var segmentsNeeded = (int)Math.Ceiling(canvasWidth / segmentWidth) + 2;
                var fullText = string.Concat(Enumerable.Repeat(singleSegment, segmentsNeeded));
                SettingsTab.MarqueeText.Text = fullText;

                // Animation: scroll exactly one segment width, then loop back seamlessly
                // From 0 to -segmentWidth creates perfect loop since next segment is identical
                var animation = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 0,
                    To = -segmentWidth,
                    Duration = TimeSpan.FromSeconds(segmentWidth / 80), // Speed: 80 pixels per second
                    RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
                };

                _marqueeStoryboard = new System.Windows.Media.Animation.Storyboard();
                _marqueeStoryboard.Children.Add(animation);
                System.Windows.Media.Animation.Storyboard.SetTarget(animation, SettingsTab.MarqueeText);
                System.Windows.Media.Animation.Storyboard.SetTargetProperty(animation,
                    new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.X)"));

                _marqueeStoryboard.Begin();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to start marquee animation: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Updates the marquee message from server/external source.
        /// Call this method when receiving a new message from the server.
        /// </summary>
        public void UpdateMarqueeMessage(string message)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(message)) return;

                var newMessage = message.Trim().ToUpperInvariant();
                if (!newMessage.EndsWith("•") && !newMessage.EndsWith(" "))
                {
                    newMessage += " • ";
                }

                App.Settings.Current.MarqueeMessage = newMessage;
                Dispatcher.Invoke(() =>
                {
                    SettingsTab.MarqueeText.Text = newMessage;
                    StartMarqueeAnimation();
                });
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to update marquee message: {Error}", ex.Message);
            }
        }

        #endregion
    }
}
