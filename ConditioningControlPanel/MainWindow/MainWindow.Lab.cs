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
using ConditioningControlPanel.Models.Quiz;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    // Lab tab: AI lab session controls and state.
    public partial class MainWindow
    {
        #region Lab

        private void InitializeLockdown()
        {
            if (App.Lockdown == null) return;

            App.Lockdown.LockdownActivated += OnLockdownActivated;
            App.Lockdown.LockdownDeactivated += OnLockdownDeactivated;
            App.Lockdown.CountdownTick += OnLockdownTick;
        }

        internal void BtnActivateLockdown_Click(object sender, RoutedEventArgs e)
        {
            if (App.Lockdown == null) return;

            // Get duration from combo box
            var selectedItem = LockdownTab.CmbLockdownDuration.SelectedItem as ComboBoxItem;
            if (selectedItem?.Tag is not string minutesStr || !int.TryParse(minutesStr, out var minutes))
                return;

            var duration = TimeSpan.FromMinutes(minutes);

            // Show double warning with clear consequences
            var confirmed = WarningDialog.ShowDoubleWarning(this, "Lockdown Mode",
                "- You will be LOCKED IN for " + minutes + " minutes\n" +
                "- Strict Lock will be FORCED ON\n" +
                "- Panic Key will be DISABLED\n" +
                "- Alt+F4, Alt+Tab, Windows key, and Escape will be BLOCKED\n" +
                "- You CANNOT close or minimize the application\n" +
                "- The only escape is waiting for the timer to expire\n" +
                "  (or Ctrl+Alt+Del → Task Manager as a safety valve)");

            if (!confirmed) return;

            App.Lockdown.Activate(duration);
        }

        internal void BtnStartQuiz_Click(object sender, RoutedEventArgs e)
        {
            // Prevent opening multiple quiz windows — focus existing one instead
            var existingQuiz = Application.Current.Windows.OfType<QuizWindow>().FirstOrDefault();
            if (existingQuiz != null)
            {
                existingQuiz.Activate();
                existingQuiz.Focus();
                return;
            }

            if (App.Ai == null || !App.Ai.IsAvailable)
            {
                MessageBox.Show(Loc.Get("msg_you_need_to_be_logged_in_to_use_the_ai_quiz"), "Login Required",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var fullscreen = LabTab.ChkQuizFullscreen?.IsChecked == true;
            var playDrone = LabTab.ChkQuizDrone?.IsChecked == true;
            var quizWindow = new QuizWindow(fullscreen, playDrone);
            quizWindow.Closed += (s, args) => RefreshPastQuizzes();
            quizWindow.Show();
        }

        /// <summary>
        /// Lab → Chaos Mode hero card. Opens the setup/lobby window where the user
        /// configures the run; BEGIN CHAOS there persists settings and launches via
        /// <see cref="App.Chaos"/> (which owns the countdown, HUD and loop).
        /// Modeless on purpose: ShowDialog would disable every other app window,
        /// including the loadout sidebar that opens beside the Warren.
        /// </summary>
        internal void BtnStartChaos_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // DtRH browser game (default ON since M6): the whole experience lives in the web
                // page — hub, run and all. The legacy WPF path below stays for the Lab toggle and
                // as the automatic fallback when the page reported a WebGL boot-error this session.
                bool webPath = App.Settings?.Current?.ChaosWebGameEnabled == true
                               && !Services.Chaos.DtrhHostService.BootFailedThisSession;

                // Already down the hole? Just focus it — never re-pick a save mid-session.
                if (webPath && Services.Chaos.DtrhHostService.IsActive)
                {
                    Services.Chaos.DtrhHostService.Launch();
                    return;
                }
                if (!webPath && (App.Chaos == null || App.Chaos.IsRunning)) return;

                // Save slots: choose which of the three local saves to descend into, right before
                // the hole opens. Cancel backs out; the pick becomes the live slot for this session.
                var slot = ChaosSlotPickerWindow.Pick(this);
                if (slot == null) return;
                Services.Chaos.ChaosMeta.SwitchSlot(slot.Value);

                if (webPath)
                {
                    Services.Chaos.DtrhHostService.Launch();
                    return;
                }
                // Happy path run 1: the Dollhouse stays shut until the first descent is done.
                // FALL IN drops straight into the scripted naked run instead.
                if (Services.Chaos.ChaosMeta.State.RunsCompleted == 0)
                {
                    App.Chaos!.StartRun(Services.Chaos.ChaosHappyPath.BuildFirstRunConfig());
                    return;
                }
                if (ChaosHubWindow.Current != null) { ChaosHubWindow.Current.Activate(); return; }
                var hub = new ChaosHubWindow { Owner = this };
                hub.Show();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "BtnStartChaos_Click failed");
                MessageBox.Show("Couldn't start Down the Rabbit Hole:\n\n" + ex.Message, "Down the Rabbit Hole",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Quick Start: launch a Chaos run with the saved settings, bypassing the modal hub.
        /// Mirrors what BEGIN CHAOS does after SaveToSettings (StartRun reads ChaosRunConfig.FromSettings),
        /// just without the dialog.
        /// </summary>
        internal void BtnQuickStartChaos_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // DtRH browser game: same surface as the hero card — see BtnStartChaos_Click.
                // Quick Start skips the save picker by design (that's the "quick" part) and reuses
                // the last-chosen slot, already live in ChaosMeta.State.
                if (App.Settings?.Current?.ChaosWebGameEnabled == true
                    && !Services.Chaos.DtrhHostService.BootFailedThisSession)
                {
                    Services.Chaos.DtrhHostService.Launch();
                    return;
                }
                if (App.Chaos == null || App.Chaos.IsRunning) return;
                // Happy path run 1: the quick start drops into the same scripted naked run.
                if (Services.Chaos.ChaosMeta.State.RunsCompleted == 0)
                {
                    App.Chaos.StartRun(Services.Chaos.ChaosHappyPath.BuildFirstRunConfig());
                    return;
                }
                App.Chaos.StartRun();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "BtnQuickStartChaos_Click failed");
                MessageBox.Show("Couldn't start Down the Rabbit Hole:\n\n" + ex.Message, "Down the Rabbit Hole",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void RefreshPastQuizzes()
        {
            try
            {
                var history = QuizService.LoadHistory();
                LabTab.PastQuizzesList.Children.Clear();

                if (history.Count == 0)
                {
                    LabTab.TxtPastQuizzesHeader.Visibility = Visibility.Collapsed;
                    LabTab.PastQuizzesPanel.Visibility = Visibility.Collapsed;
                    return;
                }

                LabTab.TxtPastQuizzesHeader.Visibility = Visibility.Visible;
                LabTab.PastQuizzesPanel.Visibility = Visibility.Visible;

                // Trend summary at top — show latest archetype + trend per category that has history.
                // Group by TrendKey (CategoryId string), not the enum: custom categories all
                // collapse to Category=Sissy and would clobber the built-in Sissy stat (#518/#521).
                var categories = history.Select(QuizService.TrendKey)
                    .Distinct(StringComparer.OrdinalIgnoreCase);
                foreach (var cat in categories)
                {
                    var trend = QuizService.GetScoreTrend(history, cat);
                    if (trend == null) continue;

                    // Extract archetype from latest profile text
                    var latestEntry = history.FirstOrDefault(h =>
                        string.Equals(QuizService.TrendKey(h), cat, StringComparison.OrdinalIgnoreCase));
                    var archetype = "";
                    if (latestEntry != null)
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(latestEntry.ProfileText, @"You are a (.+?)\.");
                        if (match.Success) archetype = match.Groups[1].Value;
                    }

                    var arrow = trend.Direction switch
                    {
                        TrendDirection.Up => "\u2191",
                        TrendDirection.Down => "\u2193",
                        TrendDirection.Flat => "\u2192",
                        _ => ""
                    };
                    var catDisplay = latestEntry != null ? QuizService.DisplayName(latestEntry) : cat;
                    var trendLabel = trend.Direction == TrendDirection.FirstQuiz
                        ? $"{catDisplay}: {trend.LatestPercent}%"
                        : $"{catDisplay}: {trend.LatestPercent}% {arrow}{Math.Abs(trend.DeltaPercent)}%";
                    if (!string.IsNullOrEmpty(archetype))
                        trendLabel += $" · {archetype}";

                    var trendRow = new TextBlock
                    {
                        Text = trendLabel,
                        Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4)),
                        FontSize = 11,
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(8, 3, 8, 3)
                    };
                    LabTab.PastQuizzesList.Children.Add(trendRow);
                }

                foreach (var entry in history)
                {
                    var pct = entry.MaxScore > 0 ? (int)Math.Round((double)entry.TotalScore / entry.MaxScore * 100) : 0;
                    var catName = QuizService.DisplayName(entry);
                    var label = $"{entry.TakenAt:MMM d}  ·  {catName}  ·  {entry.TotalScore}/{entry.MaxScore} ({pct}%)";

                    var row = new Border
                    {
                        Cursor = System.Windows.Input.Cursors.Hand,
                        Padding = new Thickness(8, 5, 8, 5),
                        Background = System.Windows.Media.Brushes.Transparent
                    };

                    var txt = new TextBlock
                    {
                        Text = label,
                        Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xB8)),
                        FontSize = 11.5
                    };
                    row.Child = txt;

                    var captured = entry;
                    row.MouseLeftButtonDown += (s, args) =>
                    {
                        // Close any existing report window before opening a new one
                        foreach (var w in Application.Current.Windows.OfType<QuizReportWindow>().ToList())
                            w.Close();
                        new QuizReportWindow(captured) { Owner = this }.Show();
                    };
                    row.MouseEnter += (s, args) =>
                    {
                        if (s is Border b) b.Background = new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF));
                    };
                    row.MouseLeave += (s, args) =>
                    {
                        if (s is Border b) b.Background = System.Windows.Media.Brushes.Transparent;
                    };

                    LabTab.PastQuizzesList.Children.Add(row);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "MainWindow: Failed to refresh past quizzes");
            }
        }

        // ============ POP QUIZ HANDLERS ============

        internal void ChkPopQuizEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (App.Settings?.Current == null) return;
            App.Settings.Current.PopQuizEnabled = LabTab.ChkPopQuizEnabled.IsChecked == true;
        }

        internal void SliderPopQuizFrequency_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (App.Settings?.Current == null || LabTab.TxtPopQuizFrequency == null) return;
            var val = (int)Math.Round(e.NewValue);
            App.Settings.Current.PopQuizFrequency = val;
            LabTab.TxtPopQuizFrequency.Text = $"{val}/session hr";
        }

        internal void BtnTestPopQuiz_Click(object sender, RoutedEventArgs e)
        {
            App.PopQuiz?.TestPopQuiz();
        }

        // ============ WALLPAPER OVERRIDE HANDLERS ============

        internal void ChkWallpaperEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (App.Settings?.Current == null || App.Wallpaper == null) return;

            var enabled = LabTab.ChkWallpaperEnabled.IsChecked == true;
            if (enabled)
            {
                if (!App.Wallpaper.Activate())
                {
                    // No images found — uncheck and notify
                    LabTab.ChkWallpaperEnabled.IsChecked = false;
                    App.Settings.Current.WallpaperEnabled = false;
                    MessageBox.Show(Loc.Get("msg_no_wallpaper_images"), "Wallpaper Override",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                LabTab.TxtCurrentWallpaper.Text = App.Wallpaper.CurrentFilename;
                LabTab.TxtCurrentWallpaper.Visibility = Visibility.Visible;
                LabTab.BtnShuffleWallpaper.Visibility = Visibility.Visible;
            }
            else
            {
                App.Wallpaper.Deactivate();
                LabTab.TxtCurrentWallpaper.Visibility = Visibility.Collapsed;
                LabTab.BtnShuffleWallpaper.Visibility = Visibility.Collapsed;
            }
            App.Settings.Current.WallpaperEnabled = enabled;
        }

        internal void BtnShuffleWallpaper_Click(object sender, RoutedEventArgs e)
        {
            if (App.Wallpaper == null) return;
            App.Wallpaper.Shuffle();
            LabTab.TxtCurrentWallpaper.Text = App.Wallpaper.CurrentFilename;
        }

        private void OnLockdownActivated()
        {
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    // Enable system key suppression on the keyboard hook (the setter also
                    // installs the hook if panic key / keyword triggers never started it)
                    if (_keyboardHook != null)
                    {
                        _keyboardHook.SuppressSystemKeys = true;
                        if (!_keyboardHook.IsInstalled)
                            App.Logger?.Warning("Lockdown: keyboard hook could not be installed - Esc/Win/Alt-Tab will NOT be blocked this session");
                    }

                    // Gray out strict lock and panic key toggles
                    if (SettingsTab.ChkStrictLock != null)
                    {
                        SettingsTab.ChkStrictLock.IsEnabled = false;
                        SettingsTab.ChkStrictLock.Opacity = 0.4;
                        SettingsTab.ChkStrictLock.ToolTip = Loc.Get("tooltip_you_are_in_lockdown_mode_there_is_no_escape");
                    }
                    if (SettingsTab.ChkNoPanic != null)
                    {
                        SettingsTab.ChkNoPanic.IsEnabled = false;
                        SettingsTab.ChkNoPanic.Opacity = 0.4;
                        SettingsTab.ChkNoPanic.ToolTip = Loc.Get("tooltip_you_are_in_lockdown_mode_there_is_no_escape");
                    }

                    // Swap UI panels
                    if (LockdownTab.LockdownSetupPanel != null) LockdownTab.LockdownSetupPanel.Visibility = Visibility.Collapsed;
                    if (LockdownTab.LockdownActivePanel != null) LockdownTab.LockdownActivePanel.Visibility = Visibility.Visible;

                    // Reset secret exit state
                    _lockdownTimerClickCount = 0;
                    if (LockdownTab.TxtLockdownExit != null)
                    {
                        LockdownTab.TxtLockdownExit.Visibility = Visibility.Collapsed;
                        LockdownTab.TxtLockdownExit.Text = "";
                    }

                    // Apply blood-red theme
                    ApplyLockdownTheme();

                    // Play activation flash animation
                    PlayLockdownActivationAnimation();

                    App.Logger?.Information("Lockdown UI activated");
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "Error activating lockdown UI");
                }
            });
        }

        private void OnLockdownDeactivated()
        {
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    // Disable system key suppression. Lockdown may have installed the hook
                    // itself; drop it again unless another feature still needs it (mirrors
                    // the startup install condition). LockdownService restores the user's
                    // real PanicKeyEnabled before raising this event, so the check is safe.
                    if (_keyboardHook != null)
                    {
                        _keyboardHook.SuppressSystemKeys = false;
                        if (App.Settings.Current.PanicKeyEnabled != true &&
                            App.Settings.Current.KeywordTriggersEnabled != true)
                            _keyboardHook.Stop();
                    }

                    // Restore strict lock and panic key toggles
                    if (SettingsTab.ChkStrictLock != null)
                    {
                        SettingsTab.ChkStrictLock.IsEnabled = true;
                        SettingsTab.ChkStrictLock.Opacity = 1.0;
                        SettingsTab.ChkStrictLock.ToolTip = null;
                    }
                    if (SettingsTab.ChkNoPanic != null)
                    {
                        SettingsTab.ChkNoPanic.IsEnabled = true;
                        SettingsTab.ChkNoPanic.Opacity = 1.0;
                        SettingsTab.ChkNoPanic.ToolTip = null;
                    }

                    // Swap UI panels back
                    if (LockdownTab.LockdownSetupPanel != null) LockdownTab.LockdownSetupPanel.Visibility = Visibility.Visible;
                    if (LockdownTab.LockdownActivePanel != null) LockdownTab.LockdownActivePanel.Visibility = Visibility.Collapsed;

                    // Hide secret exit
                    if (LockdownTab.TxtLockdownExit != null)
                        LockdownTab.TxtLockdownExit.Visibility = Visibility.Collapsed;

                    // Restore normal theme
                    RestoreLockdownTheme();

                    App.Logger?.Information("Lockdown UI deactivated");
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "Error deactivating lockdown UI");
                }
            });
        }

        private void OnLockdownTick(TimeSpan remaining)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (LockdownTab.TxtLockdownTimer != null)
                {
                    if (remaining.TotalHours >= 1)
                        LockdownTab.TxtLockdownTimer.Text = remaining.ToString(@"h\:mm\:ss");
                    else
                        LockdownTab.TxtLockdownTimer.Text = remaining.ToString(@"mm\:ss");
                }
            });
        }

        internal void TxtLockdownTimer_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var now = DateTime.Now;

            // Reset click count if more than 1 second since last click
            if ((now - _lockdownTimerLastClick).TotalMilliseconds > 1000)
                _lockdownTimerClickCount = 0;

            _lockdownTimerLastClick = now;
            _lockdownTimerClickCount++;

            if (_lockdownTimerClickCount >= 5 && LockdownTab.TxtLockdownExit != null)
            {
                LockdownTab.TxtLockdownExit.Visibility = Visibility.Visible;
                LockdownTab.TxtLockdownExit.Focus();
                _lockdownTimerClickCount = 0;
            }
        }

        internal void TxtLockdownExit_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;

            if (LockdownTab.TxtLockdownExit != null)
            {
                var phrase = LockdownTab.TxtLockdownExit.Text;
                var success = App.Lockdown?.TryExitWithPhrase(phrase) ?? false;

                if (!success)
                {
                    // Wrong phrase — clear and hide
                    LockdownTab.TxtLockdownExit.Text = "";
                    LockdownTab.TxtLockdownExit.Visibility = Visibility.Collapsed;
                }
            }
        }

        // --- Lockdown Theme ---

        private static readonly Color LockdownCrimson = (Color)ColorConverter.ConvertFromString("#DC143C");
        private static readonly Color LockdownDarkRed = (Color)ColorConverter.ConvertFromString("#8B0000");
        private static readonly Color LockdownPanelBg = (Color)ColorConverter.ConvertFromString("#1A0A0A");
        private static readonly Color LockdownWindowBg = (Color)ColorConverter.ConvertFromString("#100505");

        private void ApplyLockdownTheme()
        {
            try
            {
                // Save current values for restoration
                _preLockdownWindowBg = Background;
                _preLockdownTitleBarBg = TitleBarBorder?.Background;

                // Window background
                Background = new SolidColorBrush(LockdownWindowBg);

                // Title bar
                if (TitleBarBorder != null)
                    TitleBarBorder.Background = new SolidColorBrush(LockdownDarkRed);

                // Player title and glow
                if (TxtPlayerTitle != null)
                {
                    TxtPlayerTitle.Foreground = new SolidColorBrush(LockdownCrimson);
                    if (TxtPlayerTitle.Effect is DropShadowEffect glow)
                        glow.Color = LockdownCrimson;
                }

                // Header version
                if (TxtHeaderVersion != null)
                    TxtHeaderVersion.Foreground = new SolidColorBrush(LockdownCrimson);

                // Level label
                if (TxtLevelLabel != null)
                    TxtLevelLabel.Foreground = new SolidColorBrush(LockdownCrimson);

                // XP bar
                if (XPBar != null)
                    XPBar.Background = new SolidColorBrush(LockdownCrimson);

                // Banner texts
                if (TxtBannerPrimary != null)
                    TxtBannerPrimary.Foreground = new SolidColorBrush(LockdownCrimson);
                if (TxtBannerSecondary != null)
                    TxtBannerSecondary.Foreground = new SolidColorBrush(LockdownCrimson);
                if (TxtBannerTertiary != null)
                    TxtBannerTertiary.Foreground = new SolidColorBrush(LockdownCrimson);

                // Lockdown card border → red glow
                if (LockdownTab.LockdownCardBorder != null)
                {
                    LockdownTab.LockdownCardBorder.BorderBrush = new SolidColorBrush(LockdownCrimson);
                    LockdownTab.LockdownCardBorder.Background = new SolidColorBrush(LockdownPanelBg);
                }

                // Update Application-level resource brushes (affects styled controls)
                var res = Application.Current.Resources;
                res["PinkBrush"] = new SolidColorBrush(LockdownCrimson);
                res["DarkPinkBrush"] = new SolidColorBrush(LockdownDarkRed);
                res["TransparentPinkBrush"] = new SolidColorBrush(Color.FromArgb(0x30, 0xDC, 0x14, 0x3C));
                res["PinkButtonHoveredBrush"] = new SolidColorBrush(LockdownCrimson);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to apply lockdown theme");
            }
        }

        private void RestoreLockdownTheme()
        {
            try
            {
                // Restore saved values
                if (_preLockdownWindowBg != null)
                    Background = _preLockdownWindowBg;
                if (_preLockdownTitleBarBg != null && TitleBarBorder != null)
                    TitleBarBorder.Background = _preLockdownTitleBarBg;

                // Restore lockdown card to normal gradient border using mod colors
                if (LockdownTab.LockdownCardBorder != null)
                {
                    var accentHex = App.Mods?.GetAccentColorHex() ?? "#FF69B4";
                    var secondaryHex = App.Mods?.GetSecondaryColorHex() ?? "#9B59B6";
                    var accentColor = (Color)ColorConverter.ConvertFromString(accentHex);
                    var secondaryColor = (Color)ColorConverter.ConvertFromString(secondaryHex);

                    var borderBrush = new LinearGradientBrush
                    {
                        StartPoint = new System.Windows.Point(0, 0),
                        EndPoint = new System.Windows.Point(1, 1)
                    };
                    borderBrush.GradientStops.Add(new GradientStop(accentColor, 0));
                    borderBrush.GradientStops.Add(new GradientStop(secondaryColor, 1));
                    LockdownTab.LockdownCardBorder.BorderBrush = borderBrush;

                    var bgBrush = new LinearGradientBrush
                    {
                        StartPoint = new System.Windows.Point(0, 0),
                        EndPoint = new System.Windows.Point(1, 1)
                    };
                    bgBrush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#1A1A32"), 0));
                    bgBrush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#201A38"), 1));
                    LockdownTab.LockdownCardBorder.Background = bgBrush;
                }

                // Re-apply mode-aware theme colors (restores all resource brushes + named elements)
                RefreshThemeAwareElements();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to restore lockdown theme");
            }
        }

        private void PlayLockdownActivationAnimation()
        {
            try
            {
                // Create a full-screen red flash overlay
                var flash = new System.Windows.Controls.Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(180, 220, 20, 60)), // semi-transparent crimson
                    IsHitTestVisible = false
                };

                RootGrid.Children.Add(flash);

                // Fade out over 600ms
                var fadeOut = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 1.0,
                    To = 0.0,
                    Duration = TimeSpan.FromMilliseconds(600),
                    EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                };

                fadeOut.Completed += (_, _) =>
                {
                    try { RootGrid.Children.Remove(flash); } catch { }
                };

                flash.BeginAnimation(OpacityProperty, fadeOut);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to play lockdown animation");
            }
        }

        #endregion
    }
}
