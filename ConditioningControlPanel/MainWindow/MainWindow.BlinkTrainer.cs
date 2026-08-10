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
    // Blink Trainer tab: demo loop (Phase B), live state machine (Phase D), controls (Phase C).
    public partial class MainWindow
    {
        #region Blink Trainer Tab — demo loop (Phase B)

        // Demo loop state. The loop cycles through 4 SFW abstract gradient PNGs
        // every 2 seconds with a 200ms cross-fade between two overlapping Image
        // controls. This is DEMO MODE ONLY — Phase D's live-mode swap (driven by
        // App.Webcam.OnBlink) will hard-cut, not cross-fade. Keep the two paths
        // separate so we can tune timing independently.
        private DispatcherTimer? _blinkTrainerDemoTimer;
        private int _blinkTrainerDemoIndex = 0;
        private bool _blinkTrainerDemoUsingA = true;
        private List<BitmapImage>? _blinkTrainerDemoAssets;

        /// <summary>
        /// Lazily loads the 4 demo PNGs from pack:// URIs and shuffles them so
        /// the play order isn't predictable. Cached in a field; the images
        /// outlive every tab visit and are only released on app shutdown.
        /// </summary>
        private void EnsureBlinkTrainerDemoAssetsLoaded()
        {
            if (_blinkTrainerDemoAssets != null) return;

            var loaded = new List<BitmapImage>(4);
            for (int i = 1; i <= 4; i++)
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(
                        $"pack://application:,,,/assets/BlinkTrainer/Demo/demo_{i:00}.png",
                        UriKind.Absolute);
                    bmp.EndInit();
                    bmp.Freeze();
                    loaded.Add(bmp);
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "BlinkTrainer demo asset demo_{Index:00}.png failed to load", i);
                }
            }

            // Fisher-Yates shuffle so the first run isn't always demo_01 -> 02 -> ...
            var rng = Random.Shared;
            for (int i = loaded.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (loaded[i], loaded[j]) = (loaded[j], loaded[i]);
            }

            _blinkTrainerDemoAssets = loaded;
        }

        /// <summary>
        /// Starts the 2s cross-fade cycle on the stage preview. Idempotent — if
        /// already running, returns immediately. Sets the initial image
        /// synchronously so the user never sees an empty frame.
        /// </summary>
        private void StartBlinkTrainerDemoLoop()
        {
            try
            {
                if (_blinkTrainerDemoTimer != null) return; // already running

                EnsureBlinkTrainerDemoAssetsLoaded();
                if (_blinkTrainerDemoAssets == null || _blinkTrainerDemoAssets.Count == 0)
                {
                    App.Logger?.Warning("BlinkTrainer: demo loop skipped — no demo assets loaded");
                    return;
                }

                _blinkTrainerDemoIndex = 0;
                _blinkTrainerDemoUsingA = true;
                if (BlinkTrainerTab.BlinkTrainerStageImageA != null)
                {
                    BlinkTrainerTab.BlinkTrainerStageImageA.Source = _blinkTrainerDemoAssets[0];
                    BlinkTrainerTab.BlinkTrainerStageImageA.Opacity = 1;
                }
                if (BlinkTrainerTab.BlinkTrainerStageImageB != null)
                {
                    BlinkTrainerTab.BlinkTrainerStageImageB.Source = null;
                    BlinkTrainerTab.BlinkTrainerStageImageB.Opacity = 0;
                }

                _blinkTrainerDemoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.0) };
                _blinkTrainerDemoTimer.Tick += BlinkTrainerDemoTimer_Tick;
                _blinkTrainerDemoTimer.Start();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "StartBlinkTrainerDemoLoop failed");
            }
        }

        private void BlinkTrainerDemoTimer_Tick(object? sender, EventArgs e) => AdvanceBlinkTrainerDemo();

        /// <summary>
        /// Stops the demo timer and detaches its handler. Idempotent. Does NOT
        /// clear the cached assets (they're cheap to keep around for the next
        /// tab visit).
        /// </summary>
        private void StopBlinkTrainerDemoLoop()
        {
            try
            {
                if (_blinkTrainerDemoTimer == null) return;
                _blinkTrainerDemoTimer.Stop();
                _blinkTrainerDemoTimer.Tick -= BlinkTrainerDemoTimer_Tick;
                _blinkTrainerDemoTimer = null;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "StopBlinkTrainerDemoLoop failed");
            }
        }

        private void AdvanceBlinkTrainerDemo()
        {
            if (_blinkTrainerDemoAssets == null || _blinkTrainerDemoAssets.Count == 0) return;
            if (BlinkTrainerTab.BlinkTrainerStageImageA == null || BlinkTrainerTab.BlinkTrainerStageImageB == null) return;

            _blinkTrainerDemoIndex = (_blinkTrainerDemoIndex + 1) % _blinkTrainerDemoAssets.Count;
            var nextAsset = _blinkTrainerDemoAssets[_blinkTrainerDemoIndex];

            Image incoming = _blinkTrainerDemoUsingA ? BlinkTrainerTab.BlinkTrainerStageImageB : BlinkTrainerTab.BlinkTrainerStageImageA;
            Image outgoing = _blinkTrainerDemoUsingA ? BlinkTrainerTab.BlinkTrainerStageImageA : BlinkTrainerTab.BlinkTrainerStageImageB;

            incoming.Source = nextAsset;

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut } };
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut } };

            incoming.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            outgoing.BeginAnimation(UIElement.OpacityProperty, fadeOut);

            _blinkTrainerDemoUsingA = !_blinkTrainerDemoUsingA;
        }

        /// <summary>
        /// Toggles a Blink Trainer session via BlinkTrainerService. Mirrors
        /// the legacy Lab handler's pre-warm-then-start sequence, but routes
        /// the post-action UI refresh through Phase D's state machine.
        /// </summary>
        internal async void BtnBlinkTrainerStartSession_Click(object sender, RoutedEventArgs e)
        {
            // #743: the prewarm below can take the full 90s camera-open timeout, and nothing used
            // to stop the user clicking again meanwhile. The service now refuses a concurrent
            // start outright, but a dead button is the honest signal that work is in flight.
            var button = sender as System.Windows.Controls.Button;
            if (button != null) button.IsEnabled = false;
            try
            {
                if (App.BlinkTrainer == null) return;

                if (App.BlinkTrainer.IsRunning)
                {
                    App.BlinkTrainer.Stop();
                    // StateChanged fires inside Stop() and refreshes both UIs;
                    // call explicitly here too for the case where Stop runs
                    // synchronously enough that StateChanged is already done.
                    RefreshBlinkTrainerStatusRow();
                    ApplyBlinkTrainerStageMode(DetermineBlinkTrainerStageMode());
                    return;
                }

                // Pre-warm webcam off the UI thread so BlinkTrainerService.Start
                // doesn't block on capture device init. Same pattern as the
                // legacy Lab handler (BtnBlinkTrainerStart_Click).
                if (App.Webcam != null && !App.Webcam.IsRunning && WebcamTrackingService.IsConsentCurrent())
                {
                    try
                    {
                        await Task.Run(() =>
                        {
                            try { App.Webcam.Start(); }
                            catch (Exception ex) { App.Logger?.Warning(ex, "Blink Trainer prewarm failed"); }
                        });
                    }
                    catch { /* swallowed — Start() handles its own error reporting */ }
                }

                App.BlinkTrainer.Start();
                // Defensive refresh (see Stop branch).
                RefreshBlinkTrainerStatusRow();
                ApplyBlinkTrainerStageMode(DetermineBlinkTrainerStageMode());
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Blink Trainer Start handler failed");
            }
            finally
            {
                if (button != null) button.IsEnabled = true;
            }
        }

        #endregion

        #region Blink Trainer Tab — live mode + state machine (Phase D)

        // ──────────────────────────────────────────────────────────────────
        // Stage mode (Demo vs Live)
        // ──────────────────────────────────────────────────────────────────

        private enum BlinkTrainerStageMode
        {
            Demo,         // no consent / no folders / non-premium (Phase E)
            LivePreview,  // ready but no session running — show real swaps in preview
            LiveSession,  // session running — preview keeps mirroring (D.5)
        }

        private BlinkTrainerStageMode _currentBlinkTrainerStageMode = BlinkTrainerStageMode.Demo;
        private bool _blinkTrainerLiveSubscribed;

        // Live-mode pool cache. Token combines the folder list + IncludeVideos
        // bool; rebuild whenever it changes.
        private BlinkTrainerAssetPool? _blinkTrainerLivePool;
        private string _blinkTrainerLivePoolToken = "";
        private string? _blinkTrainerLiveLastPickedPath;

        private BlinkTrainerStageMode DetermineBlinkTrainerStageMode()
        {
            // Phase E: non-premium users always see the demo loop. Their saved
            // folder list stays in settings but doesn't render on the stage,
            // because the demo is what's visible to them through the gate.
            if (App.Patreon?.HasPremiumAccess != true)
                return BlinkTrainerStageMode.Demo;

            var s = App.Settings?.Current;
            bool consented = WebcamTrackingService.IsConsentCurrent();
            bool hasFolders = (s?.BlinkTrainerFolders?.Count ?? 0) > 0;
            bool running = App.BlinkTrainer?.IsRunning == true;

            if (running) return BlinkTrainerStageMode.LiveSession;
            if (consented && hasFolders) return BlinkTrainerStageMode.LivePreview;
            return BlinkTrainerStageMode.Demo;
        }

        /// <summary>
        /// Shows / hides the premium gate based on HasPremiumAccess. Mirrors
        /// the existing RefreshPremiumGate pattern (Bambi / Awareness / etc.)
        /// but keeps Blink Trainer's gate self-contained so the gate logic and
        /// the stage-mode short-circuit live together. Also disables the
        /// gated StackPanel (H.1) so keyboard focus can't tab past the gate
        /// into covered controls — a non-premium user shouldn't be able to
        /// adjust the duration slider via arrow keys with no visible feedback.
        /// </summary>
        private void RefreshBlinkTrainerGate()
        {
            if (BlinkTrainerTab.BlinkTrainerGate == null) return;
            bool premium = App.Patreon?.HasPremiumAccess == true;
            BlinkTrainerTab.BlinkTrainerGate.Visibility = premium ? Visibility.Collapsed : Visibility.Visible;
            // FX (PR-4a): the shared animated gate treatment. Decoration only - see
            // Controls/PremiumGateFx.cs and the twin call in MainWindow.Patreon.RefreshPremiumGate.
            Controls.PremiumGateFx.Attach(BlinkTrainerTab.BlinkTrainerGate);
            if (BlinkTrainerTab.BlinkTrainerGatedContent != null)
                BlinkTrainerTab.BlinkTrainerGatedContent.IsEnabled = premium;
            // Stage actions (status row, Start session, tracker toggle) moved
            // under the preview in v5.9.9; they sit outside the gate overlay's
            // reach, so gate them via IsEnabled here.
            if (BlinkTrainerTab.BlinkTrainerStageActions != null)
                BlinkTrainerTab.BlinkTrainerStageActions.IsEnabled = premium;
        }

        internal async void BtnBlinkTrainerStartStopTracker_Click(object sender, RoutedEventArgs e)
            => await ToggleWebcamTrackingAsync();

        /// <summary>
        /// Shared webcam start/stop used by the Blink-Trainer / Deeper Start-Stop
        /// buttons and the global camera hotkey (suggestion #674). When the tracker
        /// is off and consent is stale this surfaces the same WebcamConsentDialog the
        /// buttons use rather than silently starting the camera. Pass
        /// <paramref name="announce"/> = true to pop a brief toast on toggle — used
        /// by the hotkey path, which has no button label to update.
        /// </summary>
        internal async Task ToggleWebcamTrackingAsync(bool announce = false)
        {
            var svc = App.Webcam;
            if (svc == null) return;

            if (svc.IsRunning)
            {
                svc.Stop();
                RefreshBlinkTrainerTrackerButton();
                if (announce)
                    App.Notifications?.Show(Loc.Get("camera_shortcut_toast_stopped"),
                        NotificationType.Info, TimeSpan.FromSeconds(2));
                return;
            }

            if (!WebcamTrackingService.IsConsentCurrent())
            {
                var dlg = new WebcamConsentDialog { Owner = this };
                var ok = dlg.ShowDialog();
                if (ok != true || !dlg.ConsentGiven) return;
            }

            EnsureWebcamDebugSubscribed();
            var started = await StartWebcamOffUiThreadAsync(svc);
            RefreshBlinkTrainerTrackerButton();
            if (announce && started)
                App.Notifications?.Show(Loc.Get("camera_shortcut_toast_started"),
                    NotificationType.Info, TimeSpan.FromSeconds(2));
        }

        /// <summary>
        /// Global camera-hotkey action. No-op when the webcam service is unavailable;
        /// otherwise toggles the tracker via the shared <see cref="ToggleWebcamTrackingAsync"/>
        /// (which honours the consent gate). Restores/activates the main window first
        /// when a start would need to surface the consent dialog, so the modal isn't
        /// lost behind a minimized owner.
        /// </summary>
        private void ToggleWebcamFromHotkey()
        {
            var svc = App.Webcam;
            if (svc == null) return;

            // A start that still needs consent shows a modal owned by this window —
            // make sure the window is visible so the dialog can't hide behind the tray.
            if (!svc.IsRunning && !WebcamTrackingService.IsConsentCurrent())
            {
                try
                {
                    if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                    Show();
                    Activate();
                }
                catch (Exception ex) { App.Logger?.Warning(ex, "ToggleWebcamFromHotkey: window restore failed"); }
            }

            _ = ToggleWebcamTrackingAsync(announce: true);
        }

        // Keeps the BT tracker toggle in sync with WebcamTrackingService.IsRunning.
        // Called from RefreshBlinkTrainerTab and after any local start/stop.
        // Also mirrors the label onto the Deeper-hub Start/Stop button so the
        // duplicated setup card stays consistent.
        private void RefreshBlinkTrainerTrackerButton()
        {
            bool running = App.Webcam?.IsRunning == true;
            var label = running ? "Stop tracker" : "Start tracker";
            if (BlinkTrainerTab.BtnBlinkTrainerStartStopTracker != null)
                BlinkTrainerTab.BtnBlinkTrainerStartStopTracker.Content = label;
            if (DeeperTab.BtnDeeperWebcamStartStopTracker != null)
                DeeperTab.BtnDeeperWebcamStartStopTracker.Content = label;
        }

        internal void BtnBlinkTrainerGateUnlock_Click(object sender, RoutedEventArgs e)
        {
            // Same path every other gated tab uses (Bambi / Haptics / etc.):
            // open the dashboard's App Info & Data popup where the Patreon
            // login lives.
            ShowAppInfoPopup();
        }

        /// <summary>
        /// Idempotent transition. Stops the loop/subscription owned by the
        /// outgoing mode and starts the incoming one. Both LivePreview and
        /// LiveSession use the same OnBlink subscription so transitioning
        /// between them is a no-op.
        /// </summary>
        private void ApplyBlinkTrainerStageMode(BlinkTrainerStageMode mode)
        {
            if (mode == _currentBlinkTrainerStageMode) return;

            bool wasLive = _currentBlinkTrainerStageMode != BlinkTrainerStageMode.Demo;
            bool nowLive = mode != BlinkTrainerStageMode.Demo;

            if (wasLive && !nowLive)
            {
                UnsubscribeBlinkTrainerLiveBlink();
                StartBlinkTrainerDemoLoop();
            }
            else if (!wasLive && nowLive)
            {
                StopBlinkTrainerDemoLoop();
                // Reset stage to a known state before live mode takes over so
                // the user doesn't see a stale demo asset in their first frame.
                ResetBlinkTrainerStageForLive();
                SubscribeBlinkTrainerLiveBlink();
            }

            _currentBlinkTrainerStageMode = mode;
            App.Logger?.Debug("BlinkTrainer stage mode -> {Mode}", mode);
        }

        private void ResetBlinkTrainerStageForLive()
        {
            try
            {
                // Park both images on the first live blink's "incoming" slot.
                if (BlinkTrainerTab.BlinkTrainerStageImageA != null)
                {
                    BlinkTrainerTab.BlinkTrainerStageImageA.BeginAnimation(UIElement.OpacityProperty, null);
                    BlinkTrainerTab.BlinkTrainerStageImageA.Opacity = 0;
                }
                if (BlinkTrainerTab.BlinkTrainerStageImageB != null)
                {
                    BlinkTrainerTab.BlinkTrainerStageImageB.BeginAnimation(UIElement.OpacityProperty, null);
                    BlinkTrainerTab.BlinkTrainerStageImageB.Opacity = 0;
                }
                if (BlinkTrainerTab.BlinkTrainerStageMedia != null)
                {
                    try { BlinkTrainerTab.BlinkTrainerStageMedia.Stop(); } catch { }
                    BlinkTrainerTab.BlinkTrainerStageMedia.Opacity = 0;
                }
                _blinkTrainerLiveLastPickedPath = null;
                _blinkTrainerDemoUsingA = true; // first live pick goes to A
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "ResetBlinkTrainerStageForLive failed"); }
        }

        private void SubscribeBlinkTrainerLiveBlink()
        {
            if (_blinkTrainerLiveSubscribed) return;
            if (App.Webcam == null) return;
            App.Webcam.OnBlink += OnBlinkTrainerStagePreviewBlink;
            _blinkTrainerLiveSubscribed = true;
        }

        private void UnsubscribeBlinkTrainerLiveBlink()
        {
            if (!_blinkTrainerLiveSubscribed) return;
            if (App.Webcam != null) App.Webcam.OnBlink -= OnBlinkTrainerStagePreviewBlink;
            _blinkTrainerLiveSubscribed = false;
        }

        /// <summary>
        /// Invalidate the cached live pool so the next OnBlink rebuilds. Called
        /// on folder add/remove and IncludeVideos toggle.
        /// </summary>
        private void InvalidateBlinkTrainerLivePool()
        {
            _blinkTrainerLivePool = null;
            _blinkTrainerLivePoolToken = "";
        }

        private BlinkTrainerAssetPool GetOrBuildBlinkTrainerLivePool()
        {
            var s = App.Settings?.Current;
            var folders = s?.BlinkTrainerFolders ?? new List<string>();
            bool includeVideos = s?.BlinkTrainerIncludeVideos == true;
            var token = string.Join("|", folders) + "::" + includeVideos;
            if (_blinkTrainerLivePool == null || _blinkTrainerLivePoolToken != token)
            {
                _blinkTrainerLivePool = BlinkTrainerAssetPool.Build(folders, includeVideos);
                _blinkTrainerLivePoolToken = token;
            }
            return _blinkTrainerLivePool;
        }

        /// <summary>
        /// Hard-cut swap on every real blink. App.Webcam.OnBlink already raises
        /// on the UI dispatcher (per WebcamTrackingService:1642), but Dispatcher
        /// check is cheap defense.
        /// </summary>
        private void OnBlinkTrainerStagePreviewBlink()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(OnBlinkTrainerStagePreviewBlink));
                return;
            }
            try
            {
                var pool = GetOrBuildBlinkTrainerLivePool();
                if (pool.IsEmpty) return;
                var path = pool.PickRandom(_blinkTrainerLiveLastPickedPath);
                if (string.IsNullOrEmpty(path)) return;
                _blinkTrainerLiveLastPickedPath = path;

                if (BlinkTrainerAssetPool.IsVideo(path))
                    ApplyBlinkTrainerLiveVideo(path);
                else
                    ApplyBlinkTrainerLiveImage(path);
            }
            catch (Exception ex)
            {
                // Don't crash; just skip this swap. Demo fallback is reserved
                // for non-configured state, not live errors.
                App.Logger?.Warning(ex, "OnBlinkTrainerStagePreviewBlink failed");
            }
        }

        private void ApplyBlinkTrainerLiveImage(string path)
        {
            if (BlinkTrainerTab.BlinkTrainerStageImageA == null || BlinkTrainerTab.BlinkTrainerStageImageB == null) return;

            // Stop any playing video first.
            if (BlinkTrainerTab.BlinkTrainerStageMedia != null)
            {
                try { BlinkTrainerTab.BlinkTrainerStageMedia.Stop(); } catch { }
                BlinkTrainerTab.BlinkTrainerStageMedia.Opacity = 0;
            }

            BitmapImage bmp;
            try
            {
                bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path);
                bmp.EndInit();
                bmp.Freeze();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "BlinkTrainer live image load failed for {Path}", path);
                return;
            }

            // Hard-cut swap — kill any in-flight animations from demo mode and
            // pin opacities directly. Toggle which Image control is "active".
            Image incoming = _blinkTrainerDemoUsingA ? BlinkTrainerTab.BlinkTrainerStageImageB : BlinkTrainerTab.BlinkTrainerStageImageA;
            Image outgoing = _blinkTrainerDemoUsingA ? BlinkTrainerTab.BlinkTrainerStageImageA : BlinkTrainerTab.BlinkTrainerStageImageB;

            incoming.BeginAnimation(UIElement.OpacityProperty, null);
            outgoing.BeginAnimation(UIElement.OpacityProperty, null);
            incoming.Source = bmp;
            incoming.Opacity = 1;
            outgoing.Opacity = 0;

            _blinkTrainerDemoUsingA = !_blinkTrainerDemoUsingA;
        }

        private void ApplyBlinkTrainerLiveVideo(string path)
        {
            if (BlinkTrainerTab.BlinkTrainerStageMedia == null) return;

            // Hide both images while the video is on top.
            if (BlinkTrainerTab.BlinkTrainerStageImageA != null)
            {
                BlinkTrainerTab.BlinkTrainerStageImageA.BeginAnimation(UIElement.OpacityProperty, null);
                BlinkTrainerTab.BlinkTrainerStageImageA.Opacity = 0;
            }
            if (BlinkTrainerTab.BlinkTrainerStageImageB != null)
            {
                BlinkTrainerTab.BlinkTrainerStageImageB.BeginAnimation(UIElement.OpacityProperty, null);
                BlinkTrainerTab.BlinkTrainerStageImageB.Opacity = 0;
            }

            try
            {
                BlinkTrainerTab.BlinkTrainerStageMedia.Stop();
                BlinkTrainerTab.BlinkTrainerStageMedia.Source = new Uri(path);
                BlinkTrainerTab.BlinkTrainerStageMedia.Opacity = 1;
                BlinkTrainerTab.BlinkTrainerStageMedia.Play();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "BlinkTrainer live video load failed for {Path}", path);
                BlinkTrainerTab.BlinkTrainerStageMedia.Opacity = 0;
            }
        }

        internal void BlinkTrainerStageMedia_MediaEnded(object sender, RoutedEventArgs e)
        {
            // Loop the preview video until the next blink swaps it.
            try
            {
                if (BlinkTrainerTab.BlinkTrainerStageMedia == null) return;
                BlinkTrainerTab.BlinkTrainerStageMedia.Position = TimeSpan.Zero;
                BlinkTrainerTab.BlinkTrainerStageMedia.Play();
            }
            catch { }
        }

        // ──────────────────────────────────────────────────────────────────
        // Status row state machine
        // ──────────────────────────────────────────────────────────────────

        private enum BlinkTrainerStatusState
        {
            IdleReady,
            Running,
            NeedsConsent,
            NeedsFolders,
            NeedsCalibration,
            Error,
        }

        private BlinkTrainerStatusState _currentBlinkTrainerStatusState = BlinkTrainerStatusState.IdleReady;
        private RoutedEventHandler? _blinkTrainerStatusActionHandler;

        private BlinkTrainerStatusState DetermineBlinkTrainerStatusState()
        {
            if (App.BlinkTrainer?.IsRunning == true)
                return BlinkTrainerStatusState.Running;

            // Service exposes LastError as a non-empty string after a failure.
            if (!string.IsNullOrEmpty(App.BlinkTrainer?.LastError))
                return BlinkTrainerStatusState.Error;

            if (!WebcamTrackingService.IsConsentCurrent())
                return BlinkTrainerStatusState.NeedsConsent;

            var folderCount = App.Settings?.Current?.BlinkTrainerFolders?.Count ?? 0;
            if (folderCount == 0)
                return BlinkTrainerStatusState.NeedsFolders;

            if (IsMultiMonitorEnvironment() && !HasUsableCalibration())
                return BlinkTrainerStatusState.NeedsCalibration;

            return BlinkTrainerStatusState.IdleReady;
        }

        private static bool IsMultiMonitorEnvironment()
        {
            try
            {
                var screens = System.Windows.Forms.Screen.AllScreens;
                return screens != null && screens.Length > 1;
            }
            catch { return false; }
        }

        private static bool HasUsableCalibration()
        {
            var cal = App.Webcam?.Calibration;
            if (cal == null) return false;
            // Pre-multimonitor saves had empty DeviceName — see the existing
            // recalibrate-multimonitor sticky toast at MainWindow.xaml.cs:1690.
            if (cal.MonitorBounds == null) return false;
            return !string.IsNullOrEmpty(cal.MonitorBounds.DeviceName);
        }

        private void RefreshBlinkTrainerStatusRow()
        {
            if (BlinkTrainerTab.BlinkTrainerStatusDot == null || BlinkTrainerTab.BlinkTrainerStatusText == null) return;

            var state = DetermineBlinkTrainerStatusState();
            if (state != _currentBlinkTrainerStatusState)
            {
                App.Logger?.Debug("BlinkTrainer status state -> {State}", state);
                _currentBlinkTrainerStatusState = state;
            }
            ApplyBlinkTrainerStatusState(state);
        }

        private void ApplyBlinkTrainerStatusState(BlinkTrainerStatusState state)
        {
            var pinkBrush = FindResource("PinkBrush") as Brush ?? Brushes.HotPink;
            Brush amber = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xD0, 0x80));
            Brush green = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4A, 0xDE, 0x80));
            Brush red = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xEF, 0x44, 0x44));
            amber.Freeze();
            green.Freeze();
            red.Freeze();

            // Stop any prior animation explicitly so dot opacity isn't stuck
            // mid-pulse when leaving IdleReady.
            BlinkTrainerTab.BlinkTrainerStatusDot.BeginAnimation(UIElement.OpacityProperty, null);
            BlinkTrainerTab.BlinkTrainerStatusDot.Opacity = 1;

            // FX (PR-4a): the dot breathes while a session is genuinely RUNNING, and only then.
            // It used to blink at 0.8s in IdleReady - an idle loop on a tab where, by definition,
            // nothing is happening - which the FX plan rules out for every status surface.
            SetBlinkTrainerStatusPulse(state == BlinkTrainerStatusState.Running);

            string startLabel = Localization.Loc.Get("blink_trainer_start_session");
            string stopLabel = Localization.Loc.Get("blink_trainer_stop_session");

            switch (state)
            {
                case BlinkTrainerStatusState.IdleReady:
                    BlinkTrainerTab.BlinkTrainerStatusDot.Fill = pinkBrush;
                    BlinkTrainerTab.BlinkTrainerStatusText.Text = Localization.Loc.Get("blink_trainer_status_ready");
                    BlinkTrainerTab.BlinkTrainerStatusText.Foreground = FindResource("TextMutedBrush") as Brush ?? Brushes.Gray;
                    WireBlinkTrainerStatusAction(null, null);
                    SetStartButtonState(enabled: true, content: startLabel);
                    break;

                case BlinkTrainerStatusState.Running:
                    BlinkTrainerTab.BlinkTrainerStatusDot.Fill = green;
                    // Initial text — BlinkTrainerTick takes over each second.
                    var rem = App.BlinkTrainer?.Remaining ?? TimeSpan.Zero;
                    BlinkTrainerTab.BlinkTrainerStatusText.Text = Localization.Loc.GetF("blink_trainer_status_running", rem.ToString(rem.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss"));
                    BlinkTrainerTab.BlinkTrainerStatusText.Foreground = FindResource("TextMutedBrush") as Brush ?? Brushes.Gray;
                    WireBlinkTrainerStatusAction(null, null);
                    SetStartButtonState(enabled: true, content: stopLabel);
                    break;

                case BlinkTrainerStatusState.NeedsConsent:
                    BlinkTrainerTab.BlinkTrainerStatusDot.Fill = amber;
                    BlinkTrainerTab.BlinkTrainerStatusText.Text = Localization.Loc.Get("blink_trainer_status_needs_consent");
                    BlinkTrainerTab.BlinkTrainerStatusText.Foreground = FindResource("TextMutedBrush") as Brush ?? Brushes.Gray;
                    WireBlinkTrainerStatusAction(
                        Localization.Loc.Get("blink_trainer_consent_grant"),
                        BlinkTrainerStatusAction_GrantConsent);
                    SetStartButtonState(enabled: false, content: startLabel);
                    break;

                case BlinkTrainerStatusState.NeedsFolders:
                    BlinkTrainerTab.BlinkTrainerStatusDot.Fill = amber;
                    BlinkTrainerTab.BlinkTrainerStatusText.Text = Localization.Loc.Get("blink_trainer_status_needs_folders");
                    BlinkTrainerTab.BlinkTrainerStatusText.Foreground = FindResource("TextMutedBrush") as Brush ?? Brushes.Gray;
                    WireBlinkTrainerStatusAction(
                        Localization.Loc.Get("blink_trainer_add_folder"),
                        BlinkTrainerStatusAction_AddFolder);
                    SetStartButtonState(enabled: false, content: startLabel);
                    break;

                case BlinkTrainerStatusState.NeedsCalibration:
                    BlinkTrainerTab.BlinkTrainerStatusDot.Fill = amber;
                    BlinkTrainerTab.BlinkTrainerStatusText.Text = Localization.Loc.Get("blink_trainer_status_needs_calibration");
                    BlinkTrainerTab.BlinkTrainerStatusText.Foreground = FindResource("TextMutedBrush") as Brush ?? Brushes.Gray;
                    WireBlinkTrainerStatusAction(
                        Localization.Loc.Get("blink_trainer_calibration_btn"),
                        BlinkTrainerStatusAction_Calibrate);
                    // Calibration is recommended for multi-monitor only; let
                    // the user start without it.
                    SetStartButtonState(enabled: true, content: startLabel);
                    break;

                case BlinkTrainerStatusState.Error:
                    BlinkTrainerTab.BlinkTrainerStatusDot.Fill = red;
                    // Service-supplied error text — passed through as-is. Service
                    // LastError strings are not currently localized; if/when they
                    // are, this branch picks up the loc'd value transparently.
                    BlinkTrainerTab.BlinkTrainerStatusText.Text = App.BlinkTrainer?.LastError ?? "";
                    BlinkTrainerTab.BlinkTrainerStatusText.Foreground = red;
                    WireBlinkTrainerStatusAction(null, null);
                    SetStartButtonState(enabled: true, content: startLabel);
                    break;
            }
        }

        private void SetStartButtonState(bool enabled, string content)
        {
            if (BlinkTrainerTab.BtnBlinkTrainerStartSession == null) return;
            BlinkTrainerTab.BtnBlinkTrainerStartSession.IsEnabled = enabled;
            BlinkTrainerTab.BtnBlinkTrainerStartSession.Content = content;
        }

        /// <summary>
        /// Single point of truth for the status action button's delegate. Unhooks
        /// any prior handler before wiring the new one, so handlers can't accumulate
        /// across state transitions.
        /// </summary>
        private void WireBlinkTrainerStatusAction(string? content, RoutedEventHandler? handler)
        {
            if (BlinkTrainerTab.BlinkTrainerStatusAction == null) return;

            if (_blinkTrainerStatusActionHandler != null)
                BlinkTrainerTab.BlinkTrainerStatusAction.Click -= _blinkTrainerStatusActionHandler;
            _blinkTrainerStatusActionHandler = handler;

            if (content != null && handler != null)
            {
                BlinkTrainerTab.BlinkTrainerStatusAction.Content = content;
                BlinkTrainerTab.BlinkTrainerStatusAction.Click += handler;
                BlinkTrainerTab.BlinkTrainerStatusAction.Visibility = Visibility.Visible;
            }
            else
            {
                BlinkTrainerTab.BlinkTrainerStatusAction.Visibility = Visibility.Collapsed;
            }
        }

        // The IdleReady blink (a 0.8s opacity Storyboard living in the dot's own Resources) was
        // retired by the FX pass - see ApplyBlinkTrainerStatusState. The dot now breathes a glow
        // while a session is RUNNING, from SetBlinkTrainerStatusPulse
        // (MainWindow.TabFxTakeoverLabStatus.cs), and is otherwise perfectly still.

        // Status action button handlers — routed via WireBlinkTrainerStatusAction
        // so only the current state's handler is wired at any time.

        private void BlinkTrainerStatusAction_GrantConsent(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new WebcamConsentDialog { Owner = this };
                dlg.ShowDialog();
                RefreshBlinkTrainerWebcamColumn();
                RefreshBlinkTrainerStatusRow();
                ApplyBlinkTrainerStageMode(DetermineBlinkTrainerStageMode());
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "BlinkTrainerStatusAction_GrantConsent failed"); }
        }

        private void BlinkTrainerStatusAction_AddFolder(object sender, RoutedEventArgs e)
        {
            // Reuse the same flow as the Asset Packs column's "+ Add folder" so
            // there's exactly one folder-pick path.
            BtnBlinkTrainerAddFolderCard_Click(sender, e);
            InvalidateBlinkTrainerLivePool();
            RefreshBlinkTrainerStatusRow();
            ApplyBlinkTrainerStageMode(DetermineBlinkTrainerStageMode());
        }

        private void BlinkTrainerStatusAction_Calibrate(object sender, RoutedEventArgs e)
        {
            try
            {
                WebcamCalibrationWindow.ShowDialogWithRecalibrate(this);
                RefreshBlinkTrainerWebcamColumn();
                RefreshBlinkTrainerStatusRow();
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "BlinkTrainerStatusAction_Calibrate failed"); }
        }

        #endregion

        #region Blink Trainer Tab — controls (Phase C)

        // ──────────────────────────────────────────────────────────────────
        // Column 1: Asset Packs
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Rebuilds the folder card stack from AppSettings.Current.BlinkTrainerFolders.
        /// Each card shows the folder's display name, an image/video count summary,
        /// and a × remove button. Card hover state is handled inline via a local
        /// Style trigger (no separate XAML resource).
        /// </summary>
        private void RebuildBlinkTrainerFolderCards()
        {
            try
            {
                if (BlinkTrainerTab.BlinkTrainerFolderCardsHost == null) return;
                BlinkTrainerTab.BlinkTrainerFolderCardsHost.Children.Clear();

                var settings = App.Settings?.Current;
                if (settings?.BlinkTrainerFolders == null) return;

                bool includeVideos = settings.BlinkTrainerIncludeVideos;
                foreach (var folder in settings.BlinkTrainerFolders.ToList())
                {
                    BlinkTrainerTab.BlinkTrainerFolderCardsHost.Children.Add(BuildBlinkTrainerFolderCard(folder, includeVideos));
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "RebuildBlinkTrainerFolderCards failed");
            }
        }

        private Border BuildBlinkTrainerFolderCard(string folder, bool includeVideos)
        {
            // Card border with hover-state border-brush swap via a local Style.
            // Background brush stays #11000000; the only thing that changes is
            // the border color (0.3 pink at rest, full pink on hover).
            var pinkColorObj = TryFindResource("PinkColor");
            var pinkColor = pinkColorObj is System.Windows.Media.Color c
                ? c
                : System.Windows.Media.Color.FromRgb(0xFF, 0x69, 0xB4);
            var restBorder = new SolidColorBrush(pinkColor) { Opacity = 0.3 };
            restBorder.Freeze();

            var style = new Style(typeof(Border));
            var hoverTrigger = new Trigger { Property = Border.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(
                Border.BorderBrushProperty,
                FindResource("PinkBrush")));
            style.Triggers.Add(hoverTrigger);

            var card = new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x11, 0, 0, 0)),
                BorderBrush = restBorder,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 8),
                Style = style,
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var info = new StackPanel();
            Grid.SetColumn(info, 0);

            // Display name: folder basename, falling back to parent if blank
            // (e.g. trailing-slash paths). Tooltip carries the full path.
            string displayName = "";
            try
            {
                displayName = System.IO.Path.GetFileName(folder);
                if (string.IsNullOrWhiteSpace(displayName))
                    displayName = new System.IO.DirectoryInfo(folder).Name;
            }
            catch { displayName = folder; }
            if (string.IsNullOrWhiteSpace(displayName)) displayName = folder;

            info.Children.Add(new TextBlock
            {
                Text = displayName,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Medium,
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = folder,
            });

            // Count summary via AssetPack.FromFolder. Null pack = invalid/empty.
            var pack = Lab.GazeMinigame.AssetPack.FromFolder(folder);
            string countLine;
            Brush countBrush = FindResource("TextMutedBrush") as Brush ?? Brushes.Gray;
            if (pack == null)
            {
                countLine = Localization.Loc.Get("blink_trainer_folder_empty_or_invalid");
                countBrush = FindResource("TextDimBrush") as Brush ?? countBrush;
            }
            else
            {
                int gifCount = pack.ImagePaths.Count(p =>
                    System.IO.Path.GetExtension(p).Equals(".gif", StringComparison.OrdinalIgnoreCase));
                int nonGifImages = pack.ImagePaths.Count - gifCount;

                if (includeVideos)
                {
                    countLine = $"{pack.ImagePaths.Count} images, {pack.VideoPaths.Count} videos";
                }
                else
                {
                    countLine = gifCount > 0
                        ? $"{nonGifImages} images, {gifCount} GIFs"
                        : $"{pack.ImagePaths.Count} images";
                }
            }
            info.Children.Add(new TextBlock
            {
                Text = countLine,
                Foreground = countBrush,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0),
            });

            grid.Children.Add(info);

            // × remove button. Tag carries the folder path so the handler can
            // disambiguate when multiple cards live in the same StackPanel.
            var removeBtn = new Button
            {
                Content = "×",
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = FindResource("TextMutedBrush") as Brush ?? Brushes.Gray,
                Padding = new Thickness(6, 0, 6, 0),
                FontSize = 16,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Top,
                Tag = folder,
            };
            removeBtn.Click += BtnBlinkTrainerRemoveFolderCard_Click;
            Grid.SetColumn(removeBtn, 1);
            grid.Children.Add(removeBtn);

            card.Child = grid;
            return card;
        }

        internal void BtnBlinkTrainerAddFolderCard_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using var dlg = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = "Pick a folder of images / GIFs for Blink Trainer",
                };
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

                var folder = dlg.SelectedPath;
                if (string.IsNullOrWhiteSpace(folder)) return;

                var settings = App.Settings?.Current;
                if (settings == null) return;

                if (settings.BlinkTrainerFolders.Any(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase)))
                    return;

                settings.BlinkTrainerFolders.Add(folder);
                App.Settings?.Save();
                RebuildBlinkTrainerFolderCards();
                InvalidateBlinkTrainerLivePool();
                RefreshBlinkTrainerStatusRow();
                ApplyBlinkTrainerStageMode(DetermineBlinkTrainerStageMode());
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "BtnBlinkTrainerAddFolderCard_Click failed");
            }
        }

        private void BtnBlinkTrainerRemoveFolderCard_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not Button btn) return;
                if (btn.Tag is not string folder) return;

                var settings = App.Settings?.Current;
                if (settings == null) return;

                settings.BlinkTrainerFolders.RemoveAll(f =>
                    string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));
                App.Settings?.Save();
                RebuildBlinkTrainerFolderCards();
                InvalidateBlinkTrainerLivePool();
                RefreshBlinkTrainerStatusRow();
                ApplyBlinkTrainerStageMode(DetermineBlinkTrainerStageMode());
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "BtnBlinkTrainerRemoveFolderCard_Click failed");
            }
        }

        internal void ToggleBlinkTrainerIncludeVideos_Changed(object sender, RoutedEventArgs e)
        {
            try
            {
                if (BlinkTrainerTab.ToggleBlinkTrainerIncludeVideos == null) return;
                var settings = App.Settings?.Current;
                if (settings == null) return;

                bool newValue = BlinkTrainerTab.ToggleBlinkTrainerIncludeVideos.IsChecked == true;
                if (settings.BlinkTrainerIncludeVideos == newValue) return;
                settings.BlinkTrainerIncludeVideos = newValue;
                App.Settings?.Save();

                // Rebuild cards so count summaries reflect the new mode, and
                // invalidate the live pool so the next blink picks from the
                // updated mix.
                RebuildBlinkTrainerFolderCards();
                InvalidateBlinkTrainerLivePool();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "ToggleBlinkTrainerIncludeVideos_Changed failed");
            }
        }

        // ──────────────────────────────────────────────────────────────────
        // Column 2: Session
        // ──────────────────────────────────────────────────────────────────

        internal void SliderBlinkTrainerDurationNew_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                int v = (int)Math.Round(e.NewValue);
                if (App.Settings?.Current is { } s) s.BlinkTrainerDurationMinutes = v;
                if (BlinkTrainerTab.TxtBlinkTrainerDurationValue != null)
                    BlinkTrainerTab.TxtBlinkTrainerDurationValue.Text = $"{v} min";
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "SliderBlinkTrainerDurationNew_Changed failed"); }
        }

        internal void SliderBlinkTrainerOpacityNew_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                int v = (int)Math.Round(e.NewValue);
                if (App.Settings?.Current is { } s) s.BlinkTrainerOpacity = v;
                if (BlinkTrainerTab.TxtBlinkTrainerOpacityValue != null)
                    BlinkTrainerTab.TxtBlinkTrainerOpacityValue.Text = $"{v}%";
                ApplyBlinkTrainerOpacityFillOpacity(v);
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "SliderBlinkTrainerOpacityNew_Changed failed"); }
        }

        // ── H.7: reactive opacity slider fill ──
        // The filled portion of the BlinkTrainerGradientSlider template is a
        // RepeatButton inside the Track named "PART_Track". On the slider's
        // Loaded event we walk the template once to cache that RepeatButton,
        // then ValueChanged sets its Opacity to map 1-100 -> 0.109-1.0. The
        // shared style means the Duration slider has the same RepeatButton,
        // but only the Opacity slider's Loaded handler caches a reference, so
        // only its fill fades.
        private RepeatButton? _blinkTrainerOpacityFillButton;

        internal void SliderBlinkTrainerOpacityNew_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not Slider slider) return;
                if (slider.Template?.FindName("PART_Track", slider) is not Track track) return;
                _blinkTrainerOpacityFillButton = track.DecreaseRepeatButton;
                ApplyBlinkTrainerOpacityFillOpacity((int)Math.Round(slider.Value));
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "SliderBlinkTrainerOpacityNew_Loaded failed"); }
        }

        private void ApplyBlinkTrainerOpacityFillOpacity(int sliderValue)
        {
            if (_blinkTrainerOpacityFillButton == null) return;
            // Linear map 1-100 -> ~0.109-1.0. Slope 0.9 over 99-unit range,
            // offset 0.1 so v=1 is still faintly visible.
            int v = Math.Clamp(sliderValue, 1, 100);
            _blinkTrainerOpacityFillButton.Opacity = v / 100.0 * 0.9 + 0.1;
        }

        // ── H.6: reactive value-label scale on slider drag ──
        // PreviewMouseLeftButtonDown fires on press anywhere in the slider area
        // (track or thumb). LostMouseCapture catches the release-outside-slider
        // case where MouseLeftButtonUp wouldn't fire on the slider itself.

        internal void BlinkTrainerSlider_DragStart(object sender, MouseButtonEventArgs e)
        {
            if (sender is Slider s) AnimateBlinkTrainerSliderLabel(s, scaleTo: 1.15, durationMs: 100, easeOut: true);
        }

        internal void BlinkTrainerSlider_DragEnd(object sender, MouseButtonEventArgs e)
        {
            if (sender is Slider s) AnimateBlinkTrainerSliderLabel(s, scaleTo: 1.0, durationMs: 150, easeOut: false);
        }

        internal void BlinkTrainerSlider_LostCapture(object sender, MouseEventArgs e)
        {
            if (sender is Slider s) AnimateBlinkTrainerSliderLabel(s, scaleTo: 1.0, durationMs: 150, easeOut: false);
        }

        private void AnimateBlinkTrainerSliderLabel(Slider slider, double scaleTo, int durationMs, bool easeOut)
        {
            TextBlock? label = null;
            if (slider == BlinkTrainerTab.SliderBlinkTrainerDurationNew) label = BlinkTrainerTab.TxtBlinkTrainerDurationValue;
            else if (slider == BlinkTrainerTab.SliderBlinkTrainerOpacityNew) label = BlinkTrainerTab.TxtBlinkTrainerOpacityValue;
            if (label?.RenderTransform is not ScaleTransform st) return;

            IEasingFunction ease = easeOut
                ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 }
                : new QuadraticEase { EasingMode = EasingMode.EaseOut };
            var anim = new DoubleAnimation(scaleTo, TimeSpan.FromMilliseconds(durationMs))
            {
                EasingFunction = ease,
            };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        }

        internal void BlinkTrainerMixOptionSame_Click(object sender, MouseButtonEventArgs e) => SetMixMode(false);
        internal void BlinkTrainerMixOptionMix_Click(object sender, MouseButtonEventArgs e) => SetMixMode(true);

        private void SetMixMode(bool isMix)
        {
            try
            {
                if (App.Settings?.Current is { } s)
                {
                    if (s.BlinkTrainerMixImages != isMix)
                    {
                        s.BlinkTrainerMixImages = isMix;
                        App.Settings?.Save();
                    }
                }
                SetMixModeSelection(isMix);
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "SetMixMode failed"); }
        }

        /// <summary>
        /// Paints the selected mix-mode option border with full pink + a pink
        /// drop-shadow glow, and clears the unselected one. Called on tab show
        /// and on every option click.
        /// </summary>
        private void SetMixModeSelection(bool isMix)
        {
            if (BlinkTrainerTab.BlinkTrainerMixOptionSame == null || BlinkTrainerTab.BlinkTrainerMixOptionMix == null) return;

            var pinkBrush = FindResource("PinkBrush") as Brush ?? Brushes.HotPink;
            var pinkColorObj = TryFindResource("PinkColor");
            var pinkColor = pinkColorObj is System.Windows.Media.Color c
                ? c
                : System.Windows.Media.Color.FromRgb(0xFF, 0x69, 0xB4);

            void Apply(Border b, bool selected)
            {
                if (selected)
                {
                    b.BorderBrush = pinkBrush;
                    b.Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = pinkColor,
                        BlurRadius = 16,
                        ShadowDepth = 0,
                        Opacity = 0.6,
                    };
                }
                else
                {
                    b.BorderBrush = Brushes.Transparent;
                    b.Effect = null;
                }
            }
            Apply(BlinkTrainerTab.BlinkTrainerMixOptionSame, !isMix);
            Apply(BlinkTrainerTab.BlinkTrainerMixOptionMix, isMix);
        }

        // ──────────────────────────────────────────────────────────────────
        // Column 3: Webcam
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Refreshes every Webcam-column control: device list, consent
        /// card tint + button text, calibration status line. Called on tab
        /// show and after any dialog return (consent / calibrate / quick recal).
        /// </summary>
        private void RefreshBlinkTrainerWebcamColumn()
        {
            try
            {
                // Shared populator (Cleanup 1) — also refreshes the Lab combo.
                PopulateWebcamDeviceCombos();

                // Consent
                bool consented = WebcamTrackingService.IsConsentCurrent();
                if (BlinkTrainerTab.BlinkTrainerConsentCard != null)
                {
                    if (consented)
                    {
                        // Green-tinted (granted)
                        BlinkTrainerTab.BlinkTrainerConsentCard.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x1A, 0x4A, 0xDE, 0x80));
                        BlinkTrainerTab.BlinkTrainerConsentCard.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4A, 0xDE, 0x80));
                    }
                    else
                    {
                        // Amber-tinted (required)
                        BlinkTrainerTab.BlinkTrainerConsentCard.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x1A, 0xFF, 0xD0, 0x80));
                        BlinkTrainerTab.BlinkTrainerConsentCard.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xD0, 0x80));
                    }
                }
                if (BlinkTrainerTab.BlinkTrainerConsentStatus != null)
                {
                    BlinkTrainerTab.BlinkTrainerConsentStatus.Text = Localization.Loc.Get(
                        consented ? "blink_trainer_consent_granted" : "blink_trainer_consent_required");
                }
                if (BlinkTrainerTab.BtnBlinkTrainerManageConsent != null)
                {
                    BlinkTrainerTab.BtnBlinkTrainerManageConsent.Content = Localization.Loc.Get(
                        consented ? "blink_trainer_consent_manage" : "blink_trainer_consent_grant");
                }
                if (BlinkTrainerTab.BtnBlinkTrainerRevokeConsent != null)
                {
                    BlinkTrainerTab.BtnBlinkTrainerRevokeConsent.Visibility = consented ? Visibility.Visible : Visibility.Collapsed;
                }

                // Calibration status line. All three branches are now loc'd;
                // the "Calibrated for {device}" line uses GetF with the device
                // name passed as the {0} substitution (still system-provided
                // text, but the surrounding wording is translatable).
                if (BlinkTrainerTab.BlinkTrainerCalibrationStatus != null)
                {
                    var cal = App.Webcam?.Calibration;
                    if (cal == null)
                    {
                        BlinkTrainerTab.BlinkTrainerCalibrationStatus.Text = Localization.Loc.Get("blink_trainer_calibration_none");
                    }
                    else if (cal.MonitorBounds != null && !string.IsNullOrEmpty(cal.MonitorBounds.DeviceName))
                    {
                        BlinkTrainerTab.BlinkTrainerCalibrationStatus.Text = Localization.Loc.GetF(
                            "blink_trainer_calibration_calibrated_format", cal.MonitorBounds.DeviceName);
                    }
                    else
                    {
                        // Pre-multimonitor calibration data — flagged by the
                        // existing recalibrate-multimonitor sticky toast.
                        BlinkTrainerTab.BlinkTrainerCalibrationStatus.Text = Localization.Loc.Get("blink_trainer_calibration_outdated");
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "RefreshBlinkTrainerWebcamColumn failed");
            }

            // Fan out to the Deeper hub's duplicate card (same state, same
            // service). Null-safe — Deeper card may not be loaded yet.
            RefreshDeeperWebcamColumn();
            // Tracker button label is shared state across both cards.
            RefreshBlinkTrainerTrackerButton();
        }

        // Phase 2 (UX restructure): the Blink Trainer's camera picker + refresh button and the
        // Deeper hub's camera/monitor pickers, refresh button and restrict-gaze checkbox were
        // duplicate editors of WebcamDeviceIndex / WebcamCalibrationScreen /
        // RestrictGazeContentToCalibratedScreen. Settings -> Devices owns all three now, so those
        // six handlers were deleted with their controls. Consent + calibration handlers below are
        // ACTIONS, not settings - several launchers for one dialog is fine, and keeping them here
        // is what lets a free user set the camera up without leaving the page they came for.

        internal void BtnBlinkTrainerManageConsent_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Same dialog in both directions — non-consenting users go through
                // the grant gates; already-consenting users see review-only copy.
                // Closing without explicit Enable leaves WebcamConsentGiven alone.
                var dlg = new WebcamConsentDialog { Owner = this };
                dlg.ShowDialog();
                RefreshBlinkTrainerWebcamColumn();
                RefreshBlinkTrainerStatusRow();
                ApplyBlinkTrainerStageMode(DetermineBlinkTrainerStageMode());
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "BtnBlinkTrainerManageConsent_Click failed");
            }
        }

        internal void BtnBlinkTrainerRevokeConsent_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = MessageBox.Show(
                    this,
                    Localization.Loc.Get("blink_trainer_consent_revoke_confirm_body"),
                    Localization.Loc.Get("blink_trainer_consent_revoke_confirm_title"),
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning,
                    MessageBoxResult.Cancel);
                if (result != MessageBoxResult.OK) return;

                App.Webcam?.RevokeConsent();
                if (AppSettingsTab.ChkWebcamDebugCursor != null) AppSettingsTab.ChkWebcamDebugCursor.IsChecked = false;

                RefreshBlinkTrainerWebcamColumn();
                RefreshBlinkTrainerStatusRow();
                ApplyBlinkTrainerStageMode(DetermineBlinkTrainerStageMode());
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "BtnBlinkTrainerRevokeConsent_Click failed");
            }
        }

        internal void BtnBlinkTrainerCalibrate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                WebcamCalibrationWindow.ShowDialogWithRecalibrate(this);
                RefreshBlinkTrainerWebcamColumn();
                RefreshBlinkTrainerStatusRow();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "BtnBlinkTrainerCalibrate_Click failed");
            }
        }

        internal async void BtnBlinkTrainerQuickRecal_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var svc = App.Webcam;
                if (svc == null) return;
                if (!WebcamTrackingService.IsConsentCurrent())
                {
                    var consent = new WebcamConsentDialog { Owner = this };
                    if (consent.ShowDialog() != true || !consent.ConsentGiven)
                    {
                        RefreshBlinkTrainerWebcamColumn();
                        return;
                    }
                }
                if (svc.Calibration == null)
                {
                    System.Windows.MessageBox.Show(this,
                        Localization.Loc.Get("blink_trainer_quick_recal_needs_full_body"),
                        Localization.Loc.Get("blink_trainer_quick_recal_needs_full_title"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                bool startedHere = false;
                if (!svc.IsRunning)
                {
                    // Off the UI thread so the camera/ONNX load doesn't freeze
                    // the window; the loading splash shows during the wait.
                    if (await svc.StartAsync()) startedHere = true;
                }

                var recalDlg = new WebcamQuickRecalWindow { Owner = this };
                App.ApplyCalibrationScreenPlacement(recalDlg);
                recalDlg.ShowDialog();

                if (startedHere) svc.Stop();
                RefreshBlinkTrainerWebcamColumn();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "BtnBlinkTrainerQuickRecal_Click failed");
            }
        }

        // ──────────────────────────────────────────────────────────────
        // Deeper hub webcam setup card. The card is a 1:1 visual copy of
        // the Blink Trainer setup card so non-Patreon users can grant
        // consent + calibrate without bumping into the Blink Trainer gate.
        // State is shared (AppSettings + App.Webcam), so handlers here
        // either delegate to the Blink Trainer handler (consent dialogs,
        // calibration windows) or replicate the device/monitor combo logic
        // with the same _webcamDevicePopulating / _webcamMonitorPopulating
        // guards. RefreshBlinkTrainerWebcamColumn fans out to the Deeper
        // card via RefreshDeeperWebcamColumn so any state change updates
        // both surfaces.
        // ──────────────────────────────────────────────────────────────

        internal void BtnDeeperWebcamManageConsent_Click(object sender, RoutedEventArgs e)
            => BtnBlinkTrainerManageConsent_Click(sender, e);

        internal void BtnDeeperWebcamRevokeConsent_Click(object sender, RoutedEventArgs e)
            => BtnBlinkTrainerRevokeConsent_Click(sender, e);

        internal void BtnDeeperWebcamCalibrate_Click(object sender, RoutedEventArgs e)
            => BtnBlinkTrainerCalibrate_Click(sender, e);

        internal void BtnDeeperWebcamQuickRecal_Click(object sender, RoutedEventArgs e)
            => BtnBlinkTrainerQuickRecal_Click(sender, e);

        internal void BtnDeeperWebcamStartStopTracker_Click(object sender, RoutedEventArgs e)
            => BtnBlinkTrainerStartStopTracker_Click(sender, e);

        /// <summary>
        /// Mirrors RefreshBlinkTrainerWebcamColumn for the Deeper hub's setup
        /// card. Called from inside RefreshBlinkTrainerWebcamColumn so every
        /// existing consent/calibration trigger fans out here automatically.
        /// All element accesses are null-guarded — the Deeper hub UI may not
        /// be loaded yet on the first refresh (e.g. before the tab is shown).
        /// </summary>
        private void RefreshDeeperWebcamColumn()
        {
            try
            {
                if (App.Webcam == null) return;
                var consented = WebcamTrackingService.IsConsentCurrent();

                if (DeeperTab.DeeperWebcamConsentCard != null)
                {
                    if (consented)
                    {
                        DeeperTab.DeeperWebcamConsentCard.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x1A, 0x4A, 0xDE, 0x80));
                        DeeperTab.DeeperWebcamConsentCard.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4A, 0xDE, 0x80));
                    }
                    else
                    {
                        DeeperTab.DeeperWebcamConsentCard.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x1A, 0xFF, 0xD0, 0x80));
                        DeeperTab.DeeperWebcamConsentCard.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xD0, 0x80));
                    }
                }
                if (DeeperTab.DeeperWebcamConsentStatus != null)
                {
                    DeeperTab.DeeperWebcamConsentStatus.Text = Localization.Loc.Get(
                        consented ? "blink_trainer_consent_granted" : "blink_trainer_consent_required");
                }
                if (DeeperTab.BtnDeeperWebcamManageConsent != null)
                {
                    DeeperTab.BtnDeeperWebcamManageConsent.Content = Localization.Loc.Get(
                        consented ? "blink_trainer_consent_manage" : "blink_trainer_consent_grant");
                }
                if (DeeperTab.BtnDeeperWebcamRevokeConsent != null)
                {
                    DeeperTab.BtnDeeperWebcamRevokeConsent.Visibility = consented ? Visibility.Visible : Visibility.Collapsed;
                }

                if (DeeperTab.DeeperWebcamCalibrationStatus != null)
                {
                    var cal = App.Webcam.Calibration;
                    if (cal == null)
                    {
                        DeeperTab.DeeperWebcamCalibrationStatus.Text = Localization.Loc.Get("blink_trainer_calibration_none");
                    }
                    else if (cal.MonitorBounds != null && !string.IsNullOrEmpty(cal.MonitorBounds.DeviceName))
                    {
                        DeeperTab.DeeperWebcamCalibrationStatus.Text = Localization.Loc.GetF(
                            "blink_trainer_calibration_calibrated_format", cal.MonitorBounds.DeviceName);
                    }
                    else
                    {
                        DeeperTab.DeeperWebcamCalibrationStatus.Text = Localization.Loc.Get("blink_trainer_calibration_outdated");
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "RefreshDeeperWebcamColumn failed");
            }
        }

        #endregion
    }
}
