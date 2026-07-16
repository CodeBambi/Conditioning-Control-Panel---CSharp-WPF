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

            // Show double warning describing the friction (NOT a lockout — every escape still works).
            var confirmed = WarningDialog.ShowDoubleWarning(this, "Dark Patterns",
                "For the next " + minutes + " minutes, the app fights dirty to keep you here:\n" +
                "- The close ✕ jumps to the far left and swaps with maximize, so you misclick\n" +
                "- The Exit button shrinks to a tiny target that runs from your cursor\n" +
                "- Flashes stop closing themselves; only a tiny ✕ hidden in a random corner dismisses one\n" +
                "- The panic key makes you claw through a manipulative confirm chain first\n" +
                "- The countdown may lie to you\n" +
                "\n" +
                "It is all friction, never a real cage: Alt+F4, Task Manager, the panic chain and\n" +
                "typing \"let me out\" on the timer all still get you out whenever you truly mean it.");

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
                if (App.Settings?.Current?.ChaosWebGameEnabled == true
                    && !Services.Chaos.DtrhHostService.BootFailedThisSession)
                {
                    Services.Chaos.DtrhHostService.Launch();
                    return;
                }
                if (App.Chaos == null || App.Chaos.IsRunning) return;
                // Happy path run 1: the Dollhouse stays shut until the first descent is done.
                // FALL IN drops straight into the scripted naked run instead.
                if (Services.Chaos.ChaosMeta.State.RunsCompleted == 0)
                {
                    App.Chaos.StartRun(Services.Chaos.ChaosHappyPath.BuildFirstRunConfig());
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

                // Trend summary at top — show latest archetype + trend per category that has history
                var categories = history.Select(h => h.Category).Distinct();
                foreach (var cat in categories)
                {
                    var trend = QuizService.GetScoreTrend(history, cat);
                    if (trend == null) continue;

                    // Extract archetype from latest profile text
                    var latestEntry = history.FirstOrDefault(h => h.Category == cat);
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
                    var catDisplay = latestEntry != null && !string.IsNullOrEmpty(latestEntry.CategoryName)
                        ? latestEntry.CategoryName : cat.ToString();
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
                    var catName = !string.IsNullOrEmpty(entry.CategoryName) ? entry.CategoryName : entry.Category.ToString();
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
                    // NOTE (Dark Patterns): unlike the old hard lockout, we deliberately do NOT enable
                    // GlobalKeyboardHook.SuppressSystemKeys and do NOT gray out the Strict Lock / No
                    // Panic toggles. Alt+F4, the Windows key and Task Manager all stay live, and the
                    // panic key still works (through the anti-panic confirm chain). All the friction is
                    // the in-app chrome trickery below — never an OS-level lockout.

                    // Apply the window-chrome dark patterns (X to the left, close/maximize swapped,
                    // ludicrously tiny + fleeing Exit, Save/Exit positions swapped).
                    ApplyDarkPatternChrome();

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
                    // Undo the chrome trickery — put the caption buttons, Exit and Save back exactly.
                    RestoreDarkPatternChrome();

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
                if (LockdownTab.TxtLockdownTimer == null) return;

                // Dark pattern: the DISPLAYED countdown occasionally lies — it stalls on the last
                // value or even ticks a second or two upward, so watching the clock feels futile. The
                // REAL LockdownService timer is untouched, so the mode still ends exactly on schedule
                // and every genuine escape ("let me out", Alt+F4, the anti-panic chain) still works.
                var shown = remaining;
                int roll = _darkPatternRng.Next(4);
                if (roll == 0)
                    shown = _lastShownRemaining; // stall on the previous value
                else if (roll == 1)
                    shown = remaining + TimeSpan.FromSeconds(_darkPatternRng.Next(1, 4)); // creep upward
                _lastShownRemaining = shown;

                if (shown.TotalHours >= 1)
                    LockdownTab.TxtLockdownTimer.Text = shown.ToString(@"h\:mm\:ss");
                else
                    LockdownTab.TxtLockdownTimer.Text = shown.ToString(@"mm\:ss");
            });
        }

        // ---- Dark Patterns chrome trickery ----

        private static readonly Random _darkPatternRng = new Random();
        private TimeSpan _lastShownRemaining = TimeSpan.Zero;

        // Saved original chrome state, restored verbatim on deactivate.
        private double _preExitWidth, _preExitHeight, _preExitFontSize;
        private Thickness _preExitMargin;
        private int _preExitColumn, _preSaveColumn;
        private MouseEventHandler? _fleeingExitHandler;
        private TranslateTransform? _exitFleeTransform;
        private Transform? _preExitTransform;
        private int _exitFleeRemaining;

        /// <summary>
        /// Applies the window-chrome dark patterns while the mode is active: moves the ✕ close button
        /// to the far left of the title bar (which, as a bonus, drops Maximize into the habitual
        /// close position so the muscle-memory click maximizes instead), shrinks the bottom Exit
        /// button to an absurdly tiny target that flees the cursor, and swaps the Save / Exit
        /// positions. All reversible via <see cref="RestoreDarkPatternChrome"/>. Purely cosmetic —
        /// every button still works; it's just obnoxious to hit.
        /// </summary>
        private void ApplyDarkPatternChrome()
        {
            try
            {
                // 1) Move ✕ to the far left. Reparent BtnClose out of the right caption panel into the
                //    left title panel at index 0. The rightmost caption button is now Maximize, sitting
                //    exactly where the eye/muscle-memory expects Close — instant misclick bait.
                if (BtnClose != null && CaptionButtonsPanel != null && TitleLeftPanel != null
                    && CaptionButtonsPanel.Children.Contains(BtnClose))
                {
                    CaptionButtonsPanel.Children.Remove(BtnClose);
                    TitleLeftPanel.Children.Insert(0, BtnClose);
                }

                // 2) Nerf the Exit button — smaller than normal, but still a real, clickable target
                //    (it dodges rather than shrinking into oblivion).
                if (BtnExitBottom != null)
                {
                    _preExitWidth = BtnExitBottom.Width;
                    _preExitHeight = BtnExitBottom.Height;
                    _preExitFontSize = BtnExitBottom.FontSize;
                    _preExitMargin = BtnExitBottom.Margin;
                    _preExitTransform = BtnExitBottom.RenderTransform;

                    BtnExitBottom.Height = 30;
                    BtnExitBottom.FontSize = 11;
                    BtnExitBottom.Margin = new Thickness(0);
                    BtnExitBottom.VerticalAlignment = VerticalAlignment.Center;

                    // 3) Make it flee a FINITE number of times (2-5), then give up and settle back home
                    //    so it's actually clickable. Each time the cursor catches it, it jumps once and
                    //    decrements; when the budget hits zero it snaps to its layout spot and stays.
                    _exitFleeRemaining = _darkPatternRng.Next(2, 6); // 2..5 inclusive
                    _exitFleeTransform = new TranslateTransform(0, 0);
                    BtnExitBottom.RenderTransform = _exitFleeTransform;
                    _fleeingExitHandler = (_, _) =>
                    {
                        if (_exitFleeTransform == null) return;
                        if (_exitFleeRemaining <= 0)
                        {
                            // Given up — settle back to its real position so the click can land.
                            _exitFleeTransform.X = 0;
                            _exitFleeTransform.Y = 0;
                            return;
                        }
                        _exitFleeRemaining--;
                        _exitFleeTransform.X = _darkPatternRng.Next(-120, 121);
                        _exitFleeTransform.Y = _darkPatternRng.Next(-24, 25);
                    };
                    BtnExitBottom.MouseEnter += _fleeingExitHandler;
                }

                // 4) Swap Save <-> Exit positions.
                if (BtnSaveBottom != null && BtnExitBottom != null)
                {
                    _preSaveColumn = Grid.GetColumn(BtnSaveBottom);
                    _preExitColumn = Grid.GetColumn(BtnExitBottom);
                    Grid.SetColumn(BtnSaveBottom, _preExitColumn);
                    Grid.SetColumn(BtnExitBottom, _preSaveColumn);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to apply Dark Patterns chrome");
            }
        }

        private void RestoreDarkPatternChrome()
        {
            try
            {
                // 1) Put ✕ back on the right caption panel (restore original order: last child).
                if (BtnClose != null && CaptionButtonsPanel != null && TitleLeftPanel != null
                    && TitleLeftPanel.Children.Contains(BtnClose))
                {
                    TitleLeftPanel.Children.Remove(BtnClose);
                    CaptionButtonsPanel.Children.Add(BtnClose);
                }

                // 2/3) Restore the Exit button size + stop it fleeing.
                if (BtnExitBottom != null)
                {
                    if (_fleeingExitHandler != null)
                    {
                        BtnExitBottom.MouseEnter -= _fleeingExitHandler;
                        _fleeingExitHandler = null;
                    }
                    _exitFleeTransform = null;
                    BtnExitBottom.RenderTransform = _preExitTransform ?? Transform.Identity;
                    BtnExitBottom.Width = _preExitWidth;
                    BtnExitBottom.Height = _preExitHeight;
                    BtnExitBottom.FontSize = _preExitFontSize;
                    BtnExitBottom.Margin = _preExitMargin;
                }

                // 4) Swap Save <-> Exit columns back.
                if (BtnSaveBottom != null && BtnExitBottom != null)
                {
                    Grid.SetColumn(BtnSaveBottom, _preSaveColumn);
                    Grid.SetColumn(BtnExitBottom, _preExitColumn);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to restore Dark Patterns chrome");
            }
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
