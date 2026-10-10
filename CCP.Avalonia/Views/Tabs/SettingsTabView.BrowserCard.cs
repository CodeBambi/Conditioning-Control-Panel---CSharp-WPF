// PORTED from WPF 7.1.5, the Home browser card's header row (ledger P6 / H6):
//   BtnMuteBrowser_Click + SyncBrowserMuteIcon   MainWindow.Browser.cs :2629, :2639
//   BtnWebcamTracking_Click + its button refresh  MainWindow.DeeperTab.cs :245
//   ChkForceShowBambiCloud_Changed                MainWindow.DeeperTab.cs :505
// Mute rides the WebHost script seam (Views/Controls/WebHostMedia.cs) and is re-applied after every
// navigation, since a new document has no flag.
// not ported: "Enhance if possible" (WPF BrowserEnhanceBridge matches the playing video against the
// Deeper library; there is no bridge on this head) and Pop out (WPF re-parents the WebView2 into its
// own window with a fullscreen rig; NativeWebView cannot be re-parented live). Both are greyed with
// the "not on this build" line instead of sitting there dead.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class SettingsTabView
    {
        private bool _browserCardSyncing;

        /// <summary>Tests: how many times the mute script was sent to the page.</summary>
        internal int BrowserMuteApplies { get; private set; }

        /// <summary>Tests: stands in for the tracker (running?, toggle).</summary>
        internal Func<bool>? WebcamRunningOverride { get; set; }

        private void HookBrowserCard()
        {
            BrowserWebHost.NavigationCompleted += _ => ApplyBrowserMute();
            AttachedToVisualTree += (_, _) =>
            {
                Platform.WebcamTracker.Instance.StateChanged -= OnBrowserCardTrackerChanged;
                Platform.WebcamTracker.Instance.StateChanged += OnBrowserCardTrackerChanged;
                CoreMods.ModChanged -= OnBrowserCardModChanged;
                CoreMods.ModChanged += OnBrowserCardModChanged;
                SyncBrowserCard();
            };
            DetachedFromVisualTree += (_, _) =>
            {
                Platform.WebcamTracker.Instance.StateChanged -= OnBrowserCardTrackerChanged;
                CoreMods.ModChanged -= OnBrowserCardModChanged;
            };

            // No host for these two on this head: greyed with the reason, never a dead click.
            foreach (Control c in new Control[] { ToggleEnhanceIfPossible, BtnPopOutBrowser })
            {
                c.IsEnabled = false;
                c.Bind(ToolTip.TipProperty, new Binding("[exclusives_not_on_this_build]")
                    { Source = LocalizationManager.Instance, Mode = BindingMode.OneWay });
                ToolTip.SetShowOnDisabled(c, true);
            }
            SyncBrowserCard();
        }

        private void OnBrowserCardTrackerChanged() => Dispatcher.UIThread.Post(RefreshBrowserWebcamButton);
        private void OnBrowserCardModChanged(object? sender, ModPackage? mod) => Dispatcher.UIThread.Post(SyncBrowserCard);

        /// <summary>The saved state into the row: the mute glyph, the webcam pill, the BambiCloud
        /// override box and the radio it reveals.</summary>
        internal void SyncBrowserCard()
        {
            try
            {
                _browserCardSyncing = true;
                try { ChkForceShowBambiCloud.IsChecked = CoreSettings.Current.ForceShowBambiCloud; }
                finally { _browserCardSyncing = false; }
                SyncBrowserMuteIcon();
                RefreshBrowserWebcamButton();
                ApplyBambiCloudVisibility(navigateIfHidden: false);
            }
            catch (Exception ex) { Log.Debug("SyncBrowserCard: {E}", ex.Message); }
        }

        // ------------------------------------------------------------------ mute

        /// <summary>WPF BtnMuteBrowser_Click: flip the saved flag, apply it live, repaint the glyph.</summary>
        private void BtnMuteBrowser_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var s = CoreSettings.Current;
                s.BrowserVideoMuted = !s.BrowserVideoMuted;
                CoreSettings.Save();
                ApplyBrowserMute();
                SyncBrowserMuteIcon();
            }
            catch (Exception ex) { Log.Debug("BtnMuteBrowser_Click: {E}", ex.Message); }
        }

        /// <summary>Sends the saved mute state to the live page (WPF: BrowserService re-applies
        /// IsAudioMuted on init). Fire and forget: the page answers on its own thread.</summary>
        internal void ApplyBrowserMute()
        {
            try
            {
                BrowserMuteApplies++;
                _ = BrowserWebHost.SetMutedAsync(CoreSettings.Current.BrowserVideoMuted || Shell?.BrowserPaused == true);
            }
            catch (Exception ex) { Log.Debug("ApplyBrowserMute: {E}", ex.Message); }
        }

        /// <summary>WPF SyncBrowserMuteIcon: the header glyph matches the saved preference.</summary>
        internal void SyncBrowserMuteIcon() =>
            TxtBrowserMute.Text = CoreSettings.Current.BrowserVideoMuted ? "🔇" : "🔊";

        // ------------------------------------------------------------------ webcam

        /// <summary>WPF BtnWebcamTracking_Click: stop if running; else the consent gate (declined =
        /// nothing happens), then start off the UI thread. The pill follows the tracker's own state.</summary>
        private async void BtnWebcamTracking_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                try { CoreBark.NotifyUiAction("webcam_tracking"); } catch (Exception ex) { Log.Debug("webcam bark: {E}", ex.Message); }
                var tracker = Platform.WebcamTracker.Instance;
                if (tracker.IsRunning)
                {
                    await tracker.StopAsync();
                    RefreshBrowserWebcamButton();
                    return;
                }
                if (!Services.Webcam.WebcamConsent.IsCurrent(CoreSettings.Current))
                {
                    if (TopLevel.GetTopLevel(this) is not Window owner) return;
                    await new WebcamConsentDialog().ShowDialogSafe(owner);
                    if (!Services.Webcam.WebcamConsent.IsCurrent(CoreSettings.Current)) return;
                }
                BtnWebcamTracking.IsEnabled = false;
                bool started;
                try { started = await tracker.StartAsync(); }
                finally { BtnWebcamTracking.IsEnabled = true; }
                if (!started) Log.Information("Browser webcam button: tracker did not start ({E})", tracker.LastError);
                RefreshBrowserWebcamButton();
            }
            catch (Exception ex) { Log.Warning(ex, "Browser webcam button failed"); }
        }

        /// <summary>The pill's words and tooltip for the tracker's state, bound so a language switch follows.</summary>
        internal void RefreshBrowserWebcamButton()
        {
            try
            {
                bool on = WebcamRunningOverride?.Invoke() ?? Platform.WebcamTracker.Instance.IsRunning;
                TxtWebcamTracking.Bind(TextBlock.TextProperty, new Binding($"[{(on ? "btn_browser_webcam_tracking_on" : "btn_browser_webcam_tracking_off")}]")
                    { Source = LocalizationManager.Instance, Mode = BindingMode.OneWay });
                BtnWebcamTracking.Bind(ToolTip.TipProperty, new Binding($"[{(on ? "tooltip_browser_webcam_tracking_on" : "tooltip_browser_webcam_tracking_off")}]")
                    { Source = LocalizationManager.Instance, Mode = BindingMode.OneWay });
            }
            catch (Exception ex) { Log.Debug("RefreshBrowserWebcamButton: {E}", ex.Message); }
        }

        // ------------------------------------------------------------------ BambiCloud override

        /// <summary>WPF ChkForceShowBambiCloud_Changed (#596): reveal the BambiCloud site toggle on
        /// mods that hide it. Saves, then re-applies the toggle's visibility for the active mod.</summary>
        private void ChkForceShowBambiCloud_Changed(object? sender, RoutedEventArgs e)
        {
            if (_browserCardSyncing) return;
            try
            {
                CoreSettings.Current.ForceShowBambiCloud = ChkForceShowBambiCloud.IsChecked == true;
                CoreSettings.Save();
                ApplyBambiCloudVisibility(navigateIfHidden: true);
            }
            catch (Exception ex) { Log.Debug("ChkForceShowBambiCloud_Changed: {E}", ex.Message); }
        }

        /// <summary>The rule: the radio shows when the mod wants it OR the user forces it.</summary>
        internal static bool BambiCloudShown(bool modWants, bool forced) => modWants || forced;

        /// <summary>Hides or shows the BambiCloud radio. When it hides while selected, the dot moves to
        /// HypnoTube and, on a user change, the page follows (WPF SyncSiteRadiosToActiveMod with
        /// navigateIfChanged): a radio left on a page whose button was just hidden is the old bug.</summary>
        private void ApplyBambiCloudVisibility(bool navigateIfHidden)
        {
            bool modWants = true;
            try { modWants = App.Mods?.ShowBambiCloudOption() ?? true; }
            catch (Exception ex) { Log.Debug("ShowBambiCloudOption: {E}", ex.Message); }
            bool show = BambiCloudShown(modWants, CoreSettings.Current.ForceShowBambiCloud);
            RbBambiCloud.IsVisible = show;
            if (show || RbBambiCloud.IsChecked != true) return;
            RbHypnoTube.IsChecked = true;
            if (navigateIfHidden) Shell?.BrowserSiteToggle_Click(RbHypnoTube, new RoutedEventArgs());
        }

        private void BtnPopOutBrowser_Click(object? sender, RoutedEventArgs e) { }          // greyed: see the header
        private void ToggleEnhanceIfPossible_Changed(object? sender, RoutedEventArgs e) { } // greyed: see the header
    }
}
