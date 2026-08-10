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
    // Lab tab: webcam diagnostics, blink trainer glue, and device UI.
    //
    // TOMBSTONE (UX restructure, Phase 5): the "AI Companion Effects & Memory" card left this tab
    // for the Companion door (Views\Controls\Companion\AiPermissionsGrid, mounted as Z7b of
    // CompanionRoomView). Nothing of it was ever in this file — its handlers have always lived in
    // MainWindow.Patreon.cs (ChkCapEffects_Changed, ChkAllowEffect_Changed,
    // SliderMaxHapticIntensity_ValueChanged, ChkChatMemoryEnabled_Changed, BtnClearChatMemory_Click,
    // SyncLabEffectPermsUI) — which is why the move touched no logic here. Noted so nobody goes
    // looking for the permissions grid in the file named after the tab it used to sit on.
    public partial class MainWindow
    {
        #region Lab Tab

        private void BtnLab_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("lab");
        }

        // ─── [DEBUG] Webcam smoke test — TEMPORARY, remove with the XAML card ───
        private bool _webcamDebugSubscribed;
        private int _webcamDebugBlinkCount;
        private int _webcamDebugMouthOpenCount;
        private int _webcamDebugTongueOutCount;
        private GazeSide _webcamDebugLastGaze = GazeSide.Center;
        private bool _webcamDebugLastGazeSet;
        private string _webcamDebugFaceLabel = "—";

        // Stored delegate refs so EnsureWebcamDebugSubscribed's six lambdas can
        // actually be unhooked from App.Webcam in OnClosing — the pre-existing
        // _webcamDebugSubscribed flag only blocked re-subscription, it didn't
        // tear down. Without these the lambdas (which capture `this`) hold a
        // reference to MainWindow forever.
        private Action<WebcamTrackingState>? _onDebugStateChanged;
        private Action? _onDebugFaceFound;
        private Action? _onDebugFaceLost;
        private Action? _onDebugBlink;
        private Action? _onDebugMouthOpen;
        private Action? _onDebugTongueOut;
        private Action<GazeSide>? _onDebugGazeSide;

        // Camera-active pill in the title bar — visible whenever any webcam
        // feature has the capture loop running. Stored handler so we can
        // unhook in OnClosing alongside the debug subscriptions above.
        private Action<WebcamTrackingState>? _onPillStateChanged;

        // Mic-active pill in the title bar — the privacy indicator. Visible whenever the microphone is
        // (or, for an always-on wake word, continuously is) physically open: any Vosk capture session
        // (commands / Voice Lock Cards / mantras), the sherpa wake mic, OR the whole time wake word is
        // armed. Stored handlers so we can unhook in OnClosing. Both events fire off the capture thread
        // → marshal to the UI thread.
        private EventHandler<bool>? _onMicListeningChanged;   // App.Speech (Vosk capture)
        private EventHandler<bool>? _onWakeListeningChanged;  // App.WakeWord (sherpa wake mic)

        private void WireMicActivePill()
        {
            if (_onMicListeningChanged != null) return; // already wired

            if (App.Speech != null)
            {
                _onMicListeningChanged = (_, _) => RunOnUi(UpdateMicPill);
                App.Speech.ListeningChanged += _onMicListeningChanged;
            }
            if (App.WakeWord != null)
            {
                _onWakeListeningChanged = (_, _) => RunOnUi(UpdateMicPill);
                App.WakeWord.ListeningChanged += _onWakeListeningChanged;
            }
            UpdateMicPill();
        }

        /// <summary>
        /// Honest mic privacy posture: light the title-bar pill whenever the microphone is reachable.
        /// That's any live capture (Vosk command/lock-card/mantra OR the sherpa wake mic), AND — because
        /// an enabled wake word holds the mic open in a tight always-on loop — the entire time the wake
        /// word is armed (consent + wake enabled). Push-to-talk alone does NOT keep the mic open, so it
        /// only lights the pill during its actual capture window. Call this from the listening events and
        /// from every arm/disarm path so "mic on" and "pill on" can never diverge.
        /// </summary>
        internal void UpdateMicPill()
        {
            if (MicActivePill == null) return;
            var s = App.Settings?.Current;
            bool wakeContinuous = s?.MicConsentGiven == true && s.SpeechWakeWordEnabled;
            bool capturing = App.Speech?.IsListening == true || App.WakeWord?.IsListening == true;
            MicActivePill.Visibility = (wakeContinuous || capturing) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void MicActivePill_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Click is the privacy stop affordance — same intent as the camera pill. Fully disarm the
            // offline mic so it genuinely stays off (and the dashboard Voice dot reflects it): clears
            // wake-word + push-to-talk, cuts live capture, tears down the loop/hook, and drops any open
            // Voice Lock Card to typed solve so the lock still holds.
            try { DisarmVoiceMic(); } catch { }
        }

        private void WireWebcamActivePill()
        {
            if (App.Webcam == null || _onPillStateChanged != null) return;

            void Update(WebcamTrackingState s)
            {
                if (WebcamActivePill != null)
                {
                    WebcamActivePill.Visibility = (s == WebcamTrackingState.Tracking || s == WebcamTrackingState.FaceLost)
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                }
                UpdateLabTrackerUi(s);
            }

            _onPillStateChanged = Update;
            App.Webcam.OnTrackingStateChanged += _onPillStateChanged;
            // Reflect current state on wire-up — service may already be running
            // if we got here after a previous Stop/Start cycle.
            Update(App.Webcam.State);
        }

        // --- Rapid 6-blink recalibration gesture -----------------------------
        // Blinking fast 6 times in a row halts all active conditioning (keeping
        // the camera on) and offers recalibration. The blink detector enforces a
        // 500ms cooldown between fires (WebcamTrackingService.BlinkCooldownMs),
        // so 6 blinks physically span >=2.5s — a literal "6 in 2s" can't fire.
        // 3.5s is the achievable window, and still far above the natural blink
        // rate (~1 per 3-4s) so spontaneous blinking never triggers it.
        private const int RapidBlinkRecalCount = 6;
        private const int RapidBlinkRecalWindowMs = 3500;
        private readonly Queue<DateTime> _rapidBlinkTimes = new();
        private Action? _onRapidBlinkRecal;
        private bool _rapidBlinkRecalInProgress;

        private void WireRapidBlinkRecalibrateShortcut()
        {
            if (App.Webcam == null || _onRapidBlinkRecal != null) return;

            void OnBlink()
            {
                // Opt-in via the toggle shown on every webcam card.
                if (App.Settings?.Current?.BlinkRecalibrateShortcutEnabled != true) return;
                // Don't fire while a calibration window is already open (its
                // verify step asks the user to blink) or while we're mid-trigger.
                if (_rapidBlinkRecalInProgress || WebcamCalibrationWindow.IsShowing) return;

                var now = DateTime.UtcNow;
                _rapidBlinkTimes.Enqueue(now);
                var cutoff = now.AddMilliseconds(-RapidBlinkRecalWindowMs);
                while (_rapidBlinkTimes.Count > 0 && _rapidBlinkTimes.Peek() < cutoff)
                    _rapidBlinkTimes.Dequeue();

                if (_rapidBlinkTimes.Count >= RapidBlinkRecalCount)
                {
                    _rapidBlinkTimes.Clear();
                    _ = TriggerRapidBlinkRecalibrateAsync();
                }
            }

            _onRapidBlinkRecal = OnBlink;
            App.Webcam.OnBlink += _onRapidBlinkRecal;
        }

        private async Task TriggerRapidBlinkRecalibrateAsync()
        {
            if (_rapidBlinkRecalInProgress) return;
            _rapidBlinkRecalInProgress = true;
            try
            {
                App.Logger?.Information("Rapid 6-blink gesture: stopping all activity and offering recalibration.");

                // Halt everything the user is experiencing — same surface as a
                // panic press — but DELIBERATELY leave App.Webcam running: the
                // calibration window requires the capture loop to be live.
                StopAllForRecalibration();

                // Guard against a race where the capture loop stopped between the
                // triggering blink and here.
                var svc = App.Webcam;
                if (svc != null && !svc.IsRunning)
                    await Task.Run(() => svc.Start());
                if (svc == null || !svc.IsRunning)
                {
                    App.Logger?.Warning("Rapid-blink recal: webcam not running and could not be (re)started; aborting.");
                    return;
                }

                var choice = MessageBox.Show(this,
                    "You blinked to stop everything. Recalibrate webcam tracking now?",
                    "Recalibrate?",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (choice != MessageBoxResult.Yes) return;

                WebcamCalibrationWindow.ShowDialogWithRecalibrate(this);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Rapid-blink recalibration failed");
            }
            finally
            {
                _rapidBlinkRecalInProgress = false;
                _rapidBlinkTimes.Clear();
            }
        }

        /// <summary>
        /// Stops all active conditioning output (engine, session, videos, audio,
        /// gaze features) — mirroring the "stop" branch of the panic key, minus
        /// the window-restore / exit / achievement bookkeeping. Leaves the
        /// webcam capture loop (App.Webcam) RUNNING so recalibration can proceed.
        /// </summary>
        private void StopAllForRecalibration()
        {
            try { Controls.HelpPopover.CloseActive(); } catch { }
            try { App.KillAllAudio(); } catch (Exception ex) { App.Logger?.Warning(ex, "Recal stop: KillAllAudio failed"); }
            try { App.Autonomy?.CancelActivePulses(); } catch { }
            try { App.GazeFocus?.Stop(); } catch { }
            try { App.BlinkTrainer?.Stop(); } catch { }
            try
            {
                if (_sessionEngine != null && _sessionEngine.IsRunning && !_sessionEngine.IsPaused)
                {
                    _sessionEngine.PauseSession();
                    if (TxtPauseIcon != null) TxtPauseIcon.Text = "▶";
                    if (BtnPauseSession != null) BtnPauseSession.ToolTip = Loc.Get("tooltip_resume_session");
                }
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "Recal stop: pause session failed"); }
            try { if (_isRunning) StopEngine(); } catch (Exception ex) { App.Logger?.Warning(ex, "Recal stop: StopEngine failed"); }
            try { App.InteractionQueue?.ForceReset(); } catch { }
        }

        // --- "Blink to recalibrate" toggle ------------------------------------
        // One setting (BlinkRecalibrateShortcutEnabled) and, since Phase 2 of the UX
        // restructure, one checkbox: AppSettingsTab.ChkBlinkRecalWebcamBar in
        // Settings → Devices. It used to be rendered FIVE times (Lab Gaze card, Lab Focus
        // card, Lab engine bar, Blink Trainer header, Deeper webcam card); the four extras
        // and the fan-out they needed are gone. The guard survives because
        // SyncBlinkRecalToggles is still the seeding path (MainWindow.xaml.cs) and seeding
        // must not re-enter the save.
        private bool _syncingBlinkRecalToggles;

        internal void ChkBlinkRecalShortcut_Changed(object sender, RoutedEventArgs e)
        {
            if (_syncingBlinkRecalToggles) return;
            bool val = (sender as System.Windows.Controls.CheckBox)?.IsChecked == true;
            if (App.Settings?.Current != null)
            {
                App.Settings.Current.BlinkRecalibrateShortcutEnabled = val;
                App.Settings?.Save();
            }
        }

        /// <summary>Seed the single blink-recal toggle from settings without re-entering the save path.</summary>
        private void SyncBlinkRecalToggles(bool val)
        {
            _syncingBlinkRecalToggles = true;
            try
            {
                var cb = AppSettingsTab?.ChkBlinkRecalWebcamBar;
                if (cb != null && cb.IsChecked != val) cb.IsChecked = val;
            }
            finally { _syncingBlinkRecalToggles = false; }
        }

        // Lab redesign: reflect tracker state on the engine-bar status pill (which now lives in
        // Settings → Devices) and dim the two Eyes cards (with "start tracking" hints) when the
        // tracker is off. Additive — keyed off the same OnTrackingStateChanged path as the title
        // pill. Phase 2 added the read-only chips on Lab / Blink Trainer / Deeper to the same call
        // so those tabs still show the state their cards depend on without owning the camera.
        private void UpdateLabTrackerUi(WebcamTrackingState s)
        {
            bool live = (s == WebcamTrackingState.Tracking || s == WebcamTrackingState.FaceLost);
            var green = TryFindResource("SuccessGreenBrush") as Brush;
            var muted = TryFindResource("TextMutedBrush") as Brush;
            var panelAccent = TryFindResource("PanelAccentBrush") as Brush;

            if (AppSettingsTab.LabTrackerDot != null) AppSettingsTab.LabTrackerDot.Fill = live ? (green ?? AppSettingsTab.LabTrackerDot.Fill) : (muted ?? AppSettingsTab.LabTrackerDot.Fill);
            if (AppSettingsTab.LabTrackerPill != null) AppSettingsTab.LabTrackerPill.BorderBrush = live ? (green ?? AppSettingsTab.LabTrackerPill.BorderBrush) : (panelAccent ?? AppSettingsTab.LabTrackerPill.BorderBrush);

            if (LabTab.LabGazeCard != null) LabTab.LabGazeCard.Opacity = live ? 1.0 : 0.62;
            if (LabTab.LabFocusCard != null) LabTab.LabFocusCard.Opacity = live ? 1.0 : 0.62;
            if (LabTab.LabGazeNeedsTracker != null) LabTab.LabGazeNeedsTracker.Visibility = live ? Visibility.Collapsed : Visibility.Visible;
            if (LabTab.LabFocusNeedsTracker != null) LabTab.LabFocusNeedsTracker.Visibility = live ? Visibility.Collapsed : Visibility.Visible;

            UpdateWebcamStatusChips(live, green, muted);
        }

        /// <summary>
        /// Repaint the read-only webcam chips that replaced the duplicated device pickers on the
        /// Lab, Blink Trainer and Deeper tabs (Phase 2). Display only — nothing here writes a
        /// setting, and every access is null-guarded because a tab view may not be realized yet.
        /// The status text is the same string the engine-bar pill shows, so all four surfaces
        /// always agree.
        /// </summary>
        private void UpdateWebcamStatusChips(bool live, Brush? green = null, Brush? muted = null)
        {
            try
            {
                green ??= TryFindResource("SuccessGreenBrush") as Brush;
                muted ??= TryFindResource("TextMutedBrush") as Brush;
                var text = AppSettingsTab?.TxtWebcamDebugStatus?.Text ?? (live ? "Tracking" : "Stopped");

                void Paint(System.Windows.Shapes.Ellipse? dot, System.Windows.Controls.TextBlock? label)
                {
                    if (dot != null) dot.Fill = (live ? green : muted) ?? dot.Fill;
                    if (label != null) label.Text = text;
                }

                Paint(LabTab?.WebcamStatusChipLabDot, LabTab?.TxtWebcamStatusChipLab);
                Paint(BlinkTrainerTab?.WebcamStatusChipBlinkTrainerDot, BlinkTrainerTab?.TxtWebcamStatusChipBlinkTrainer);
                Paint(DeeperTab?.WebcamStatusChipDeeperDot, DeeperTab?.TxtWebcamStatusChipDeeper);
            }
            catch (Exception ex) { App.Logger?.Debug("UpdateWebcamStatusChips: {E}", ex.Message); }
        }

        /// <summary>
        /// "Configure in Settings" from any of the three webcam status chips, and from the
        /// dashboard's Webcam tile. Replaces the FeaturePopupWindow that used to borrow the Lab's
        /// engine bar (DetachWebcamBarInto), which no longer exists.
        /// </summary>
        internal void OpenDeviceSettings()
        {
            try
            {
                ShowTab("appsettings");
                AppSettingsTab?.FocusSection("devices");
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "OpenDeviceSettings failed"); }
        }

        /// <summary>
        /// Re-enumerate everything the Devices page shows and re-seed the toggles it owns. Called
        /// from <c>DevicesSettingsSection.OnSectionShown</c>, i.e. every time the Settings door
        /// opens — which is the seam that replaced "the Lab tab was shown" for the camera lists and
        /// "the Webcam popup was opened" for the mic list.
        /// </summary>
        internal void RefreshDeviceSettingsLists()
        {
            try { RefreshWebcamDeviceList(); } catch (Exception ex) { App.Logger?.Warning(ex, "Devices: camera list refresh failed"); }
            try { RefreshWebcamMonitorList(); } catch (Exception ex) { App.Logger?.Warning(ex, "Devices: monitor list refresh failed"); }

            var s = App.Settings?.Current;
            if (s == null || AppSettingsTab == null) return;

            var wasLoading = _isLoading;
            _isLoading = true;
            try
            {
                if (AppSettingsTab.ChkRestrictGazeToCalScreen != null)
                    AppSettingsTab.ChkRestrictGazeToCalScreen.IsChecked = s.RestrictGazeContentToCalibratedScreen;
                if (AppSettingsTab.ChkWebcamDriftCorrection != null)
                    AppSettingsTab.ChkWebcamDriftCorrection.IsChecked = s.WebcamAutoDriftCorrection;
            }
            finally { _isLoading = wasLoading; }

            SyncBlinkRecalToggles(s.BlinkRecalibrateShortcutEnabled);
            UpdatePanicKeyButton();
        }

        private void WebcamActivePill_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Click is the panic-stop affordance. Stops every consumer that
            // shares App.Webcam — Webcam Triggers, Focus Gaze, Blink Trainer,
            // Gaze Minigame all release together when the service stops.
            try { App.GazeFocus?.Stop(); } catch { }
            try { App.BlinkTrainer?.Stop(); } catch { }
            try { App.Webcam?.Stop(); } catch { }
        }

        internal async void BtnWebcamDebugStart_Click(object sender, RoutedEventArgs e)
        {
            var svc = App.Webcam;
            if (svc == null)
            {
                AppendWebcamDebugLog("App.Webcam is null — service not initialized");
                return;
            }

            if (svc.IsRunning)
            {
                svc.Stop();
                AppSettingsTab.BtnWebcamDebugStart.Content = "Start tracking";
                AppendWebcamDebugLog("Stop requested.");
                RefreshBlinkTrainerTrackerButton();
                return;
            }

            if (!WebcamTrackingService.IsConsentCurrent())
            {
                AppendWebcamDebugLog("Consent not given — opening consent dialog…");
                var dlg = new WebcamConsentDialog { Owner = this };
                var ok = dlg.ShowDialog();
                if (ok != true || !dlg.ConsentGiven)
                {
                    AppendWebcamDebugLog("Consent declined or dialog cancelled.");
                    return;
                }
                AppendWebcamDebugLog("Consent granted.");
            }

            EnsureWebcamDebugSubscribed();
            _webcamDebugBlinkCount = 0;
            _webcamDebugMouthOpenCount = 0;
            _webcamDebugTongueOutCount = 0;
            _webcamDebugLastGazeSet = false;
            _webcamDebugFaceLabel = "—";
            UpdateWebcamDebugCounters();

            var started = await StartWebcamOffUiThreadAsync(svc);
            if (started)
            {
                AppSettingsTab.BtnWebcamDebugStart.Content = "Stop tracking";
                AppendWebcamDebugLog("Start() returned true — capture thread launching.");
            }
            else
            {
                AppendWebcamDebugLog($"Start() returned false. State={svc.State}. See logs/app.log.");
            }
            RefreshBlinkTrainerTrackerButton();
        }

        // Webcam Start() does VideoCapture open + 3 ONNX InferenceSession ctors
        // synchronously. On slow USB negotiation or driver-init paths that can
        // block 10-30s; doing it on the UI thread freezes the window long
        // enough for Windows' "not responding" reaper to terminate the app
        // (XTNSN's BUG-T3HE68DHXY pattern: instant freeze on click → silent
        // crash 10-15s later, no managed exception). Hop to a worker thread.
        private async Task<bool> StartWebcamOffUiThreadAsync(WebcamTrackingService svc)
        {
            AppendWebcamDebugLog("Starting webcam (camera open + model load can take a few seconds)…");
            if (AppSettingsTab.TxtWebcamDebugStatus != null) AppSettingsTab.TxtWebcamDebugStatus.Text = "Starting…";
            // The movable loading splash is driven globally off the service's
            // OnStartupProgress event (see InstallWebcamLoadingSplash), so it
            // shows no matter which code path calls Start() — not just this one.
            try
            {
                return await Task.Run(() => svc.Start());
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "MainWindow: webcam Start() threw on worker thread");
                AppendWebcamDebugLog($"Start() threw: {ex.Message}");
                return false;
            }
        }

        // The webcam loading splash is shown/updated/closed purely in response
        // to WebcamTrackingService.OnStartupProgress (fired from inside Start())
        // and OnTrackingStateChanged. Wiring it here, once, means every entry
        // point that starts the engine — the Lab debug button, calibration, the
        // Blink Trainer, the enhanced-video / browser nudges, the mandatory
        // video player — gets the splash without each call site knowing about
        // it. All these events are marshalled to the UI thread by the service.
        private WebcamLoadingSplash? _webcamLoadingSplash;
        private Action<double, string>? _onWebcamStartupProgress;
        private Action<WebcamTrackingState>? _onWebcamStartupState;

        private void InstallWebcamLoadingSplash()
        {
            if (App.Webcam == null || _onWebcamStartupProgress != null) return;

            _onWebcamStartupProgress = (progress, status) =>
            {
                try
                {
                    if (progress >= 1.0)
                    {
                        // Engine is up — show the bar full for a beat, then fade.
                        _webcamLoadingSplash?.SetProgress(1.0, status);
                        _webcamLoadingSplash?.CloseSplash();
                        return;
                    }

                    if (_webcamLoadingSplash == null)
                    {
                        // Don't pop a splash if the main window isn't on screen
                        // (e.g. minimized to tray during a background start).
                        if (!IsVisible) return;
                        var splash = new WebcamLoadingSplash { Owner = this };
                        splash.Closed += (s, e) =>
                        {
                            if (ReferenceEquals(_webcamLoadingSplash, splash)) _webcamLoadingSplash = null;
                        };
                        _webcamLoadingSplash = splash;
                        splash.Show();
                    }
                    _webcamLoadingSplash.SetProgress(progress, status);
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "MainWindow: webcam loading splash update failed");
                }
            };

            _onWebcamStartupState = state =>
            {
                if (_webcamLoadingSplash == null) return;
                // Start() failed or was aborted before reaching 1.0. Surface WHY
                // rather than letting the bar silently vanish (#300) or hang
                // forever waiting on a wedged camera open (#311).
                switch (state)
                {
                    case WebcamTrackingState.CameraInUse:
                        _webcamLoadingSplash.ShowErrorAndClose(
                            "Camera unavailable — it may be in use by another app, or blocked by antivirus / Windows camera privacy.");
                        break;
                    case WebcamTrackingState.CameraDenied:
                        _webcamLoadingSplash.ShowErrorAndClose(
                            "Camera access denied — enable it in Windows Settings ▸ Privacy ▸ Camera, then try again.");
                        break;
                    case WebcamTrackingState.Error:
                        _webcamLoadingSplash.ShowErrorAndClose(
                            "Eye-tracking engine failed to start. See the webcam debug log for details.");
                        break;
                    case WebcamTrackingState.Stopped:
                        _webcamLoadingSplash.CloseSplash();
                        break;
                }
            };

            App.Webcam.OnStartupProgress += _onWebcamStartupProgress;
            App.Webcam.OnTrackingStateChanged += _onWebcamStartupState;
        }

        private void EnsureWebcamDebugSubscribed()
        {
            if (_webcamDebugSubscribed || App.Webcam == null) return;
            _webcamDebugSubscribed = true;

            _onDebugStateChanged = s =>
            {
                if (AppSettingsTab.TxtWebcamDebugStatus != null) AppSettingsTab.TxtWebcamDebugStatus.Text = s.ToString();
                AppendWebcamDebugLog($"State → {s}");
                if (s == WebcamTrackingState.Stopped || s == WebcamTrackingState.Error
                    || s == WebcamTrackingState.CameraInUse || s == WebcamTrackingState.CameraDenied)
                {
                    if (AppSettingsTab.BtnWebcamDebugStart != null) AppSettingsTab.BtnWebcamDebugStart.Content = "Start tracking";
                }
            };
            _onDebugFaceFound = () =>
            {
                _webcamDebugFaceLabel = "yes";
                UpdateWebcamDebugCounters();
                AppendWebcamDebugLog("Face FOUND");
            };
            _onDebugFaceLost = () =>
            {
                _webcamDebugFaceLabel = "lost";
                UpdateWebcamDebugCounters();
                AppendWebcamDebugLog("Face LOST");
            };
            _onDebugBlink = () =>
            {
                _webcamDebugBlinkCount++;
                UpdateWebcamDebugCounters();
                AppendWebcamDebugLog($"Blink #{_webcamDebugBlinkCount}");
            };
            _onDebugMouthOpen = () =>
            {
                _webcamDebugMouthOpenCount++;
                AppendWebcamDebugLog($"Mouth-open #{_webcamDebugMouthOpenCount}");
            };
            _onDebugTongueOut = () =>
            {
                _webcamDebugTongueOutCount++;
                AppendWebcamDebugLog($"Tongue-out #{_webcamDebugTongueOutCount}");
            };
            _onDebugGazeSide = side =>
            {
                // Only log on CHANGE — gaze side fires every frame and would
                // otherwise drown out blinks and face events.
                if (_webcamDebugLastGazeSet && side == _webcamDebugLastGaze)
                {
                    _webcamDebugLastGaze = side;
                    return;
                }
                _webcamDebugLastGaze = side;
                _webcamDebugLastGazeSet = true;
                UpdateWebcamDebugCounters();
                AppendWebcamDebugLog($"Gaze → {side}");
            };

            App.Webcam.OnTrackingStateChanged += _onDebugStateChanged;
            App.Webcam.OnFaceFound += _onDebugFaceFound;
            App.Webcam.OnFaceLost += _onDebugFaceLost;
            App.Webcam.OnBlink += _onDebugBlink;
            App.Webcam.OnMouthOpen += _onDebugMouthOpen;
            App.Webcam.OnTongueOut += _onDebugTongueOut;
            App.Webcam.OnGazeSide += _onDebugGazeSide;
        }

        private void UnsubscribeWebcamDebug()
        {
            if (!_webcamDebugSubscribed || App.Webcam == null) return;
            if (_onDebugStateChanged != null) App.Webcam.OnTrackingStateChanged -= _onDebugStateChanged;
            if (_onDebugFaceFound    != null) App.Webcam.OnFaceFound -= _onDebugFaceFound;
            if (_onDebugFaceLost     != null) App.Webcam.OnFaceLost  -= _onDebugFaceLost;
            if (_onDebugBlink        != null) App.Webcam.OnBlink     -= _onDebugBlink;
            if (_onDebugMouthOpen    != null) App.Webcam.OnMouthOpen -= _onDebugMouthOpen;
            if (_onDebugTongueOut    != null) App.Webcam.OnTongueOut -= _onDebugTongueOut;
            if (_onDebugGazeSide     != null) App.Webcam.OnGazeSide  -= _onDebugGazeSide;
            _webcamDebugSubscribed = false;
        }

        private void UpdateWebcamDebugCounters()
        {
            if (AppSettingsTab.TxtWebcamDebugCounters == null) return;
            var gaze = _webcamDebugLastGazeSet ? _webcamDebugLastGaze.ToString() : "—";
            AppSettingsTab.TxtWebcamDebugCounters.Text = $"Face: {_webcamDebugFaceLabel} | Blinks: {_webcamDebugBlinkCount} | Gaze: {gaze}";
        }

        internal async void BtnWebcamDebugCalibrate_Click(object sender, RoutedEventArgs e)
        {
            var svc = App.Webcam;
            if (svc == null)
            {
                AppendWebcamDebugLog("App.Webcam is null — service not initialized");
                return;
            }

            if (!WebcamTrackingService.IsConsentCurrent())
            {
                AppendWebcamDebugLog("Consent not given — opening consent dialog…");
                var consent = new WebcamConsentDialog { Owner = this };
                var ok = consent.ShowDialog();
                if (ok != true || !consent.ConsentGiven)
                {
                    AppendWebcamDebugLog("Consent declined.");
                    return;
                }
            }

            // Calibration window expects the service to be running so OnRawIris fires.
            EnsureWebcamDebugSubscribed();
            var startedHere = false;
            if (!svc.IsRunning)
            {
                if (!await StartWebcamOffUiThreadAsync(svc))
                {
                    AppendWebcamDebugLog($"Couldn't start tracking. State={svc.State}.");
                    return;
                }
                startedHere = true;
                AppSettingsTab.BtnWebcamDebugStart.Content = "Stop tracking";
            }

            AppendWebcamDebugLog("Opening calibration window…");
            var result = WebcamCalibrationWindow.ShowDialogWithRecalibrate(this);

            if (result == true)
            {
                AppendWebcamDebugLog("Calibration applied. Gaze classification should now be much more accurate.");
            }
            else
            {
                AppendWebcamDebugLog("Calibration cancelled or failed.");
            }

            // Leave the service running if the user manually started it earlier.
            // Only auto-stop if calibration was the only reason it's running.
            if (startedHere && result != true)
            {
                svc.Stop();
                AppSettingsTab.BtnWebcamDebugStart.Content = "Start tracking";
            }

            // Cross-tab propagation (Cleanup 2 + Phase D): the Blink Trainer
            // page shows calibration status AND has a NeedsCalibration status
            // state; refresh both surfaces if the user has visited the tab.
            RefreshBlinkTrainerWebcamColumn();
            RefreshBlinkTrainerStatusRow();
        }

        internal void BtnGazeMinigame_Click(object sender, RoutedEventArgs e)
        {
            // Tier 2, checked here: the Lab smokescreen is a tab-wide overlay, not a gate on the
            // window this opens.
            if (!TierGate.DemandLab("The Gaze Minigame")) return;

            new Lab.GazeMinigame.GazeMinigameWindow { Owner = this }.Show();
        }

        // ─── Focus Gaze (Lab) ──────────────────────────────────────────
        private bool _focusGazeSyncing;

        private void HookFocusGazeService()
        {
            if (App.GazeFocus == null) return;

            // Belt-and-suspenders: stop GazeFocus before WPF checks its window
            // count on MainWindow close. Without this, the cursor window can
            // keep the OnLastWindowClose process alive — App.OnExit then never
            // runs, leaving Webcam.Dispose uncalled and the camera lit.
            Closing += (_, _) => App.GazeFocus?.Stop();

            App.GazeFocus.OnActiveChanged += active =>
            {
                // Service may stop itself (e.g., webcam death) — keep the
                // toggle visually in sync without re-entering the handler.
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.BeginInvoke(() => SyncFocusGazeToggle(active));
                    return;
                }
                SyncFocusGazeToggle(active);
            };
        }

        private void SyncFocusGazeToggle(bool active)
        {
            if (LabTab.ChkFocusGaze == null) return;
            if (LabTab.ChkFocusGaze.IsChecked == active) return;
            _focusGazeSyncing = true;
            try { LabTab.ChkFocusGaze.IsChecked = active; }
            finally { _focusGazeSyncing = false; }
            if (LabTab.TxtFocusGazeStatus != null && !active) LabTab.TxtFocusGazeStatus.Text = "";
        }

        internal async void ChkFocusGaze_Changed(object sender, RoutedEventArgs e)
        {
            if (_focusGazeSyncing) return;
            if (App.GazeFocus == null) return;

            var on = LabTab.ChkFocusGaze.IsChecked == true;
            if (on)
            {
                if (!WebcamTrackingService.IsConsentCurrent())
                {
                    var dlg = new WebcamConsentDialog { Owner = this };
                    var ok = dlg.ShowDialog();
                    if (ok != true || !dlg.ConsentGiven)
                    {
                        SyncFocusGazeToggle(false);
                        if (LabTab.TxtFocusGazeStatus != null) LabTab.TxtFocusGazeStatus.Text = Localization.Loc.Get("label_focus_gaze_consent_required");
                        return;
                    }
                }

                // Pre-warm the webcam off the UI thread so GazeFocus.Start —
                // which would otherwise call WebcamTrackingService.Start
                // synchronously — finds it already running and just subscribes.
                if (App.Webcam != null && !App.Webcam.IsRunning)
                {
                    if (LabTab.TxtFocusGazeStatus != null) LabTab.TxtFocusGazeStatus.Text = "Starting webcam…";
                    var started = await Task.Run(() => App.Webcam.Start());
                    if (!started)
                    {
                        SyncFocusGazeToggle(false);
                        if (LabTab.TxtFocusGazeStatus != null)
                            LabTab.TxtFocusGazeStatus.Text = Localization.Loc.GetF("label_focus_gaze_webcam_failed_format", App.Webcam?.State);
                        return;
                    }
                }

                // Arm the master consumer. The engine is shared now — setting
                // MasterEnabled runs EvaluateDesiredState, which starts it
                // (webcam is prewarmed + consent just granted).
                App.GazeFocus.MasterEnabled = true;
                if (App.GazeFocus.IsActive)
                {
                    if (LabTab.TxtFocusGazeStatus != null) LabTab.TxtFocusGazeStatus.Text = Localization.Loc.Get("label_focus_gaze_active");
                }
                else
                {
                    // Couldn't start (almost always: no calibration). Disarm the
                    // master so it doesn't silently hold the engine wanted.
                    App.GazeFocus.MasterEnabled = false;
                    SyncFocusGazeToggle(false);
                    if (LabTab.TxtFocusGazeStatus != null)
                    {
                        if (App.Webcam?.Calibration == null)
                            LabTab.TxtFocusGazeStatus.Text = Localization.Loc.Get("label_focus_gaze_calibrate_first");
                        else
                            LabTab.TxtFocusGazeStatus.Text = Localization.Loc.GetF("label_focus_gaze_webcam_failed_format", App.Webcam?.State);
                    }
                }
            }
            else
            {
                // Release the master consumer. The engine keeps running if a
                // per-feature gaze toggle (Flash pop / linger, Video click) is
                // still on — those are self-sufficient now.
                App.GazeFocus.MasterEnabled = false;
                if (LabTab.TxtFocusGazeStatus != null) LabTab.TxtFocusGazeStatus.Text = "";
            }
        }

        // ─── Blink Trainer — service lifecycle + Running countdown ───────
        // The configurator + stage UI now lives on the dedicated Exclusives
        // page (see "Blink Trainer Tab — *" regions). What's left here is the
        // service-side glue: hook StateChanged, run the 1Hz countdown timer
        // that the new status row's Running text depends on, and stop on
        // window close.
        private DispatcherTimer? _blinkTrainerTickTimer;

        private void HookBlinkTrainerService()
        {
            if (App.BlinkTrainer == null) return;

            // Stop on MainWindow close so the camera doesn't stay lit if the
            // overlay window is the only thing keeping the process alive.
            Closing += (_, _) => App.BlinkTrainer?.Stop();

            App.BlinkTrainer.StateChanged += () =>
            {
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.BeginInvoke(OnBlinkTrainerServiceStateChanged);
                    return;
                }
                OnBlinkTrainerServiceStateChanged();
            };

            // Defensive: if the service is somehow already running at hook
            // time (shouldn't happen — hook runs at window load before the
            // user can start anything — but cheap insurance), make sure the
            // countdown timer is wired.
            SyncBlinkTrainerCountdownTimer();
        }

        /// <summary>
        /// Single fan-out for BlinkTrainerService.StateChanged. Updates the
        /// new Exclusives tab's status row + stage mode and manages the
        /// per-second countdown timer that BlinkTrainerTick drives.
        /// </summary>
        private void OnBlinkTrainerServiceStateChanged()
        {
            try { SyncBlinkTrainerCountdownTimer(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "SyncBlinkTrainerCountdownTimer failed"); }

            try { RefreshBlinkTrainerStatusRow(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "RefreshBlinkTrainerStatusRow failed"); }

            try { ApplyBlinkTrainerStageMode(DetermineBlinkTrainerStageMode()); }
            catch (Exception ex) { App.Logger?.Warning(ex, "ApplyBlinkTrainerStageMode failed"); }
        }

        /// <summary>
        /// Starts / stops the 1Hz countdown timer to match the service's
        /// running state. Idempotent. Drives the new Exclusives tab's Running
        /// status text via BlinkTrainerTick.
        /// </summary>
        private void SyncBlinkTrainerCountdownTimer()
        {
            if (App.BlinkTrainer == null) return;
            var running = App.BlinkTrainer.IsRunning;

            if (running)
            {
                if (_blinkTrainerTickTimer == null)
                {
                    _blinkTrainerTickTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                    _blinkTrainerTickTimer.Tick += BlinkTrainerTick;
                    _blinkTrainerTickTimer.Start();
                }
                BlinkTrainerTick(this, EventArgs.Empty);
            }
            else
            {
                if (_blinkTrainerTickTimer != null)
                {
                    try { _blinkTrainerTickTimer.Stop(); } catch { }
                    _blinkTrainerTickTimer.Tick -= BlinkTrainerTick;
                    _blinkTrainerTickTimer = null;
                }
            }
        }

        private void BlinkTrainerTick(object? sender, EventArgs e)
        {
            if (App.BlinkTrainer == null || !App.BlinkTrainer.IsRunning) return;
            var rem = App.BlinkTrainer.Remaining;

            // New Exclusives tab status text — only overwrite while we're
            // displaying the Running state. Other states (Error / NeedsX) get
            // their own copy from ApplyBlinkTrainerStatusState.
            if (BlinkTrainerTab.BlinkTrainerStatusText != null && _currentBlinkTrainerStatusState == BlinkTrainerStatusState.Running)
                BlinkTrainerTab.BlinkTrainerStatusText.Text = Localization.Loc.GetF("blink_trainer_status_running", rem.ToString(rem.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss"));
        }

        /// <summary>
        /// Lab "Moved to Exclusives" stub navigates to the new home.
        /// </summary>
        internal void BtnLabBlinkTrainerOpenNew_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ShowTab("blinktrainer");
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Lab Blink Trainer stub navigation failed");
            }
        }

        internal async void BtnWebcamDebugTrackerTest_Click(object sender, RoutedEventArgs e)
        {
            var svc = App.Webcam;
            if (svc == null)
            {
                AppendWebcamDebugLog("App.Webcam is null — service not initialized");
                return;
            }

            if (!WebcamTrackingService.IsConsentCurrent())
            {
                AppendWebcamDebugLog("Consent not given — opening consent dialog…");
                var consent = new WebcamConsentDialog { Owner = this };
                var ok = consent.ShowDialog();
                if (ok != true || !consent.ConsentGiven)
                {
                    AppendWebcamDebugLog("Consent declined.");
                    return;
                }
            }

            // Tracker test needs the service running so OnGazeMove fires, AND a
            // calibration loaded so there's a homography to project through.
            EnsureWebcamDebugSubscribed();
            var startedHere = false;
            if (!svc.IsRunning)
            {
                if (!await StartWebcamOffUiThreadAsync(svc))
                {
                    AppendWebcamDebugLog($"Couldn't start tracking. State={svc.State}.");
                    return;
                }
                startedHere = true;
                AppSettingsTab.BtnWebcamDebugStart.Content = "Stop tracking";
            }

            if (svc.Calibration == null)
            {
                AppendWebcamDebugLog("No calibration loaded — run Calibrate (16-point) first.");
                if (startedHere) { svc.Stop(); AppSettingsTab.BtnWebcamDebugStart.Content = "Start tracking"; }
                return;
            }

            AppendWebcamDebugLog("Opening tracker test window…");
            var trackerDlg = new WebcamGazeTrackerWindow { Owner = this };
            App.ApplyCalibrationScreenPlacement(trackerDlg);
            trackerDlg.ShowDialog();
            AppendWebcamDebugLog("Tracker test closed.");

            // Match calibration handler's lifetime: only auto-stop tracking if we
            // were the ones that started it. If the user already had it running,
            // leave it running.
            if (startedHere)
            {
                svc.Stop();
                AppSettingsTab.BtnWebcamDebugStart.Content = "Start tracking";
            }
        }

        internal async void BtnWebcamDebugQuickRecal_Click(object sender, RoutedEventArgs e)
        {
            var svc = App.Webcam;
            if (svc == null)
            {
                AppendWebcamDebugLog("Webcam service unavailable.");
                return;
            }

            if (!WebcamTrackingService.IsConsentCurrent())
            {
                AppendWebcamDebugLog("Consent not given — opening consent dialog…");
                var consent = new WebcamConsentDialog { Owner = this };
                var ok = consent.ShowDialog();
                if (ok != true || !consent.ConsentGiven)
                {
                    AppendWebcamDebugLog("Consent declined.");
                    return;
                }
            }

            EnsureWebcamDebugSubscribed();
            var startedHere = false;
            if (!svc.IsRunning)
            {
                if (!await StartWebcamOffUiThreadAsync(svc))
                {
                    AppendWebcamDebugLog($"Couldn't start tracking. State={svc.State}.");
                    return;
                }
                startedHere = true;
                AppSettingsTab.BtnWebcamDebugStart.Content = "Stop tracking";
            }

            if (svc.Calibration == null)
            {
                AppendWebcamDebugLog("No calibration loaded — run Calibrate (16-point) first. Quick Recal only nudges an existing calibration.");
                if (startedHere) { svc.Stop(); AppSettingsTab.BtnWebcamDebugStart.Content = "Start tracking"; }
                return;
            }

            AppendWebcamDebugLog("Opening quick-recal window…");
            var recalDlg = new WebcamQuickRecalWindow { Owner = this };
            App.ApplyCalibrationScreenPlacement(recalDlg);
            var result = recalDlg.ShowDialog();
            AppendWebcamDebugLog(result == true
                ? $"Quick recal applied (offset {svc.Calibration.RuntimeOffset?.Dx:F0}, {svc.Calibration.RuntimeOffset?.Dy:F0} px)."
                : "Quick recal cancelled.");

            if (startedHere)
            {
                svc.Stop();
                AppSettingsTab.BtnWebcamDebugStart.Content = "Start tracking";
            }

            // Cross-tab propagation (Cleanup 2 + Phase D).
            RefreshBlinkTrainerWebcamColumn();
            RefreshBlinkTrainerStatusRow();
        }

        internal void BtnWebcamReviewPrivacy_Click(object sender, RoutedEventArgs e)
        {
            // Re-open the consent flow for users who want to read the privacy
            // contract again after they've already agreed. The dialog only
            // overwrites WebcamConsentGiven when the user explicitly walks
            // through the gates and clicks Enable — Cancel/close leaves the
            // existing consent state alone, so this is safe to invoke any
            // time as a "review only" path.
            try
            {
                var dlg = new WebcamConsentDialog { Owner = this };
                dlg.ShowDialog();
                AppendWebcamDebugLog("Privacy info reviewed.");
                // Cross-tab propagation (Cleanup 2 + Phase D): consent may have
                // been toggled via the dialog's Enable path.
                RefreshBlinkTrainerWebcamColumn();
                RefreshBlinkTrainerStatusRow();
                ApplyBlinkTrainerStageMode(DetermineBlinkTrainerStageMode());
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Webcam review privacy dialog failed");
            }
        }

        internal void BtnWebcamRevokeConsent_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = MessageBox.Show(
                    this,
                    "Revoke webcam consent?\n\n" +
                    "This will:\n" +
                    "  • Stop webcam tracking immediately\n" +
                    "  • Delete your calibration data\n" +
                    "  • Disable Focus Gaze and any webcam triggers\n" +
                    "  • Clear your consent record\n\n" +
                    "You'll be re-prompted to consent and recalibrate the next time you enable a webcam feature.",
                    "Revoke webcam consent",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning,
                    MessageBoxResult.Cancel);

                if (result != MessageBoxResult.OK) return;

                App.Webcam?.RevokeConsent();
                if (AppSettingsTab.ChkWebcamDebugCursor != null) AppSettingsTab.ChkWebcamDebugCursor.IsChecked = false;
                AppendWebcamDebugLog("Consent revoked. Calibration deleted; webcam features disabled.");

                // Cross-tab propagation (Cleanup 2 + Phase D).
                RefreshBlinkTrainerWebcamColumn();
                RefreshBlinkTrainerStatusRow();
                ApplyBlinkTrainerStageMode(DetermineBlinkTrainerStageMode());
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Webcam revoke consent failed");
            }
        }

        internal void ChkWebcamDebugCursor_Changed(object sender, RoutedEventArgs e)
        {
            if (AppSettingsTab.ChkWebcamDebugCursor == null) return;
            if (AppSettingsTab.ChkWebcamDebugCursor.IsChecked == true)
            {
                App.GazeCursor?.Show("debug-toggle");
                AppendWebcamDebugLog("Debug cursor enabled. Tracking must be running + calibrated for the dot to appear.");
            }
            else
            {
                App.GazeCursor?.Hide("debug-toggle");
                AppendWebcamDebugLog("Debug cursor hidden.");
            }
        }

        internal void ChkWebcamDriftCorrection_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            if (AppSettingsTab.ChkWebcamDriftCorrection == null || App.Settings?.Current == null) return;
            bool v = AppSettingsTab.ChkWebcamDriftCorrection.IsChecked == true;
            App.Settings.Current.WebcamAutoDriftCorrection = v;
            AppendWebcamDebugLog(v
                ? "Auto drift correction enabled — clicks near your gaze will fine-tune calibration."
                : "Auto drift correction disabled.");
        }

        internal void ChkRestrictGazeToCalScreen_Changed(object sender, RoutedEventArgs e)
        {
            // Seeding re-entrancy is covered by _isLoading, which RefreshDeviceSettingsLists raises
            // around its assignment. Before Phase 2 there was a second flag here
            // (_restrictGazeCheckboxSyncing) because THREE checkboxes (Lab, Blink Trainer, Deeper
            // hub) wrote this one AppSettings flag and a mirror helper fanned a change out to the
            // other two. There is one checkbox now — Settings → Devices — the mirror is deleted, and
            // the flag was left never-assigned (CS0649), i.e. a guard that guarded nothing.
            if (_isLoading) return;
            if (AppSettingsTab.ChkRestrictGazeToCalScreen == null || App.Settings?.Current == null) return;
            bool v = AppSettingsTab.ChkRestrictGazeToCalScreen.IsChecked == true;
            App.Settings.Current.RestrictGazeContentToCalibratedScreen = v;
        }


        // Suppresses the SelectionChanged save while we programmatically (re)populate the webcam
        // ComboBox during enumeration / restore. Before Phase 2 this single flag had to cover
        // THREE combos (Lab, Blink Trainer, Deeper) so a populate-one-then-the-other sequence
        // couldn't trip the save path mid-loop; there is one combo now, in Settings → Devices.
        private bool _webcamDevicePopulating;

        /// <summary>
        /// Rebuild the camera picker in Settings → Devices from a live enumeration.
        /// Safe before the Settings view is realized (null check inside PopulateWebcamCombo).
        /// </summary>
        private void PopulateWebcamDeviceCombos()
        {
            if (App.Webcam == null) return;
            var devices = App.Webcam.EnumerateDevices();
            _webcamDevicePopulating = true;
            try
            {
                PopulateWebcamCombo(AppSettingsTab?.CmbWebcamDevice, devices);
            }
            finally
            {
                _webcamDevicePopulating = false;
            }
        }

        private static void PopulateWebcamCombo(
            ComboBox? cb,
            IReadOnlyList<Services.WebcamDeviceEnumerator.WebcamDevice> devices)
        {
            if (cb == null) return;
            cb.Items.Clear();
            if (devices.Count == 0)
            {
                cb.Items.Add(new ComboBoxItem
                {
                    Content = "(no cameras detected)",
                    Tag = -1,
                    IsEnabled = false,
                });
                cb.SelectedIndex = 0;
                return;
            }
            foreach (var d in devices)
            {
                cb.Items.Add(new ComboBoxItem
                {
                    Content = $"[{d.Index}] {d.Name}",
                    Tag = d.Index,
                });
            }
            int saved = App.Settings?.Current?.WebcamDeviceIndex ?? -1;
            int target = saved >= 0 && saved < devices.Count ? saved : 0;
            cb.SelectedIndex = target;
        }

        /// <summary>
        /// Kept as the name existing callers use. There is a single camera combo since Phase 2
        /// (Settings → Devices), so this is a straight repopulate; SyncWebcamComboSelections and
        /// SelectComboByDeviceIndex existed only to keep three combos from diverging and are gone.
        /// </summary>
        private void RefreshWebcamDeviceList() => PopulateWebcamDeviceCombos();

        internal void CmbWebcamDevice_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_webcamDevicePopulating) return;
            if (AppSettingsTab.CmbWebcamDevice?.SelectedItem is not ComboBoxItem item) return;
            if (item.Tag is not int idx || idx < 0) return;

            if (App.Settings?.Current is { } s)
            {
                if (s.WebcamDeviceIndex == idx) return;
                s.WebcamDeviceIndex = idx;
                s.WebcamDeviceName = item.Content?.ToString() ?? "";
                App.Settings.Save();
            }

            AppendWebcamDebugLog($"Camera set to {item.Content}. {(App.Webcam?.IsRunning == true ? "Stop and Start tracking to apply." : "Will be used on next Start.")}");
        }

        internal void BtnWebcamDeviceRefresh_Click(object sender, RoutedEventArgs e)
        {
            PopulateWebcamDeviceCombos();
            // Report the count of actually-enumerated devices, NOT AppSettingsTab.CmbWebcamDevice.Items.Count
            // — when zero cameras are found the combo holds a single "(no cameras detected)"
            // placeholder item, which made the message falsely say "1 found" (#291).
            int found = App.Webcam?.EnumerateDevices().Count ?? 0;
            AppendWebcamDebugLog(found == 0
                ? "Re-scanned cameras: none detected."
                : $"Re-scanned cameras: {found} found.");
        }

        // Re-entrancy guard so seeding the ComboBox doesn't trigger the save path.
        private bool _webcamMonitorPopulating;

        private void RefreshWebcamMonitorList()
        {
            // One combo since Phase 2 (Settings -> Devices). The Blink Trainer and Deeper copies
            // that used to be filled from this same screen list are gone; both tabs show a
            // read-only chip instead. The populating flag still guards the seeding assignment.
            _webcamMonitorPopulating = true;
            try
            {
                var screens = App.GetAllScreensCached();
                var saved = App.Settings?.Current?.WebcamCalibrationScreen ?? "Primary";

                FillMonitorCombo(AppSettingsTab?.CmbWebcamMonitor, screens, saved);
            }
            finally
            {
                _webcamMonitorPopulating = false;
            }
        }

        private static void FillMonitorCombo(ComboBox? cb, System.Collections.Generic.IList<System.Windows.Forms.Screen> screens, string saved)
        {
            if (cb == null) return;
            cb.Items.Clear();
            // Always include "Primary" — survives monitor reorder. GetWebcamCalibrationScreen
            // short-circuits to Screen.PrimaryScreen when set to this sentinel.
            cb.Items.Add(new ComboBoxItem
            {
                Content = Loc.Get("webcam_monitor_primary"),
                Tag = "Primary",
            });
            int n = 1;
            foreach (var s in screens)
            {
                var label = string.Format(
                    Loc.Get("webcam_monitor_item_fmt"),
                    n++,
                    s.DeviceName,
                    s.Bounds.Width,
                    s.Bounds.Height);
                cb.Items.Add(new ComboBoxItem { Content = label, Tag = s.DeviceName });
            }
            int target = 0;
            for (int i = 0; i < cb.Items.Count; i++)
            {
                if (cb.Items[i] is ComboBoxItem ci
                    && ci.Tag is string tag
                    && string.Equals(tag, saved, StringComparison.OrdinalIgnoreCase))
                {
                    target = i; break;
                }
            }
            cb.SelectedIndex = target;
        }

        internal void CmbWebcamMonitor_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_webcamMonitorPopulating) return;
            if (AppSettingsTab.CmbWebcamMonitor?.SelectedItem is not ComboBoxItem item) return;
            if (item.Tag is not string deviceName) return;

            if (App.Settings?.Current is { } s)
            {
                if (string.Equals(s.WebcamCalibrationScreen, deviceName, StringComparison.OrdinalIgnoreCase)) return;
                s.WebcamCalibrationScreen = deviceName;
                App.Settings.Save();
            }

            AppendWebcamDebugLog($"Calibration monitor set to {item.Content}.");
        }

        private void AppendWebcamDebugLog(string line)
        {
            if (AppSettingsTab.TxtWebcamDebugLog == null) return;
            var stamp = DateTime.Now.ToString("HH:mm:ss");
            var existing = AppSettingsTab.TxtWebcamDebugLog.Text;
            if (existing == "(events will appear here)") existing = "";
            var lines = (existing + (existing.Length > 0 ? "\n" : "") + $"[{stamp}] {line}")
                .Split('\n');
            if (lines.Length > 12) lines = lines[(lines.Length - 12)..];
            AppSettingsTab.TxtWebcamDebugLog.Text = string.Join("\n", lines);
        }
        #endregion
    }
}
