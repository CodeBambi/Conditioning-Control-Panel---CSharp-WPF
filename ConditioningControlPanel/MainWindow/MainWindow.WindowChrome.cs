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
    // Window chrome: custom title bar and top-level window lifecycle events.
    public partial class MainWindow
    {
        #region Custom Title Bar

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                // Double-click to maximize/restore
                BtnMaximize_Click(sender, e);
            }
            else
            {
                // Drag window
                if (WindowState == WindowState.Maximized)
                {
                    // Restore before dragging from maximized
                    var point = PointToScreen(e.GetPosition(this));
                    WindowState = WindowState.Normal;
                    Left = point.X - (Width / 2);
                    Top = point.Y - 15;
                }
                DragMove();
            }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            try { App.Bark?.NotifyUiAction("minimize"); } catch { }
            // Hide avatar tube BEFORE minimizing to prevent visual artifacts
            HideAvatarTube();
            WindowState = WindowState.Minimized;
        }

        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
                BtnMaximize.Content = "☐";

                // Re-attach avatar if it was attached before maximizing
                if (_avatarWasAttachedBeforeMaximize && _avatarTubeWindow != null && _avatarTubeWindow.IsDetached)
                {
                    _avatarTubeWindow.Attach();
                    _avatarWasAttachedBeforeMaximize = false;
                }
            }
            else
            {
                // Detach avatar before maximizing (it would be in wrong position otherwise)
                if (_avatarTubeWindow != null && !_avatarTubeWindow.IsDetached)
                {
                    _avatarWasAttachedBeforeMaximize = true;
                    _avatarTubeWindow.Detach();
                }

                WindowState = WindowState.Maximized;
                BtnMaximize.Content = "❐";
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            try { App.Bark?.NotifyUiAction("close"); } catch { }
            Close();
        }

        #endregion

        #region Window Events

        /// <summary>
        /// Stops an in-progress preset session so SessionEngine.RestoreSettings() runs BEFORE we
        /// persist settings on exit. Without this, quitting mid-session saves the session's
        /// overridden pools (every user phrase disabled + session phrases injected), so the
        /// subliminal/bouncing-text message sets look wiped on the next launch. StopSession()
        /// is a no-op when no session is running, so this is always safe to call.
        /// </summary>
        private void EnsureSessionRestoredForExit()
        {
            try
            {
                if (_sessionEngine != null && _sessionEngine.IsRunning)
                    _sessionEngine.StopSession(completed: false);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to restore session settings on exit");
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Lockdown mode: block all close attempts
            if (App.Lockdown?.IsActive == true)
            {
                e.Cancel = true;
                return;
            }

            // Closing (real exit OR minimize-to-tray) always ends a Chaos run so it
            // can't keep spawning bubbles / pinning the app alive behind the scenes.
            try { App.Chaos?.ForceShutdown(); } catch { }

            // Only allow actual close if exit was explicitly requested
            if (_exitRequested)
            {
                // Kill all audio and effects first - ensures clean exit
                App.KillAllAudio();

                // Sync conditioning time to server before exit
                try
                {
                    _ = SyncConditioningTimeToServerAsync();
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "Failed to sync conditioning time on exit");
                }

                // Restore any active session's settings before persisting (else the overridden
                // pools get saved and the message sets look wiped next launch).
                EnsureSessionRestoredForExit();

                // Actually closing - clean up
                SaveSettings();

                // Stop ALL timers to prevent post-close dispatcher crashes
                _schedulerTimer?.Stop();
                _rampTimer?.Stop();
                _packPreviewTimer?.Stop();
                _remoteNotificationTimer?.Stop();
                _remoteSessionInfoTimer?.Stop();
                _bannerRotationTimer?.Stop();
                _marqueeRefreshTimer?.Stop();
                _statPillUpdateTimer?.Stop();
                _conditioningTimeTimer?.Stop();
                _conditioningTimeSyncTimer?.Stop();

                // Unsubscribe service events to allow GC of this window
                UnsubscribeWebcamDebug();
                if (_onPillStateChanged != null && App.Webcam != null)
                {
                    App.Webcam.OnTrackingStateChanged -= _onPillStateChanged;
                    _onPillStateChanged = null;
                }
                if (_onMicListeningChanged != null && App.Speech != null)
                {
                    App.Speech.ListeningChanged -= _onMicListeningChanged;
                    _onMicListeningChanged = null;
                }
                if (_onWakeListeningChanged != null && App.WakeWord != null)
                {
                    App.WakeWord.ListeningChanged -= _onWakeListeningChanged;
                    _onWakeListeningChanged = null;
                }
                if (_onRapidBlinkRecal != null && App.Webcam != null)
                {
                    App.Webcam.OnBlink -= _onRapidBlinkRecal;
                    _onRapidBlinkRecal = null;
                }
                if (App.Progression != null)
                {
                    App.Progression.XPChanged -= OnXPChanged;
                    App.Progression.LevelUp -= OnLevelUp;
                }
                if (App.Companion != null)
                {
                    App.Companion.XPAwarded -= OnCompanionXPAwarded;
                    App.Companion.CompanionLevelUp -= OnCompanionLevelUp;
                    App.Companion.XPDrained -= OnCompanionXPDrained;
                    App.Companion.CompanionSwitched -= OnCompanionSwitched;
                }
                if (App.ProfileSync != null)
                {
                    App.ProfileSync.ProfileLoaded -= OnProfileLoaded;
                    App.ProfileSync.SyncHealthChanged -= OnSyncHealthChanged;
                }
                if (App.Achievements != null)
                {
                    App.Achievements.AchievementUnlocked -= OnAchievementUnlockedInMainWindow;
                }
                if (App.Quests != null)
                {
                    App.Quests.QuestCompleted -= OnQuestCompleted;
                    App.Quests.QuestProgressChanged -= OnQuestProgressChanged;
                }
                if (App.SkillTree != null)
                {
                    App.SkillTree.PinkRushStarted -= OnPinkRushStarted;
                    App.SkillTree.PinkRushEnded -= OnPinkRushEnded;
                }
                if (App.Roadmap != null)
                {
                    App.Roadmap.StepCompleted -= OnRoadmapStepCompleted;
                    App.Roadmap.TrackUnlocked -= OnRoadmapTrackUnlocked;
                }
                if (App.EnhancementLibrary != null)
                {
                    App.EnhancementLibrary.LibraryChanged -= OnDeeperLibraryChanged;
                }

                _keyboardHook?.Dispose();
                _trayIcon?.Dispose();
                _browser?.Dispose();
                _avatarTubeWindow?.CloseSafe();

                // Close any quiz windows (topmost/fullscreen, would keep app alive)
                try
                {
                    foreach (var quiz in Application.Current.Windows.OfType<PopQuizWindow>().ToList())
                        quiz.Close();
                    foreach (var quiz in Application.Current.Windows.OfType<QuizWindow>().ToList())
                        quiz.Close();
                }
                catch { }

                // Close any open Deeper editors / players / overlays. WebView2,
                // LibVLCSharp media players, and the on-rails tutorial overlay
                // each pin the dispatcher (native handles + animations + event
                // subscriptions), so the owner-cascade alone isn't enough to
                // let the process exit. Force-close them here.
                try
                {
                    foreach (var w in Application.Current.Windows
                                          .OfType<Views.Deeper.DeeperEditorWindow>().ToList())
                        w.ForceClose();
                    foreach (var w in Application.Current.Windows
                                          .OfType<Views.Deeper.EnhancementPlayerWindow>().ToList())
                        w.Close();
                    foreach (var w in Application.Current.Windows
                                          .OfType<TutorialOverlay>().ToList())
                        w.Close();
                }
                catch { }

                // Stop and dispose session engine (closes corner GIF window)
                try
                {
                    _sessionEngine?.Dispose();
                }
                catch { }

                // Explicitly dispose overlay services (their windows prevent WPF shutdown)
                try
                {
                    App.ScreenOcr?.Dispose();
                    App.KeywordTriggers?.Dispose();
                    App.KeywordHighlight?.Dispose();
                    App.Overlay?.Dispose();
                }
                catch { }

                // Stop the webcam pipeline immediately on close. Critical: the
                // gaze debug cursor is an unowned visible window that keeps WPF
                // alive past MainWindow close — if we don't tear this down here,
                // App.OnExit never runs, the capture loop keeps holding the
                // camera handle, and the cursor keeps moving with the app
                // "closed". Order matters: stop dependents (cursor + focus
                // gaze) before stopping Webcam, then kill the camera handle.
                try
                {
                    App.GazeFocus?.Dispose();
                    App.GazeCursor?.Dispose();
                    App.Webcam?.Dispose();
                }
                catch { }
            }
            else
            {
                // Always minimize to tray instead of closing
                e.Cancel = true;

                // Close any quiz windows (topmost/fullscreen, would stay visible while app is in tray)
                try
                {
                    foreach (var quiz in Application.Current.Windows.OfType<PopQuizWindow>().ToList())
                        quiz.Close();
                    foreach (var quiz in Application.Current.Windows.OfType<QuizWindow>().ToList())
                        quiz.Close();
                }
                catch { }

                _trayIcon?.MinimizeToTray();
                HideAvatarTube();

                // NOTE: bouncing/subliminal text is intentionally LEFT RUNNING while in the tray.
                // It's an unowned, topmost, click-through overlay, so it keeps rendering over the
                // desktop with MainWindow hidden — which is the point of a background conditioning
                // tool. This also matches every other minimize-to-tray path (start-minimized,
                // scheduler auto-start, remote), which never stopped it. (Reported: bounce text
                // vanished when the app went to tray via the X button.)
            }

            // Make sure the Blink Trainer demo timer + live subscription are
            // released. Idempotent; also called from the tab hide-all path,
            // so this is just a safety net for the close-window-while-tab-
            // visible case.
            try { StopBlinkTrainerDemoLoop(); } catch { }
            try { UnsubscribeBlinkTrainerLiveBlink(); } catch { }

            // Only raise the public Closing event (exit-cleanup handlers: global-hotkey unregister,
            // gaze/blink stop) when we're actually exiting. On a minimize-to-tray (e.Cancel==true) the
            // app keeps running, so those handlers must NOT fire — otherwise the global hotkey would die
            // after the first X press (#446).
            if (_exitRequested)
                base.OnClosing(e);
        }

        protected override void OnDpiChanged(System.Windows.DpiScale oldDpi, System.Windows.DpiScale newDpi)
        {
            // Moving to a monitor with a different scale factor makes WPF rebuild every visible layered
            // window's composition surface synchronously. Pause new flash/bubble/overlay spawns for a
            // beat so we don't pile fresh surfaces onto that rebuild burst (desktop-heap/GPU exhaustion
            // -> "Not enough quota" crash / render-thread wedge). See DisplayChangeCoordinator.
            Services.UI.DisplayChangeCoordinator.NotifyDisplayChange("dpi-changed");
            base.OnDpiChanged(oldDpi, newDpi);
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);

            // Handle restoring from tray or maximizing
            if (WindowState == WindowState.Normal)
            {
                ShowAvatarTube();

                // Re-attach avatar if it was attached before maximizing
                if (_avatarWasAttachedBeforeMaximize && _avatarTubeWindow != null && _avatarTubeWindow.IsDetached)
                {
                    _avatarTubeWindow.Attach();
                    _avatarWasAttachedBeforeMaximize = false;
                }

                // Restore autonomy and avatar mute state if we paused them on minimize
                if (_autonomyWasPausedOnMinimize && _wasAutonomyRunningBeforeMinimize)
                {
                    App.Autonomy?.Start();
                    _autonomyWasPausedOnMinimize = false;
                    _wasAutonomyRunningBeforeMinimize = false;
                    App.Logger?.Debug("MainWindow: Restored autonomy mode after restore from minimize");
                }

                if (_avatarWasMutedOnMinimize && _wasAvatarUnmutedBeforeMinimize)
                {
                    _avatarTubeWindow?.SetMuteAvatar(false);
                    _avatarWasMutedOnMinimize = false;
                    _wasAvatarUnmutedBeforeMinimize = false;
                    App.Logger?.Debug("MainWindow: Restored avatar unmuted state after restore from minimize");
                }

                // Update maximize button icon
                BtnMaximize.Content = "☐";
            }
            else if (WindowState == WindowState.Maximized)
            {
                // Detach avatar when maximizing (it would be in wrong position otherwise)
                if (_avatarTubeWindow != null && !_avatarTubeWindow.IsDetached)
                {
                    _avatarWasAttachedBeforeMaximize = true;
                    _avatarTubeWindow.Detach();
                }

                ShowAvatarTube();

                // Update maximize button icon
                BtnMaximize.Content = "❐";
            }
            else if (WindowState == WindowState.Minimized)
            {
                // Auto-pause autonomy and mute avatar when minimized with attached avatar
                // (no point running effects when user can't see them)
                if (_avatarTubeWindow != null && !_avatarTubeWindow.IsDetached)
                {
                    // Pause autonomy if it's running
                    if (App.Autonomy?.IsEnabled == true)
                    {
                        _wasAutonomyRunningBeforeMinimize = true;
                        _autonomyWasPausedOnMinimize = true;
                        App.Autonomy.Stop();
                        App.Logger?.Debug("MainWindow: Auto-paused autonomy mode on minimize (attached avatar)");
                    }

                    // Mute avatar if it's not already muted
                    if (_avatarTubeWindow.IsMuted == false)
                    {
                        _wasAvatarUnmutedBeforeMinimize = true;
                        _avatarWasMutedOnMinimize = true;
                        _avatarTubeWindow.SetMuteAvatar(true);
                        App.Logger?.Debug("MainWindow: Auto-muted avatar on minimize (attached avatar)");
                    }
                }
            }
        }

        #endregion
    }
}
