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
    // Deeper tab: browser hub, webcam integration, and catalogue flows.
    public partial class MainWindow
    {
        #region Deeper Tab

        private void BtnDeeper_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("deeper");
            if (App.Settings?.Current is { } s && !s.HasSeenDeeperTab)
            {
                s.HasSeenDeeperTab = true;
                StopDeeperTabPulse();
                App.Settings?.Save();
            }
            UpdateDeeperWelcomeCardVisibility();
            // Mission 2: lazy-init the hub the first time the user opens the
            // tab, then on every show pull a fresh scan. Cheap; doesn't churn
            // if the library hasn't changed.
            InitializeDeeperHub();
            ReloadDeeperLibraryFromDisk();
        }

        private void UpdateDeeperWelcomeCardVisibility()
        {
            if (DeeperTab.DeeperWelcomeCard == null) return;
            var seen = App.Settings?.Current?.HasSeenDeeperWelcome ?? true;
            DeeperTab.DeeperWelcomeCard.Visibility = seen ? Visibility.Collapsed : Visibility.Visible;
        }

        private void DismissDeeperWelcomeCard()
        {
            if (App.Settings?.Current is { } s && !s.HasSeenDeeperWelcome)
            {
                s.HasSeenDeeperWelcome = true;
                App.Settings?.Save();
            }
            if (DeeperTab.DeeperWelcomeCard != null) DeeperTab.DeeperWelcomeCard.Visibility = Visibility.Collapsed;
        }

        internal void BtnDeeperWelcomeTour_Click(object sender, RoutedEventArgs e)
        {
            try { App.Bark?.NotifyUiAction("deeper_tour"); } catch { }
            DismissDeeperWelcomeCard();
            StartDeeperTabTutorial();
        }

        internal void BtnDeeperWelcomeDemo_Click(object sender, RoutedEventArgs e)
        {
            DismissDeeperWelcomeCard();
            OpenDeeperBundledDemo();
        }

        internal void BtnDeeperWelcomeDismiss_Click(object sender, RoutedEventArgs e)
        {
            DismissDeeperWelcomeCard();
        }

        internal void BtnDeeperTutorial_Click(object sender, RoutedEventArgs e)
        {
            StartDeeperTabTutorial();
        }

        // The bundled "Welcome to Deeper" demo is seeded into the user's library
        // on first run. Match by the literal filename rather than a hardcoded
        // path so we follow the user's library folder if they moved it.
        private void OpenDeeperBundledDemo()
        {
            try
            {
                var lib = App.EnhancementLibrary;
                if (lib == null) return;
                var match = lib.ScanLibrary()
                    .FirstOrDefault(e =>
                        string.Equals(System.IO.Path.GetFileName(e.FilePath), "welcome.ccpenh.json",
                            StringComparison.OrdinalIgnoreCase));
                if (match == null)
                {
                    MessageBox.Show(this,
                        "The bundled demo couldn't be found in your library — try restarting the app.",
                        "Deeper", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                OpenDeeperFile(match.FilePath);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Open bundled Deeper demo failed: {Error}", ex.Message);
            }
        }

        private void StartDeeperTabTutorial()
        {
            ShowTab("deeper");
            UpdateDeeperWelcomeCardVisibility(); // keep the card consistent with state
            StartTutorial(TutorialType.Deeper);
        }

        internal void ChkEnableDeeper_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var enabled = SettingsTab.ChkEnableDeeper.IsChecked ?? true;
            if (App.Settings?.Current is { } s) s.EnableDeeper = enabled;
            if (BtnDeeper != null) BtnDeeper.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            // If the user just disabled Deeper while it's the active tab, fall back to Settings.
            if (!enabled && DeeperTab?.Visibility == Visibility.Visible) ShowTab("settings");
            App.Settings?.Save();
        }

        private bool _deeperPulseRunning;

        private void StartDeeperTabPulse()
        {
            if (BtnDeeperScale == null || _deeperPulseRunning) return;
            _deeperPulseRunning = true;
            var anim = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 1.0,
                To = 1.12,
                Duration = TimeSpan.FromMilliseconds(700),
                AutoReverse = true,
                RepeatBehavior = new System.Windows.Media.Animation.RepeatBehavior(4),
                EasingFunction = new System.Windows.Media.Animation.SineEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut
                }
            };
            anim.Completed += (_, _) =>
            {
                _deeperPulseRunning = false;
                if (BtnDeeperScale != null)
                {
                    BtnDeeperScale.ScaleX = 1.0;
                    BtnDeeperScale.ScaleY = 1.0;
                }
            };
            BtnDeeperScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, anim);
            BtnDeeperScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, anim);
        }

        private void StopDeeperTabPulse()
        {
            if (!_deeperPulseRunning && BtnDeeperScale == null) return;
            _deeperPulseRunning = false;
            if (BtnDeeperScale != null)
            {
                BtnDeeperScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
                BtnDeeperScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);
                BtnDeeperScale.ScaleX = 1.0;
                BtnDeeperScale.ScaleY = 1.0;
            }
        }

        internal void BtnDeeperNewEnhancement_Click(object sender, RoutedEventArgs e)
        {
            try { App.Bark?.NotifyUiAction("deeper_new"); } catch { }
            var dialog = new Views.Deeper.NewEnhancementDialog { Owner = this };
            if (dialog.ShowDialog() != true) return;

            var enhancement = App.EnhancementLibrary?.CreateBlank(dialog.SelectedMediaType, dialog.SelectedSource);
            if (enhancement == null) return;

            OpenDeeperEditor(enhancement, null);
        }

        internal void BtnDeeperOpenPlayer_Click(object sender, RoutedEventArgs e)
        {
            try { App.Bark?.NotifyUiAction("deeper_player"); } catch { }
            try
            {
                var win = new Views.Deeper.EnhancementPlayerWindow(App.DeeperPlayer, App.DeeperHost) { Owner = this };
                win.Show();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to open Deeper player");
                MessageBox.Show(this,
                    $"Couldn't open Deeper Player:\n\n{ex.GetType().Name}: {ex.Message}",
                    "Open Player failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnDeeperBrowserBound(string pageUrl, Models.Deeper.Enhancement enhancement)
        {
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    SettingsTab.DeeperBrowserBadge.Visibility = Visibility.Visible;
                    var name = string.IsNullOrEmpty(enhancement.Metadata?.Name) ? "(untitled)" : enhancement.Metadata!.Name;
                    SettingsTab.TxtDeeperBrowserBadge.Text = $"🌊 {name}";
                    SettingsTab.DeeperBrowserBadge.Tag = $"{name}\n{pageUrl}";

                    // QoL: if the bound enhancement uses webcam-driven rules and
                    // tracking isn't already running, ask the user once whether
                    // they want to turn it on. Mirrors the player's behavior so
                    // gaze/blink/attention rules actually fire in the browser.
                    MaybePromptBrowserWebcamForEnhancement(enhancement);
                }
                catch { }
            });
        }

        private void OnDeeperBrowserUnbound()
        {
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    SettingsTab.DeeperBrowserBadge.Visibility = Visibility.Collapsed;
                    SettingsTab.DeeperBrowserBadge.Tag = null;
                    _browserWebcamPromptShownForUrl = null;
                }
                catch { }
            });
        }

        // ────────────────────────────────────────────────────────────────────
        // Browser Webcam Tracking toggle (button above the embedded WebView2)
        // ────────────────────────────────────────────────────────────────────

        private bool _browserWebcamStateSubscribed;
        private Action<WebcamTrackingState>? _onBrowserWebcamStateChanged;
        // Tracks the page URL we've already prompted about so reload-binds
        // don't badger the user repeatedly for the same enhancement.
        private string? _browserWebcamPromptShownForUrl;

        private void EnsureBrowserWebcamStateSubscribed()
        {
            if (_browserWebcamStateSubscribed || App.Webcam == null) return;
            _browserWebcamStateSubscribed = true;
            _onBrowserWebcamStateChanged = _ => Dispatcher.BeginInvoke(RefreshBrowserWebcamButton);
            App.Webcam.OnTrackingStateChanged += _onBrowserWebcamStateChanged;
            RefreshBrowserWebcamButton();
        }

        private void RefreshBrowserWebcamButton()
        {
            try
            {
                if (SettingsTab.BtnWebcamTracking == null) return;
                var on = App.Webcam?.IsRunning == true;
                if (SettingsTab.TxtWebcamTracking != null)
                    SettingsTab.TxtWebcamTracking.Text = Loc.Get(on
                        ? "btn_browser_webcam_tracking_on"
                        : "btn_browser_webcam_tracking_off");
                SettingsTab.BtnWebcamTracking.ToolTip = Loc.Get(on
                    ? "tooltip_browser_webcam_tracking_on"
                    : "tooltip_browser_webcam_tracking_off");
            }
            catch { }
        }

        internal async void BtnWebcamTracking_Click(object sender, RoutedEventArgs e)
        {
            try { App.Bark?.NotifyUiAction("webcam_tracking"); } catch { }
            var svc = App.Webcam;
            if (svc == null) return;

            if (svc.IsRunning)
            {
                svc.Stop();
                RefreshBrowserWebcamButton();
                return;
            }

            // Consent gate. If declined or cancelled, bail silently — user can
            // try again from this same button or from the Lab tab later.
            if (!WebcamTrackingService.IsConsentCurrent())
            {
                var consent = new WebcamConsentDialog { Owner = this };
                var ok = consent.ShowDialog();
                if (ok != true || !consent.ConsentGiven) return;
            }

            // Needs-calibration path: the calibration window reads OnRawIris,
            // which only fires while the tracker is running. So we start the
            // camera FIRST, then open calibration on top of the live stream.
            // Tell the user up front so the camera light surprising them
            // doesn't feel like the app did something it shouldn't have.
            bool needsCalibration = svc.Calibration == null;
            if (needsCalibration)
            {
                var confirm = MessageBox.Show(this,
                    Loc.Get("browser_webcam_calibrate_prompt_body"),
                    Loc.Get("browser_webcam_calibrate_prompt_title"),
                    MessageBoxButton.OKCancel, MessageBoxImage.Information);
                if (confirm != MessageBoxResult.OK) return;
            }

            // Start off the UI thread — Start() does VideoCapture open + ONNX
            // session ctors and can block 10-30s on slow USB negotiation.
            bool started;
            try
            {
                started = await Task.Run(() => svc.Start());
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Browser webcam toggle: Start() threw");
                started = false;
            }
            RefreshBrowserWebcamButton();

            if (!started)
            {
                App.Logger?.Warning("Browser webcam toggle: Start() returned false, state={State}", svc.State);
                return;
            }

            // Now the tracker is live — run the 16-point calibration. If the
            // user cancels we stop the tracker again so the camera light
            // doesn't stay on for a feature they backed out of.
            if (needsCalibration)
            {
                var calibrated = WebcamCalibrationWindow.ShowDialogWithRecalibrate(this);
                if (calibrated != true)
                {
                    svc.Stop();
                    RefreshBrowserWebcamButton();
                }
            }
        }

        // Webcam-needing enhancement check. Delegates to the shared
        // EnhancementCapabilities.NeedsWebcam so the browser hub, the mandatory-
        // video engine-start nudge, and the Deeper player all answer identically
        // (AutoTags first, then a trigger scan across both the unified timeline
        // and the legacy Rules collection).
        private static bool BrowserEnhancementNeedsWebcam(Models.Deeper.Enhancement enh)
            => Services.Deeper.EnhancementCapabilities.NeedsWebcam(enh);

        private void MaybePromptBrowserWebcamForEnhancement(Models.Deeper.Enhancement enhancement)
        {
            try
            {
                if (enhancement == null) return;
                if (!BrowserEnhancementNeedsWebcam(enhancement)) return;
                var svc = App.Webcam;
                if (svc == null || svc.IsRunning) return;

                // Dedupe per-page so reload/dom-mutation doesn't re-pop the dialog.
                var url = App.DeeperBrowserDiscovery?.ActiveUrl ?? "";
                if (!string.IsNullOrEmpty(_browserWebcamPromptShownForUrl) &&
                    string.Equals(_browserWebcamPromptShownForUrl, url, StringComparison.OrdinalIgnoreCase))
                    return;
                _browserWebcamPromptShownForUrl = url;

                var result = MessageBox.Show(this,
                    Loc.Get("browser_webcam_prompt_body"),
                    Loc.Get("browser_webcam_prompt_title"),
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes) return;

                // Reuse the toggle button's flow so consent + calibration
                // gating is identical to the manual-click path.
                BtnWebcamTracking_Click(this, new RoutedEventArgs());
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("MaybePromptBrowserWebcamForEnhancement: {Error}", ex.Message);
            }
        }

        // Set once the engine-start enhancement nudge has been shown this launch,
        // so repeated Start/Stop cycles don't re-pop it (once per launch; a "Not
        // now" is remembered until the app restarts — webcam isn't auto-started,
        // so a fresh launch legitimately re-asks).
        private bool _mandatoryVideoEnhanceNudgeShown;

        /// <summary>
        /// Engine-start nudge: if the mandatory / asset video folder contains an
        /// enhanced video the current settings won't fully honour, offer to flip
        /// the missing switch(es) in one combined dialog:
        ///   • VideoEnhanceIfPossible is off but enhanced videos exist → enable it.
        ///   • An enhancement needs the webcam tracker but it isn't running → start
        ///     it, routing through the same consent/calibration flow as the manual
        ///     toggle.
        /// Scans the folder off the UI thread (cached + short-circuited) and
        /// prompts at most once per launch. No-op unless mandatory videos are on.
        /// </summary>
        private async void MaybePromptMandatoryVideoEnhancement()
        {
            try
            {
                if (_mandatoryVideoEnhanceNudgeShown) return;

                var settings = App.Settings?.Current;
                if (settings == null || !settings.MandatoryVideosEnabled) return;

                // Don't interrupt remote-controlled or locked-down sessions.
                if (App.RemoteControl?.ControllerConnected == true) return;
                if (App.Lockdown?.IsActive == true) return;

                var folder = System.IO.Path.Combine(App.EffectiveAssetsPath, "videos");

                Services.Deeper.MandatoryVideoEnhancementScanner.ScanResult scan;
                try
                {
                    scan = await Task.Run(() => Services.Deeper.MandatoryVideoEnhancementScanner.Scan(folder));
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("MaybePromptMandatoryVideoEnhancement: scan failed: {Error}", ex.Message);
                    return;
                }

                // The window/engine may have gone away during the async scan.
                if (!IsLoaded || Dispatcher.HasShutdownStarted) return;
                if (!_isRunning) return;            // engine stopped before scan returned
                if (_mandatoryVideoEnhanceNudgeShown) return; // a concurrent start won the race

                if (!scan.AnyEnhanced) return;      // nothing to nudge about

                var enhanceOff = !settings.VideoEnhanceIfPossible;
                var webcamSvc = App.Webcam;
                var webcamGap = scan.AnyWebcamEnhanced && (webcamSvc == null || !webcamSvc.IsRunning);

                // No gap → no prompt (enhancement already on and either no webcam
                // rules or the webcam is already tracking).
                if (!enhanceOff && !webcamGap) return;

                _mandatoryVideoEnhanceNudgeShown = true;

                var sb = new System.Text.StringBuilder();
                sb.Append("Some videos in your mandatory video folder have enhancements ");
                sb.Append("(synced flashes, haptics, overlays and more).\n\n");
                if (enhanceOff)
                    sb.Append("• Video enhancement is currently turned OFF, so they won't play.\n");
                if (webcamGap)
                    sb.Append("• Some use webcam tracking (gaze / blink), but the webcam engine isn't running.\n");
                sb.Append("\nWould you like to turn ");
                sb.Append(enhanceOff && webcamGap ? "these on now?"
                          : enhanceOff ? "enhancement on now?"
                          : "the webcam on now?");

                var yes = enhanceOff && webcamGap ? "Yes, set it up"
                          : enhanceOff ? "Yes, enable enhancement"
                          : "Yes, turn on webcam";

                // Never stack on top of the update popup: both are modal on the UI
                // thread, and whichever ShowDialog nests second owns input while the
                // Topmost update dialog covers it - neither can be dismissed (#481).
                // Wait for the update dialog, then take the startup-dialog flag so
                // the update path's own wait loop defers to us symmetrically.
                for (int i = 0; i < 60 && App.IsUpdateDialogActive; i++)
                    await Task.Delay(500);
                if (App.IsUpdateDialogActive) return;
                if (!IsLoaded || Dispatcher.HasShutdownStarted || !_isRunning) return;

                bool confirmed;
                IsStartupDialogShowing = true;
                try
                {
                    confirmed = ShowStyledDialog("✨ Enhanced videos detected", sb.ToString(), yes, "Not now");
                }
                finally
                {
                    IsStartupDialogShowing = false;
                }
                if (!confirmed) return;

                if (enhanceOff)
                {
                    settings.VideoEnhanceIfPossible = true;
                    App.Settings?.Save();
                    App.Logger?.Information("Mandatory-video enhancement enabled via engine-start nudge.");
                }

                if (webcamGap)
                {
                    // Reuse the manual toggle's flow so consent + calibration
                    // gating is identical to clicking the webcam button.
                    BtnWebcamTracking_Click(this, new RoutedEventArgs());
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("MaybePromptMandatoryVideoEnhancement: {Error}", ex.Message);
            }
        }

        internal void ToggleEnhanceIfPossible_Changed(object sender, RoutedEventArgs e)
        {
            try
            {
                var newValue = SettingsTab.ToggleEnhanceIfPossible?.IsChecked == true;
                if (App.Settings?.Current != null)
                {
                    App.Settings.Current.BrowserEnhanceIfPossible = newValue;
                    App.Settings.Save();
                }
                App.BrowserEnhanceBridge?.Refresh();

                // If just turned off, status text needs an immediate reset since
                // Refresh() will fire MatchChanged(null) but we want to be explicit.
                if (!newValue && SettingsTab.TxtEnhanceMatchStatus != null)
                    SettingsTab.TxtEnhanceMatchStatus.Text = Loc.Get("browser_enhance_match_off");
            }
            catch (Exception ex) { App.Logger?.Debug("ToggleEnhanceIfPossible_Changed: {Error}", ex.Message); }
        }

        /// <summary>
        /// User override (#596): reveal the BambiCloud site toggle on mods that hide it.
        /// Persists to settings and immediately re-applies the site-toggle visibility.
        /// </summary>
        internal void ChkForceShowBambiCloud_Changed(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_isLoading) return;
                var newValue = SettingsTab.ChkForceShowBambiCloud?.IsChecked == true;
                if (App.Settings?.Current != null)
                {
                    App.Settings.Current.ForceShowBambiCloud = newValue;
                    App.Settings.Save();
                }

                // Re-apply the site-toggle visibility for the current mod without
                // switching the active mod. Mirror InitializeModSelector's logic.
                var modWantsBambiCloud = App.Mods?.ShowBambiCloudOption() ?? true;
                var showBambiCloud = modWantsBambiCloud || newValue;
                SettingsTab.RbBambiCloud.Visibility = showBambiCloud ? Visibility.Visible : Visibility.Collapsed;
                if (!modWantsBambiCloud && !newValue)
                    SettingsTab.RbHypnoTube.IsChecked = true;

                RefreshBrowserLoadingText();
            }
            catch (Exception ex) { App.Logger?.Debug("ChkForceShowBambiCloud_Changed: {Error}", ex.Message); }
        }

        private void OnBrowserEnhanceMatchChanged(Services.Deeper.EnhancementLibraryEntry? match)
        {
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    if (SettingsTab.TxtEnhanceMatchStatus == null) return;
                    if (App.Settings?.Current?.BrowserEnhanceIfPossible == false)
                    {
                        SettingsTab.TxtEnhanceMatchStatus.Text = Loc.Get("browser_enhance_match_off");
                        return;
                    }
                    if (match == null)
                    {
                        SettingsTab.TxtEnhanceMatchStatus.Text = Loc.Get("browser_enhance_match_none");
                        return;
                    }
                    var name = string.IsNullOrEmpty(match.Name) ? "(untitled)" : match.Name;
                    SettingsTab.TxtEnhanceMatchStatus.Text = string.Format(Loc.Get("browser_enhance_match_fmt"), name);
                }
                catch { }
            });
        }

        private void OpenDeeperEditor(Models.Deeper.Enhancement enhancement, string? filePath)
        {
            try
            {
                var window = new Views.Deeper.DeeperEditorWindow(enhancement, filePath) { Owner = this };
                window.Closed += (_, _) => RefreshDeeperLibraryUI();
                window.Show();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to open Deeper editor");
                MessageBox.Show(this, ex.Message, "Deeper", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ---- Public entry points for "Open with CCP" + drag-drop dispatch ----

        public void OpenInDeeperPlayer(string mediaPath)
        {
            try
            {
                var win = new Views.Deeper.EnhancementPlayerWindow(App.DeeperPlayer, App.DeeperHost) { Owner = this };
                win.Show();
                win.OpenLocalMediaFile(mediaPath);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to open Deeper player for {Path}", mediaPath);
                MessageBox.Show(this,
                    $"Couldn't open Deeper Player:\n\n{ex.GetType().Name}: {ex.Message}",
                    "Open Player failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Opens the Player and loads a Deeper enhancement JSON. The host fires
        // its Loaded event which routes to OnHostLoaded → UpdateHostUi → the
        // correct media loader (remote URL / local video / audio) based on the
        // enhancement's MediaType + MediaSource. Used by the hub row's ▶
        // button — distinct from OpenInDeeperPlayer (which takes a media path).
        // Mission 3: opens the editor for an existing .ccpenh.json file. Used
        // by the Player's "Open in editor" jump (header button + event log
        // link). Routes through OpenDeeperFile so the same load/error path
        // and library-refresh-on-close behavior applies as the hub row click.
        public void OpenDeeperEditorFromPlayer(string ccpenhJsonPath)
        {
            if (string.IsNullOrWhiteSpace(ccpenhJsonPath)) return;
            OpenDeeperFile(ccpenhJsonPath);
        }

        public void OpenDeeperEnhancementInPlayer(string ccpenhJsonPath)
        {
            try
            {
                var win = new Views.Deeper.EnhancementPlayerWindow(App.DeeperPlayer, App.DeeperHost) { Owner = this };
                win.Show();
                win.LoadEnhancementFile(ccpenhJsonPath);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to open Deeper player for enhancement {Path}", ccpenhJsonPath);
                MessageBox.Show(this,
                    $"Couldn't open Deeper Player:\n\n{ex.GetType().Name}: {ex.Message}",
                    "Open Player failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void OpenInDeeperEditorForMedia(string mediaPath)
        {
            try
            {
                var ext = Path.GetExtension(mediaPath);
                var mediaType = AssetVideoExtensions.Contains(ext)
                    ? Models.Deeper.MediaTypes.Video
                    : Models.Deeper.MediaTypes.Audio;
                var enhancement = App.EnhancementLibrary?.CreateBlank(mediaType, mediaPath);
                if (enhancement == null) return;
                OpenDeeperEditor(enhancement, null);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to open Deeper editor for {Path}", mediaPath);
                MessageBox.Show(this,
                    $"Couldn't open Deeper Editor:\n\n{ex.GetType().Name}: {ex.Message}",
                    "Open Editor failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void HandlePendingFileOpen(string action, string path)
        {
            if (string.IsNullOrEmpty(action) || string.IsNullOrEmpty(path)) return;
            if (action == "play") OpenInDeeperPlayer(path);
            else if (action == "edit") OpenInDeeperEditorForMedia(path);
        }

        private void OnDeeperLibraryChanged(object? sender, EventArgs e)
        {
            // Library change events arrive on the dispatcher thread already
            // (EnhancementLibrary marshals via Application.Current.Dispatcher),
            // but only refresh if the tab is actually visible — a hidden tab
            // would just throw the work away on the next ShowTab.
            try
            {
                if (DeeperTab?.Visibility == Visibility.Visible)
                    RefreshDeeperLibraryUI();
            }
            catch (Exception ex) { App.Logger?.Debug("Deeper library refresh error: {Error}", ex.Message); }
        }

        // Mission 2 (hub redesign): the old StackPanel-rebuild was replaced by
        // the ObservableCollection + DataTemplate path in MainWindow.DeeperHub.cs.
        // Kept as a stable hook so the ~5 call sites (OpenDeeperEditor's Closed
        // handler, DeleteDeeperLibraryEntry, OnDeeperLibraryChanged, etc.) don't
        // need touching.
        private void RefreshDeeperLibraryUI() => ReloadDeeperLibraryFromDisk();

        // Mission 2: BuildDeeperLibraryRow / BuildDeeperRecentRow / BuildDeeperAutoTagsRow
        // / BuildDeeperMediaLine deleted with the two-column Library + Recent grid.
        // Their visual roles moved into the DeeperLibraryRowTemplate DataTemplate
        // in MainWindow.xaml (Mission 2 commit). VM construction lives in
        // MainWindow.DeeperHub.cs:BuildRowVm + ResolveMediaSourceDisplay.

        private void OpenDeeperFile(string path)
        {
            try
            {
                var enhancement = App.EnhancementLibrary?.Open(path);
                if (enhancement == null) return;
                OpenDeeperEditor(enhancement, path);
            }
            catch (Services.Deeper.EnhancementLoadException ex)
            {
                MessageBox.Show(this, ex.Message, "Deeper", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to open Deeper file {Path}", path);
                MessageBox.Show(this, ex.Message, "Deeper", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        internal void BtnDeeperOpenLibraryFolder_Click(object sender, RoutedEventArgs e)
        {
            var folder = App.EnhancementLibrary?.LibraryFolder;
            if (string.IsNullOrEmpty(folder)) return;
            try
            {
                System.IO.Directory.CreateDirectory(folder);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to open Deeper library folder {Folder}", folder);
                MessageBox.Show(this, ex.Message, "Deeper", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        internal void BtnDeeperImport_Click(object sender, RoutedEventArgs e)
        {
            try { App.Bark?.NotifyUiAction("deeper_import"); } catch { }
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import enhancement",
                Filter = "Deeper enhancements (*.ccpenh.json)|*.ccpenh.json|JSON files (*.json)|*.json|All files (*.*)|*.*",
                DefaultExt = ".ccpenh.json",
                Multiselect = true,
                CheckFileExists = true,
                InitialDirectory = App.EnhancementLibrary?.LastDirectory
            };
            if (dlg.ShowDialog(this) != true) return;
            ImportEnhancementFiles(dlg.FileNames);
        }

        // NOTE: Deeper-tab drag-and-drop is intentionally handled by the window-wide
        // Window_Drop / DetectDropType system (DropType.Enhancement → ImportEnhancementFiles)
        // rather than a tab-local handler. A tab-local AllowDrop handler swallowed the
        // bubbling drag events the global drop overlay relies on, so it only covered
        // part of the tab. Keeping one global handler makes the entire window — and thus
        // the whole Deeper tab — a uniform drop target.

        private static bool IsImportableEnhancementPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            // Accept either the canonical "*.ccpenh.json" double-suffix or a plain
            // ".json" — the serializer will reject anything that doesn't carry the
            // expected $schema tag, so plain .json is safe to offer.
            return path.EndsWith(".ccpenh.json", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        }

        private void ImportEnhancementFiles(System.Collections.Generic.IEnumerable<string> paths)
        {
            var lib = App.EnhancementLibrary;
            if (lib == null)
            {
                MessageBox.Show(this, "Enhancement library isn't ready yet.", "Import failed",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var imported = new System.Collections.Generic.List<string>();
            var errors = new System.Collections.Generic.List<string>();
            string? lastImportedPath = null;

            foreach (var path in paths)
            {
                if (!IsImportableEnhancementPath(path))
                {
                    errors.Add($"{System.IO.Path.GetFileName(path)} — not a .ccpenh.json file.");
                    continue;
                }
                try
                {
                    // Validate by loading; bad schema / oversized files throw with
                    // a useful message from the serializer.
                    var enhancement = Services.Deeper.EnhancementSerializer.LoadFromFile(path);
                    var saved = lib.PromoteToLibrary(enhancement, sourceTag: "import");
                    if (saved == null)
                    {
                        errors.Add($"{System.IO.Path.GetFileName(path)} — couldn't write into the library folder.");
                        continue;
                    }
                    lastImportedPath = saved;
                    imported.Add(System.IO.Path.GetFileName(saved));
                }
                catch (Services.Deeper.EnhancementLoadException ex)
                {
                    errors.Add($"{System.IO.Path.GetFileName(path)} — {ex.Message}");
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "ImportEnhancementFiles: failed on {Path}", path);
                    errors.Add($"{System.IO.Path.GetFileName(path)} — {ex.GetType().Name}: {ex.Message}");
                }
            }

            // Remember the source folder for next manual import so the file dialog
            // opens where the user picked from.
            if (lastImportedPath != null && App.Settings?.Current != null)
            {
                var src = paths.FirstOrDefault();
                if (!string.IsNullOrEmpty(src))
                {
                    var dir = System.IO.Path.GetDirectoryName(src);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        App.Settings.Current.DeeperLastDirectory = dir;
                        App.Settings.Save();
                    }
                }
            }

            // Force-refresh the hub list now in addition to the FileSystemWatcher
            // signal, since the watcher's debounce can lag a fast manual import.
            try { RefreshDeeperLibraryUI(); } catch { }

            if (errors.Count == 0 && imported.Count > 0)
            {
                var msg = imported.Count == 1
                    ? $"Imported \"{imported[0]}\" into your library."
                    : $"Imported {imported.Count} enhancements into your library.";
                MessageBox.Show(this, msg, "Import complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else if (errors.Count > 0)
            {
                var head = imported.Count > 0
                    ? $"Imported {imported.Count}, but {errors.Count} failed:\n\n"
                    : $"{errors.Count} file(s) couldn't be imported:\n\n";
                MessageBox.Show(this, head + string.Join("\n", errors), "Import failed",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void DeleteDeeperLibraryEntry(Services.Deeper.EnhancementLibraryEntry entry)
        {
            var label = string.IsNullOrEmpty(entry.Name) ? System.IO.Path.GetFileName(entry.FilePath) : entry.Name;
            var msg = string.Format(Loc.Get("deeper_library_delete_confirm_fmt"), label);
            var result = MessageBox.Show(this, msg, Loc.Get("deeper_library_delete_title"),
                MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (result != MessageBoxResult.OK) return;
            try
            {
                if (System.IO.File.Exists(entry.FilePath))
                    System.IO.File.Delete(entry.FilePath);
                // FileSystemWatcher in EnhancementLibrary will fire LibraryChanged
                // and refresh the UI, but force an immediate refresh for snappiness.
                RefreshDeeperLibraryUI();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to delete Deeper library entry {Path}", entry.FilePath);
                MessageBox.Show(this, ex.Message, "Deeper", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // W3 Piece 1 — invoked from BrowserService.NavigationCompleted on every
        // page load in the embedded browser. Filters non-HT URLs out, debounces
        // rapid navigations via a per-window CTS, and surfaces the result as a
        // toast (or no toast at all when there are no results / lookup failed).
        //
        // Runs entirely off the WebView2 navigation thread context because the
        // caller already marshals through Dispatcher.Invoke before calling us,
        // but the lookup itself awaits on the thread pool so we don't block UI
        // during the network call.
        private void TriggerCatalogueLookupForNavigation(string url)
        {
            try
            {
                if (App.CatalogueLookup == null) return;
                // Cheap pre-filter — saves the cost of starting a Task for the
                // (very common) case of a non-HT navigation. The service does
                // the same check defensively.
                if (!Helpers.HtUrlHelper.IsEligibleHtUrl(url)) return;

                // Cancel any in-flight lookup from the previous navigation so a
                // delayed response doesn't surface a toast for a page the user
                // has already left.
                try { _catalogueLookupCts?.Cancel(); }
                catch { /* idempotent */ }
                _catalogueLookupCts?.Dispose();
                var cts = new System.Threading.CancellationTokenSource();
                _catalogueLookupCts = cts;

                _ = RunCatalogueLookupAsync(url, cts.Token);
            }
            catch (Exception ex)
            {
                // Defensive — must never propagate out of a navigation event handler.
                App.Logger?.Warning(ex, "[Catalogue] TriggerCatalogueLookupForNavigation threw");
            }
        }

        private async System.Threading.Tasks.Task RunCatalogueLookupAsync(string url, System.Threading.CancellationToken ct)
        {
            LookupResult result;
            try
            {
                result = await App.CatalogueLookup!.LookupForUrlAsync(url, ct).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return; // user navigated away; nothing to do
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[Catalogue] Lookup threw unexpectedly");
                return;
            }

            // Drop the result silently if a newer navigation has since started
            // — protects against the (small) race window between the lookup
            // returning and the CTS getting cancelled.
            if (ct.IsCancellationRequested) return;

            switch (result)
            {
                case LookupResult.Success s:
                    ShowCatalogueLookupToast(url, s.Entries);
                    break;
                case LookupResult.None:
                case LookupResult.InvalidUrl:
                case LookupResult.NetworkError:
                    // No user-visible feedback on these — silent by design.
                    break;
            }
        }

        // Surface a toast for {N} discovered enhancements. Updates
        // _currentCatalogueHtVideoId so the action handler can validate the
        // user is still on the same video when they click.
        private void ShowCatalogueLookupToast(string url, System.Collections.Generic.List<CatalogueEntry> entries)
        {
            if (entries == null || entries.Count == 0) return;

            var videoId = Helpers.HtUrlHelper.TryExtractHtVideoId(url);
            _currentCatalogueHtVideoId = videoId;

            string message;
            string actionLabel;
            if (entries.Count == 1)
            {
                message = Loc.Get("catalogue_lookup_toast_one");
                actionLabel = Loc.Get("catalogue_lookup_action_use_one");
            }
            else
            {
                message = string.Format(Loc.Get("catalogue_lookup_toast_many_fmt"), entries.Count);
                actionLabel = Loc.Get("catalogue_lookup_action_pick_one");
            }

            // Snapshot the entries + video ID for the action closure so a later
            // mutation of _currentCatalogueHtVideoId by a parallel navigation
            // can be detected.
            var snapshotEntries = entries;
            var snapshotVideoId = videoId;

            App.Notifications?.Show(message, NotificationType.Info, TimeSpan.FromSeconds(10),
                actionLabel,
                () =>
                {
                    // Stale-toast guard: the user navigated away before clicking.
                    // Silently bail — they'll see a fresh toast for whatever
                    // video they're now on (if any).
                    if (!string.Equals(_currentCatalogueHtVideoId, snapshotVideoId, StringComparison.Ordinal))
                    {
                        App.Logger?.Information("[Catalogue] Toast action ignored (user navigated away)");
                        return;
                    }

                    if (snapshotEntries.Count == 1)
                    {
                        _ = DownloadAndOpenCatalogueEntryAsync(snapshotEntries[0]);
                    }
                    else
                    {
                        OpenCataloguePickerDialog(snapshotEntries, snapshotVideoId);
                    }
                });
        }

        private void OpenCataloguePickerDialog(System.Collections.Generic.List<CatalogueEntry> entries, string? videoId)
        {
            try
            {
                var dlg = new CataloguePickerDialog(entries, videoId) { Owner = this };
                dlg.ShowDialog();
                if (dlg.SelectedEntry != null)
                {
                    _ = DownloadAndOpenCatalogueEntryAsync(dlg.SelectedEntry);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[Catalogue] Picker dialog threw");
            }
        }

        private async System.Threading.Tasks.Task DownloadAndOpenCatalogueEntryAsync(CatalogueEntry entry)
        {
            DownloadResult result;
            try
            {
                // Pass default cancellation here — the per-navigation CTS is
                // about lookups, not downloads. Once the user has clicked
                // through, they expect the download to complete even if they
                // navigate the browser away while it's in flight.
                result = await App.CatalogueLookup!.DownloadAndOpenAsync(entry, default).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[Catalogue] Download flow threw");
                App.Notifications?.Show(Loc.Get("catalogue_lookup_toast_download_failed"),
                    NotificationType.Error, TimeSpan.FromSeconds(8));
                return;
            }

            switch (result)
            {
                case DownloadResult.Success s:
                    App.Notifications?.Show(
                        string.Format(Loc.Get("catalogue_lookup_toast_loaded_fmt"), entry.Title),
                        NotificationType.Info, TimeSpan.FromSeconds(6));
                    break;
                case DownloadResult.NetworkError:
                    App.Notifications?.Show(Loc.Get("catalogue_lookup_toast_download_failed"),
                        NotificationType.Error, TimeSpan.FromSeconds(8));
                    break;
                case DownloadResult.InvalidFile:
                    App.Notifications?.Show(Loc.Get("catalogue_lookup_toast_invalid_file"),
                        NotificationType.Error, TimeSpan.FromSeconds(8));
                    break;
                case DownloadResult.SaveError:
                    App.Notifications?.Show(Loc.Get("catalogue_lookup_toast_save_failed"),
                        NotificationType.Error, TimeSpan.FromSeconds(8));
                    break;
                case DownloadResult.OpenError oe:
                    App.Notifications?.Show(
                        string.Format(Loc.Get("catalogue_lookup_toast_open_failed_fmt"), oe.LocalFilename),
                        NotificationType.Warning, TimeSpan.FromSeconds(10),
                        Loc.Get("catalogue_lookup_action_open_library"),
                        () => SwitchToDeeperLibraryTab());
                    break;
            }
        }

        // Focus the Deeper Library tab when the user clicks "Open Library"
        // from the OpenError recovery toast.
        private void SwitchToDeeperLibraryTab()
        {
            try { ShowTab("deeper"); }
            catch (Exception ex) { App.Logger?.Debug("[Catalogue] SwitchToDeeperLibraryTab failed: {Msg}", ex.Message); }
        }

        // Catalogue eligibility check — wraps the URL helper with the media-type
        // gate (audio enhancements aren't catalogued in W2).
        //
        // URL eligibility itself lives in Helpers/HtUrlHelper.cs because it's
        // now shared by three callers:
        //   1. This W2 row-level submit gate
        //   2. The catalogue server (kept in sync via cclabs-web's enhancements.ts)
        //   3. W3 Piece 1's CatalogueLookupService navigation hook
        // The two client consumers MUST agree on what counts as an HT URL —
        // see HtUrlHelper for the shared regex pair and update both this client
        // helper AND the server's normalizeHtUrl together when adding patterns.
        private static bool IsCatalogueEligible(Services.Deeper.EnhancementLibraryEntry entry)
        {
            if (entry == null) return false;
            if (entry.MediaType != Models.Deeper.MediaTypes.Video) return false;
            return Helpers.HtUrlHelper.IsEligibleHtUrl(entry.MediaSource);
        }

        // W2 — submit a library enhancement to the cclabs catalogue. Opens the
        // affirmation modal; on confirmation, awaits CatalogueService and maps
        // the SubmissionResult to a NotificationService toast per the spec.
        private async Task SubmitDeeperLibraryEntryAsync(Services.Deeper.EnhancementLibraryEntry entry)
        {
            // Defense in depth — the button is already disabled when auth is
            // missing, but a race (token expiring between row build and click)
            // would otherwise produce an AuthFailed toast as the first feedback.
            if (string.IsNullOrEmpty(App.Settings?.Current?.AuthToken))
            {
                App.Notifications?.Show(Loc.Get("catalogue_toast_auth_failed"),
                    Services.NotificationType.Warning, TimeSpan.FromSeconds(8));
                return;
            }

            var label = string.IsNullOrEmpty(entry.Name) ? System.IO.Path.GetFileName(entry.FilePath) : entry.Name;
            var dialog = new CatalogueSubmitDialog(label) { Owner = this };
            if (dialog.ShowDialog() != true || !dialog.Confirmed) return;

            SubmissionResult result;
            try
            {
                result = await App.Catalogue.SubmitEnhancementAsync(entry.FilePath, default).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                // CatalogueService is designed to never throw, but defensively
                // surface anything that escapes as an UnknownError.
                App.Logger?.Warning(ex, "[Catalogue] Submit threw unexpectedly");
                result = new SubmissionResult.UnknownError(0, ex.Message);
            }

            // Remember the submission so the library badge + the eventual
            // "published" notification can track it (no-op for non-ack results).
            RecordDeeperSubmission(entry.FilePath, result);

            ShowCatalogueSubmissionResultToast(result);
        }

        // Map a SubmissionResult to a localized toast. Kept separate from the
        // submit method so tests / future flows (e.g. a "retry all failed"
        // button) can reuse the mapping.
        private void ShowCatalogueSubmissionResultToast(SubmissionResult result)
        {
            switch (result)
            {
                case SubmissionResult.Success:
                    // Success uses the distinct green border (#4CAF50) so the
                    // happy-path outcome is visually unambiguous next to the
                    // pink Info border that Duplicate uses.
                    App.Notifications?.Show(Loc.Get("catalogue_toast_success"),
                        Services.NotificationType.Success, TimeSpan.FromSeconds(6));
                    break;

                case SubmissionResult.Duplicate d:
                {
                    var key = d.ExistingStatus switch
                    {
                        "approved" => "catalogue_toast_duplicate_approved",
                        "rejected" => "catalogue_toast_duplicate_rejected",
                        _ => "catalogue_toast_duplicate_pending",
                    };
                    App.Notifications?.Show(Loc.Get(key),
                        Services.NotificationType.Info, TimeSpan.FromSeconds(6));
                    break;
                }

                case SubmissionResult.ValidationError v:
                {
                    var key = v.ErrorCode switch
                    {
                        "missing_title" => "catalogue_toast_error_missing_title",
                        "missing_creator" => "catalogue_toast_error_missing_creator",
                        "invalid_media_source" => "catalogue_toast_error_invalid_media_source",
                        "invalid_schema" => "catalogue_toast_error_invalid_schema",
                        "file_too_large" => "catalogue_toast_error_file_too_large",
                        "stale_guidelines_version" => "catalogue_toast_error_stale_guidelines",
                        _ => "",
                    };
                    var msg = !string.IsNullOrEmpty(key)
                        ? Loc.Get(key)
                        : Loc.GetF("catalogue_toast_error_generic_fmt", v.ErrorCode);
                    App.Notifications?.Show(msg, Services.NotificationType.Warning, TimeSpan.FromSeconds(8));
                    break;
                }

                case SubmissionResult.AuthFailed:
                    // CatalogueService.MapResponse already invalidated the
                    // cache for us; the user just needs to re-link in Settings.
                    App.Notifications?.Show(Loc.Get("catalogue_toast_auth_failed"),
                        Services.NotificationType.Warning, TimeSpan.FromSeconds(10));
                    break;

                case SubmissionResult.TooLarge:
                    App.Notifications?.Show(Loc.Get("catalogue_toast_too_large"),
                        Services.NotificationType.Error, TimeSpan.FromSeconds(8));
                    break;

                case SubmissionResult.RateLimited r:
                {
                    string msg;
                    if (r.RetryAfterSeconds.HasValue && r.RetryAfterSeconds.Value > 0)
                    {
                        var minutes = Math.Max(1, (int)Math.Ceiling(r.RetryAfterSeconds.Value / 60.0));
                        msg = Loc.GetF("catalogue_toast_rate_limited_minutes_fmt", minutes);
                    }
                    else
                    {
                        msg = Loc.Get("catalogue_toast_rate_limited_unknown");
                    }
                    App.Notifications?.Show(msg, Services.NotificationType.Warning, TimeSpan.FromSeconds(10));
                    break;
                }

                case SubmissionResult.UnknownError u:
                    App.Logger?.Warning("[Catalogue] Submission UnknownError status={Status} body={Body}",
                        u.StatusCode, u.Body);
                    App.Notifications?.Show(Loc.Get("catalogue_toast_unknown_error"),
                        Services.NotificationType.Error, TimeSpan.FromSeconds(8));
                    break;
            }
        }
        #endregion
    }
}
