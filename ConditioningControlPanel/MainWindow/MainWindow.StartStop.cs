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
    // Session start/stop lifecycle, scheduler, and engine helpers.
    public partial class MainWindow
    {
        #region Start/Stop

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            // Don't allow manual start/stop while remote controller is connected
            if (App.RemoteControl?.ControllerConnected == true) return;

            if (_isRunning && App.Lockdown?.IsActive == true)
            {
                MessageBox.Show(Loc.Get("msg_you_are_in_lockdown_mode_nyou_cannot_stop_dur"), Loc.Get("title_lockdown"),
                    MessageBoxButton.OK, MessageBoxImage.Stop);
                return;
            }

            if (_isRunning)
            {
                // Check if a session is running
                if (_sessionEngine != null && _sessionEngine.IsRunning)
                {
                    var session = _sessionEngine.CurrentSession;
                    var elapsed = _sessionEngine.ElapsedTime;
                    var remaining = _sessionEngine.RemainingTime;

                    // Apply level-based XP multiplier
                    var level = App.Settings?.Current?.PlayerLevel ?? 1;
                    var multiplier = App.Progression?.GetSessionXPMultiplier(level) ?? 1.0;
                    var potentialXP = (int)Math.Round((session?.BonusXP ?? 0) * multiplier);

                    var penalty = _sessionEngine.XPPenalty;
                    var finalXP = Math.Max(0, potentialXP - penalty);

                    var penaltyText = penalty > 0
                        ? $"\n(Pause penalty: -{penalty} XP, would earn: {finalXP} XP)"
                        : "";

                    var confirmed = ShowStyledDialog(
                        "⚠ Stop Session?",
                        $"You're currently in a session:\n" +
                        $"{session?.Icon} {session?.Name}\n\n" +
                        $"Time elapsed: {((int)elapsed.TotalMinutes):D2}:{elapsed.Seconds:D2}\n" +
                        $"Time remaining: {((int)remaining.TotalMinutes):D2}:{remaining.Seconds:D2}\n\n" +
                        $"If you stop now, you will lose ALL {potentialXP} XP.{penaltyText}\n\n" +
                        "Are you sure you want to quit?",
                        "Yes, stop session", "Keep going");

                    if (!confirmed) return;

                    // Stop the session without completing it
                    _sessionEngine.StopSession(completed: false);
                    if (TxtPresetsStatus != null)
                    {
                        TxtPresetsStatus.Visibility = Visibility.Collapsed;
                        TxtPresetsStatus.Text = "";
                    }
                }
                
                // Track panic press for Relapse achievement
                App.Achievements?.TrackPanicPressed();

                // User manually stopping
                if (App.Settings.Current.SchedulerEnabled && IsInScheduledTimeWindow())
                {
                    _manuallyStoppedDuringSchedule = true;
                }
                StopEngine();
            }
            else
            {
                // User manually starting - clear manual stop flag
                _manuallyStoppedDuringSchedule = false;
                StartEngine();
            }
        }

        // Opens the Start-options menu (Start normally / Jump right in).
        private void BtnStartMenu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button b && b.ContextMenu != null)
            {
                b.ContextMenu.PlacementTarget = b;
                b.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                b.ContextMenu.IsOpen = true;
            }
        }

        private void MenuStartNormal_Click(object sender, RoutedEventArgs e)
        {
            if (!_isRunning) StartEngine();
        }

        private void MenuJumpRightIn_Click(object sender, RoutedEventArgs e)
        {
            RandomizeAndStart();
        }

        /// <summary>
        /// "Jump right in": turns on a fun, non-overwhelming mix and randomizes its
        /// pacing, then starts the engine — a one-tap way to begin without arming
        /// settings by hand. Setters clamp, so the random ranges are always safe.
        /// </summary>
        internal void RandomizeAndStart()
        {
            var s = App.Settings?.Current;
            if (s != null)
            {
                var rng = new Random();
                s.FlashEnabled = true;
                s.FlashFrequency = rng.Next(20, 81);
                s.SimultaneousImages = rng.Next(2, 9);
                s.SubliminalEnabled = true;
                s.SubliminalFrequency = rng.Next(3, 13);
                s.SpiralEnabled = rng.Next(2) == 0;
                s.PinkFilterEnabled = rng.Next(2) == 0;
                App.Settings?.Save();
            }
            if (!_isRunning) StartEngine();
        }

        public void StartEngine()
        {
            SaveSettings();

            // Check for Relapse achievement (restart within 10s of ESC)
            App.Achievements?.CheckRelapse();

            var settings = App.Settings.Current;

            // Track session count and start skill tree service
            settings.TotalSessions++;
            App.SkillTree?.Start();
            App.SkillTree?.TrackTimeOfDayUsage(); // For secret skill unlocks

            App.Flash.Start();
            
            if (settings.MandatoryVideosEnabled)
                App.Video.Start();
            
            if (settings.SubliminalEnabled)
                App.Subliminal.Start();
            
            // Always start overlay service (handles spiral and pink filter)
            // This allows toggling overlays on/off while engine is running
            App.Overlay.Start();

            // Start bubble service
            if (settings.BubblesEnabled)
            {
                App.Bubbles.Start();
            }

            // Start lock card service
            if (settings.LockCardEnabled)
            {
                App.LockCard.Start();
            }

            // Start bubble count game service
            if (settings.BubbleCountEnabled)
            {
                App.BubbleCount.Start();
            }

            // Start bouncing text service
            if (settings.BouncingTextEnabled)
            {
                App.BouncingText.Start();
            }
            else
            {
                // Ensure bouncing text is stopped if disabled (cleanup any leftover state)
                App.BouncingText.Stop();
            }

            // Start mind wipe service
            if (settings.MindWipeEnabled)
            {
                App.MindWipe.Start(settings.MindWipeFrequency, settings.MindWipeVolume / 100.0);

                // Start loop mode if enabled in settings
                if (settings.MindWipeLoop)
                {
                    App.MindWipe.StartLoop(settings.MindWipeVolume / 100.0);
                }
            }

            // Start brain drain service (still gated internally by Brain Drain rework flag)
            if (settings.BrainDrainEnabled)
            {
                App.BrainDrain.Start();
            }

            // Start autonomy service (requires Patreon)
            var hasPatreonAccess = settings.PatreonTier >= 1 || App.Patreon?.IsWhitelisted == true;
            if (hasPatreonAccess && settings.AutonomyModeEnabled && settings.AutonomyConsentGiven)
            {
                App.Autonomy?.Start();
            }

            // Start pop quiz if enabled
            if (settings.PopQuizEnabled)
            {
                App.PopQuiz?.Start();
            }

            // Start pop quiz service
            if (settings.PopQuizEnabled)
            {
                App.PopQuiz?.Start();
            }

            // Start ramp timer if enabled
            if (settings.IntensityRampEnabled)
            {
                StartRampTimer();
            }

            // Browser audio serves as background - no need to play separate music

            _isRunning = true;
            App.IsEngineRunning = true;
            UpdateStartButton();

            // Start conditioning time tracker
            StartConditioningTimeTracker();

            App.Logger?.Information("Engine started - Overlay: {Overlay}, Bubbles: {Bubbles}, LockCard: {LockCard}, BubbleCount: {BubbleCount}, MindWipe: {MindWipe}, BrainDrain: {BrainDrain}",
                App.Overlay.IsRunning, App.Bubbles.IsRunning, App.LockCard.IsRunning, App.BubbleCount.IsRunning, App.MindWipe.IsRunning, App.BrainDrain.IsRunning);

            // If the mandatory video folder holds enhanced videos the current
            // settings won't fully honour (enhancement off, or webcam rules but
            // webcam not running), offer to flip the missing switch(es). Fire-and-
            // forget: scans off the UI thread and never blocks engine start.
            MaybePromptMandatoryVideoEnhancement();
        }

        private bool _stopInProgress;

        public void StopEngine()
        {
            // Re-entrancy guard: the body below pumps the dispatcher (LibVLC
            // cleanup, GC, window force-closes), so a rapid second panic press
            // could re-enter and race overlay dispatches against teardown. (#364)
            if (_stopInProgress) return;
            _stopInProgress = true;
            try
            {
                StopEngineCore();
            }
            finally
            {
                _stopInProgress = false;
            }
        }

        private void StopEngineCore()
        {
            // Stop flash first (safe, no complex cleanup)
            App.Flash.Stop();

            // Tear down any Deeper enhancement engine bound to the playing video.
            // App.Video.Stop() below closes windows via CloseAll, which does NOT
            // raise VideoEnded, so the bridge's normal unbind never fires and its
            // text overlays ("Don't blink") would keep flashing after panic. (#364)
            App.VideoEnhanceBridge?.ForceUnbind();

            // Stop bubbles BEFORE video to avoid UI thread contention
            // Bubbles use high-priority animation timers that can interfere with video cleanup
            App.Bubbles.Stop();
            App.BouncingText.Stop();

            // Now stop video (complex LibVLC cleanup)
            App.Video.Stop();

            // Stop other services
            App.Subliminal.Stop();
            App.Overlay.Stop();
            App.LockCard.Stop();
            App.BubbleCount.Stop();
            App.MindWipe.Stop();
            App.BrainDrain.Stop();
            App.PopQuiz?.Stop();
            // Only stop autonomy if it was started by the session engine (i.e., user didn't enable it independently).
            // If the user has autonomy enabled in settings, let it keep running after session ends.
            var s = App.Settings?.Current;
            var hasPatreon = (s?.PatreonTier ?? 0) >= 1 || App.Patreon?.IsWhitelisted == true;
            if (!(hasPatreon && s != null && s.AutonomyModeEnabled && s.AutonomyConsentGiven))
            {
                App.Autonomy?.Stop();
            }
            App.SkillTree?.Stop();
            App.Audio.ForceUnduck();

            // Force close any open lock card / quiz windows (panic button should close them immediately)
            LockCardWindow.ForceCloseAll();
            BubbleCountWindow.ForceCloseAll();
            BubbleCountResultWindow.ForceCloseAll();
            QuizWindow.ForceCloseAll();
            PopQuizWindow.ForceCloseAll();

            // Stop ramp timer and reset sliders
            StopRampTimer();

            // Stop conditioning time tracker
            StopConditioningTimeTracker();

            _isRunning = false;
            App.IsEngineRunning = false;
            UpdateStartButton();

            // Fire event for avatar reaction
            EngineStopped?.Invoke(this, EventArgs.Empty);

            // Release cached images and compact the Large Object Heap.
            // Flash/overlay BitmapSources are large allocations (>85 KB) that fragment
            // the LOH during sessions. Compacting here returns memory to the OS.
            App.Flash.ClearImageCache();
            System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Optimized, blocking: false);

            App.Logger?.Information("Engine stopped");
        }

        private void StartRampTimer()
        {
            var settings = App.Settings.Current;
            
            // Store base values
            _rampBaseValues["FlashOpacity"] = settings.FlashOpacity;
            _rampBaseValues["SpiralOpacity"] = settings.SpiralOpacity;
            _rampBaseValues["PinkFilterOpacity"] = settings.PinkFilterOpacity;
            _rampBaseValues["MasterVolume"] = settings.MasterVolume;
            _rampBaseValues["SubAudioVolume"] = settings.SubAudioVolume;
            
            _rampStartTime = DateTime.Now;
            
            _rampTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2) // Update every 2 seconds
            };
            _rampTimer.Tick += RampTimer_Tick;
            _rampTimer.Start();
            
            App.Logger?.Information("Ramp timer started - Duration: {Duration}min, Multiplier: {Mult}x", 
                settings.RampDurationMinutes, settings.SchedulerMultiplier);
        }

        private void StopRampTimer()
        {
            _rampTimer?.Stop();
            _rampTimer = null;
            
            // Reset sliders and settings to base values
            if (_rampBaseValues.Count > 0)
            {
                var settings = App.Settings.Current;
                
                if (_rampBaseValues.TryGetValue("FlashOpacity", out var flashOp))
                {
                    SettingsTab.SliderOpacity.Value = flashOp;
                    SettingsTab.TxtOpacity.Text = $"{(int)flashOp}%";
                    settings.FlashOpacity = (int)flashOp;
                }
                if (_rampBaseValues.TryGetValue("SpiralOpacity", out var spiralOp))
                {
                    ProgressionTab.SliderSpiralOpacity.Value = spiralOp;
                    ProgressionTab.TxtSpiralOpacity.Text = $"{(int)spiralOp}%";
                    settings.SpiralOpacity = (int)spiralOp;
                }
                if (_rampBaseValues.TryGetValue("PinkFilterOpacity", out var pinkOp))
                {
                    ProgressionTab.SliderPinkOpacity.Value = pinkOp;
                    ProgressionTab.TxtPinkOpacity.Text = $"{(int)pinkOp}%";
                    settings.PinkFilterOpacity = (int)pinkOp;
                }
                if (_rampBaseValues.TryGetValue("MasterVolume", out var masterVol))
                {
                    SettingsTab.SliderMaster.Value = masterVol;
                    SettingsTab.TxtMaster.Text = $"{(int)masterVol}%";
                    settings.MasterVolume = (int)masterVol;
                }
                if (_rampBaseValues.TryGetValue("SubAudioVolume", out var subVol))
                {
                    SettingsTab.SliderWhisperVol.Value = subVol;
                    SettingsTab.TxtWhisperVol.Text = $"{(int)subVol}%";
                    settings.SubAudioVolume = (int)subVol;
                }
                
                _rampBaseValues.Clear();
                App.Logger?.Information("Ramp timer stopped - values reset to base");
            }
        }

        private void RampTimer_Tick(object? sender, EventArgs e)
        {
            var settings = App.Settings.Current;
            var elapsed = (DateTime.Now - _rampStartTime).TotalMinutes;
            var duration = settings.RampDurationMinutes;
            var multiplier = settings.SchedulerMultiplier;

            // Skip visual effect ramping if a session is active - sessions have their own built-in ramping
            // This prevents the two systems from fighting and causing values to jump around
            var sessionActive = _sessionEngine?.IsRunning == true;

            // Calculate progress (0.0 to 1.0)
            var progress = Math.Min(elapsed / duration, 1.0);

            // Calculate current multiplier based on progress (linear interpolation from 1.0 to max)
            var currentMult = 1.0 + (multiplier - 1.0) * progress;

            // Update linked sliders and settings
            Dispatcher.Invoke(() =>
            {
                // Only apply visual effect ramps when no session is active
                if (!sessionActive && settings.RampLinkFlashOpacity && _rampBaseValues.TryGetValue("FlashOpacity", out var flashBase))
                {
                    var newVal = (int)Math.Min(flashBase * currentMult, 100);
                    SettingsTab.SliderOpacity.Value = newVal;
                    SettingsTab.TxtOpacity.Text = $"{newVal}%";
                    settings.FlashOpacity = newVal;
                }

                if (!sessionActive && settings.RampLinkSpiralOpacity && _rampBaseValues.TryGetValue("SpiralOpacity", out var spiralBase))
                {
                    var newVal = (int)Math.Min(spiralBase * currentMult, 50);
                    ProgressionTab.SliderSpiralOpacity.Value = newVal;
                    ProgressionTab.TxtSpiralOpacity.Text = $"{newVal}%";
                    settings.SpiralOpacity = newVal;
                }
                
                if (!sessionActive && settings.RampLinkPinkFilterOpacity && _rampBaseValues.TryGetValue("PinkFilterOpacity", out var pinkBase))
                {
                    var newVal = (int)Math.Min(pinkBase * currentMult, 50);
                    ProgressionTab.SliderPinkOpacity.Value = newVal;
                    ProgressionTab.TxtPinkOpacity.Text = $"{newVal}%";
                    settings.PinkFilterOpacity = newVal;
                }
                
                if (settings.RampLinkMasterAudio && _rampBaseValues.TryGetValue("MasterVolume", out var masterBase))
                {
                    var newVal = (int)Math.Min(masterBase * currentMult, 100);
                    SettingsTab.SliderMaster.Value = newVal;
                    SettingsTab.TxtMaster.Text = $"{newVal}%";
                    settings.MasterVolume = newVal;
                }
                
                if (settings.RampLinkSubliminalAudio && _rampBaseValues.TryGetValue("SubAudioVolume", out var subBase))
                {
                    var newVal = (int)Math.Min(subBase * currentMult, 100);
                    SettingsTab.SliderWhisperVol.Value = newVal;
                    SettingsTab.TxtWhisperVol.Text = $"{newVal}%";
                    settings.SubAudioVolume = newVal;
                }
            });
            
            // Check if ramp is complete and should end session. NOT while a preset session is active —
            // the preset owns its own master timer and must run its full length; letting a shorter ramp
            // call StopEngine() here tore down the manual services but left the preset timer counting
            // down (#444). The visual-ramp branches above are already guarded with !sessionActive for the
            // same reason; this stop branch was the one that wasn't.
            if (progress >= 1.0 && settings.EndSessionOnRampComplete && !sessionActive)
            {
                App.Logger?.Information("Ramp complete - ending session");
                Dispatcher.Invoke(() =>
                {
                    _trayIcon?.ShowNotification("Session Complete", "Intensity ramp finished. Stopping...", System.Windows.Forms.ToolTipIcon.Info);
                    StopEngine();
                });
            }
        }

        #endregion

        #region Scheduler

        private void CheckSchedulerOnStartup()
        {
            var settings = App.Settings.Current;
            App.Logger?.Information("Scheduler startup check: Enabled={Enabled}, InWindow={InWindow}",
                settings.SchedulerEnabled, IsInScheduledTimeWindow());

            if (!settings.SchedulerEnabled) return;

            if (IsInScheduledTimeWindow())
            {
                App.Logger?.Information("Scheduler: App started within scheduled time window - auto-starting");

                // Minimize to tray and start engine
                _trayIcon?.MinimizeToTray();
                _trayIcon?.ShowNotification("Scheduler Active", "Session auto-started based on schedule.", System.Windows.Forms.ToolTipIcon.Info);

                StartEngine();
                _schedulerAutoStarted = true;
            }
        }

        private void CheckSchedulerAfterSettingsChange()
        {
            var settings = App.Settings.Current;
            if (!settings.SchedulerEnabled) return;

            App.Logger?.Information("Scheduler settings changed - checking time window");

            if (IsInScheduledTimeWindow() && !_isRunning)
            {
                App.Logger?.Information("Scheduler: In time window after settings change - auto-starting");

                _trayIcon?.ShowNotification("Scheduler Active", "Session auto-started based on schedule.", System.Windows.Forms.ToolTipIcon.Info);

                StartEngine();
                _schedulerAutoStarted = true;
            }
        }

        private void SchedulerTimer_Tick(object? sender, EventArgs e)
        {
            var settings = App.Settings.Current;
            if (!settings.SchedulerEnabled) return;
            
            bool inWindow = IsInScheduledTimeWindow();
            
            if (inWindow && !_isRunning && !_schedulerAutoStarted && !_manuallyStoppedDuringSchedule)
            {
                // Time to start!
                App.Logger?.Information("Scheduler: Entering scheduled time window - auto-starting");
                
                Dispatcher.Invoke(() =>
                {
                    _trayIcon?.MinimizeToTray();
                    _trayIcon?.ShowNotification("Scheduler Active", "Session auto-started based on schedule.", System.Windows.Forms.ToolTipIcon.Info);

                    StartEngine();
                    _schedulerAutoStarted = true;
                });
            }
            else if (!inWindow && _isRunning && _schedulerAutoStarted)
            {
                // Time to stop!
                App.Logger?.Information("Scheduler: Exiting scheduled time window - auto-stopping");
                
                Dispatcher.Invoke(() =>
                {
                    StopEngine();
                    _schedulerAutoStarted = false;
                    _trayIcon?.ShowNotification("Scheduler", "Scheduled session ended.", System.Windows.Forms.ToolTipIcon.Info);
                });
            }
            else if (!inWindow)
            {
                // Outside window - reset flags for next window
                _schedulerAutoStarted = false;
                _manuallyStoppedDuringSchedule = false;
            }
        }

        private bool IsInScheduledTimeWindow()
        {
            var settings = App.Settings.Current;
            var now = DateTime.Now;

            // Check if today is an active day
            bool isDayActive = now.DayOfWeek switch
            {
                DayOfWeek.Monday => settings.SchedulerMonday,
                DayOfWeek.Tuesday => settings.SchedulerTuesday,
                DayOfWeek.Wednesday => settings.SchedulerWednesday,
                DayOfWeek.Thursday => settings.SchedulerThursday,
                DayOfWeek.Friday => settings.SchedulerFriday,
                DayOfWeek.Saturday => settings.SchedulerSaturday,
                DayOfWeek.Sunday => settings.SchedulerSunday,
                _ => false
            };

            if (!isDayActive)
            {
                App.Logger?.Debug("Scheduler: {Day} is not an active day", now.DayOfWeek);
                return false;
            }

            // Parse start and end times
            if (!TimeSpan.TryParse(settings.SchedulerStartTime, out var startTime))
            {
                App.Logger?.Warning("Scheduler: Could not parse start time '{Time}', using default 16:00", settings.SchedulerStartTime);
                startTime = new TimeSpan(16, 0, 0);
            }

            if (!TimeSpan.TryParse(settings.SchedulerEndTime, out var endTime))
            {
                App.Logger?.Warning("Scheduler: Could not parse end time '{Time}', using default 22:00", settings.SchedulerEndTime);
                endTime = new TimeSpan(22, 0, 0);
            }

            var currentTime = now.TimeOfDay;

            bool inWindow;
            // Handle case where end time is after midnight (e.g., 22:00 - 02:00)
            if (endTime < startTime)
            {
                // Overnight schedule
                inWindow = currentTime >= startTime || currentTime < endTime;
            }
            else
            {
                // Same-day schedule
                inWindow = currentTime >= startTime && currentTime < endTime;
            }

            App.Logger?.Debug("Scheduler: Current={Current}, Start={Start}, End={End}, InWindow={InWindow}",
                currentTime.ToString(@"hh\:mm"), startTime.ToString(@"hh\:mm"), endTime.ToString(@"hh\:mm"), inWindow);

            return inWindow;
        }

        #endregion

        #region Engine Helpers

        /// <summary>
        /// Apply current UI values to settings immediately (for live updates)
        /// </summary>
        private void ApplySettingsLive()
        {
            if (_isLoading) return;

            var s = App.Settings.Current;

            // Track previous values to detect changes
            var oldFlashFreq = s.FlashFrequency;
            var wasFlashEnabled = s.FlashEnabled;
            var wasVideoEnabled = s.MandatoryVideosEnabled;
            var wasSubliminalEnabled = s.SubliminalEnabled;

            // Flash settings
            s.FlashEnabled = SettingsTab.ChkFlashEnabled.IsChecked ?? true;
            s.FlashClickable = SettingsTab.ChkClickable.IsChecked ?? true;
            s.CorruptionMode = SettingsTab.ChkCorruption.IsChecked ?? false;
            s.HydraLinkedTiming = SettingsTab.ChkHydraLinked.IsChecked ?? true;
            s.FlashGlowEnabled = SettingsTab.ChkFlashGlow.IsChecked ?? true;
            s.FlashFrequency = (int)SettingsTab.SliderPerMin.Value;
            s.SimultaneousImages = (int)SettingsTab.SliderImages.Value;
            s.HydraLimit = (int)SettingsTab.SliderMaxOnScreen.Value;
            s.ImageScale = (int)SettingsTab.SliderSize.Value;
            s.FlashOpacity = (int)SettingsTab.SliderOpacity.Value;
            s.FadeDuration = (int)SettingsTab.SliderFade.Value;

            // Video settings
            s.MandatoryVideosEnabled = SettingsTab.ChkVideoEnabled.IsChecked ?? false;
            s.VideosPerHour = (int)SettingsTab.SliderPerHour.Value;
            s.StrictLockEnabled = SettingsTab.ChkStrictLock.IsChecked ?? false;
            s.AttentionChecksEnabled = SettingsTab.ChkMiniGameEnabled.IsChecked ?? false;
            s.AttentionDensity = (int)SettingsTab.SliderTargets.Value;
            s.RandomizeAttentionTargets = SettingsTab.ChkRandomizeTargets.IsChecked ?? false;
            s.AttentionLifespan = (int)SettingsTab.SliderDuration.Value;
            s.AttentionSize = (int)SettingsTab.SliderTargetSize.Value;

            // Subliminal settings
            s.SubliminalEnabled = SettingsTab.ChkSubliminalEnabled.IsChecked ?? false;
            s.SubliminalFrequency = (int)SettingsTab.SliderSubPerMin.Value;
            s.SubliminalDuration = (int)SettingsTab.SliderFrames.Value;
            s.SubliminalOpacity = (int)SettingsTab.SliderSubOpacity.Value;
            s.SubAudioEnabled = SettingsTab.ChkAudioWhispers.IsChecked ?? false;
            s.SubAudioVolume = (int)SettingsTab.SliderWhisperVol.Value;

            // Audio settings
            s.MasterVolume = (int)SettingsTab.SliderMaster.Value;
            s.AudioDuckingEnabled = SettingsTab.ChkAudioDuck.IsChecked ?? true;
            s.DuckingLevel = (int)SettingsTab.SliderDuck.Value;
            s.ExcludeBambiCloudFromDucking = SettingsTab.ChkExcludeBambiCloudDucking.IsChecked ?? true;

            // Overlay settings
            s.SpiralOpacity = (int)ProgressionTab.SliderSpiralOpacity.Value;
            s.PinkFilterOpacity = (int)ProgressionTab.SliderPinkOpacity.Value;

            // Refresh services if running
            if (_isRunning)
            {
                // Handle Flash service toggle
                if (s.FlashEnabled != wasFlashEnabled)
                {
                    if (s.FlashEnabled)
                        App.Flash.Start();
                    else
                        App.Flash.Stop();
                    App.Logger?.Information("Flash images toggled via ApplySettingsLive: {Enabled}", s.FlashEnabled);
                }
                // Reschedule flash timer if frequency changed
                else if (s.FlashFrequency != oldFlashFreq)
                {
                    App.Flash.RefreshSchedule();
                }

                // Handle Video service toggle
                if (s.MandatoryVideosEnabled != wasVideoEnabled)
                {
                    if (s.MandatoryVideosEnabled)
                        App.Video.Start();
                    else
                        App.Video.Stop();
                    App.Logger?.Information("Mandatory videos toggled via ApplySettingsLive: {Enabled}", s.MandatoryVideosEnabled);
                }

                // Handle Subliminal service toggle
                if (s.SubliminalEnabled != wasSubliminalEnabled)
                {
                    if (s.SubliminalEnabled)
                        App.Subliminal.Start();
                    else
                        App.Subliminal.Stop();
                    App.Logger?.Information("Subliminals toggled via ApplySettingsLive: {Enabled}", s.SubliminalEnabled);
                }

                // Refresh overlays (spiral, pink filter)
                App.Overlay.RefreshOverlays();
            }

            // Save settings to disk
            App.Settings.Save();
        }

        private void UpdateStartButton()
        {
            // Don't overwrite the remote control label while controller is connected
            if (App.RemoteControl?.ControllerConnected == true) return;

            if (_isRunning)
            {
                BtnStart.Background = new SolidColorBrush(Color.FromRgb(255, 107, 107)); // Red
                BtnStart.Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Height = 24,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = "■", FontSize = 16, Width = 20, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center },
                        new TextBlock { Text = "STOP", FontSize = 18, Width = 60, VerticalAlignment = VerticalAlignment.Center }
                    }
                };

                // Also update Presets tab button using direct reference
                if (TxtPresetsStatus != null)
                {
                    TxtPresetsStatus.Text = Loc.Get("label_running");
                }
            }
            else
            {
                BtnStart.Background = FindResource("PinkBrush") as SolidColorBrush;
                BtnStart.Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Height = 24,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = "▶", FontSize = 16, Width = 20, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center },
                        new TextBlock { Text = "START", FontSize = 18, Width = 60, VerticalAlignment = VerticalAlignment.Center }
                    }
                };

                // Also update Presets tab button
                if (TxtPresetsStatus != null)
                {
                    TxtPresetsStatus.Text = "";
                }
            }
        }

        private void UpdateStartButtonForRemoteControl(bool connected)
        {
            if (connected)
            {
                BtnStart.IsEnabled = false;
                BtnStart.Background = new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0x88)); // Green
                BtnStart.Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Height = 24,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = "\U0001F3AE", FontSize = 16, Width = 24, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center },
                        new TextBlock { Text = "REMOTE CONNECTED", FontSize = 14, VerticalAlignment = VerticalAlignment.Center }
                    }
                };
            }
            else
            {
                BtnStart.IsEnabled = true;
                // Directly set button state — don't delegate to UpdateStartButton() which has a
                // ControllerConnected guard that can prevent restoration due to event timing
                if (_isRunning)
                {
                    BtnStart.Background = new SolidColorBrush(Color.FromRgb(255, 107, 107)); // Red
                    BtnStart.Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Height = 24,
                        VerticalAlignment = VerticalAlignment.Center,
                        Children =
                        {
                            new TextBlock { Text = "■", FontSize = 16, Width = 20, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center },
                            new TextBlock { Text = "STOP", FontSize = 18, Width = 60, VerticalAlignment = VerticalAlignment.Center }
                        }
                    };
                }
                else
                {
                    BtnStart.Background = FindResource("PinkBrush") as SolidColorBrush;
                    BtnStart.Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Height = 24,
                        VerticalAlignment = VerticalAlignment.Center,
                        Children =
                        {
                            new TextBlock { Text = "▶", FontSize = 16, Width = 20, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center },
                            new TextBlock { Text = "START", FontSize = 18, Width = 60, VerticalAlignment = VerticalAlignment.Center }
                        }
                    };
                }
            }
        }

        /// <summary>
        /// Find a visual child by name in the visual tree
        /// </summary>
        private static T? FindVisualChild<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T element && element.Name == name)
                    return element;
                var result = FindVisualChild<T>(child, name);
                if (result != null)
                    return result;
            }
            return null;
        }

        #endregion
    }
}
