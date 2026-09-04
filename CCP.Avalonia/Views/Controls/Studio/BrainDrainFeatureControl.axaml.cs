using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Studio
{
    /// <summary>
    /// Brain Drain panel, ported from the WPF head.
    ///
    /// What is real here: both slider read-outs, the clip-library readout's format string and the
    /// empty-state banner it drives.
    ///
    /// What is not, and WHY - the settings are the interesting case. BrainDrainEnabled, Intensity,
    /// HighRefresh, BlurStrength, MeltEnabled and AllowOverlayCapture are all on
    /// <c>CoreSettings.Current</c> today, so "needs App.Settings" would be a stale reading. They
    /// stay unwired here on purpose: on WPF each of these writes is followed by a call into
    /// App.BrainDrain (Start/Stop, ReloadAudioFiles, the overlay-capture re-arm), so a control
    /// that wrote the setting alone would show BRAIN DRAIN as ON with nothing draining. The
    /// genuinely absent pieces are App.BrainDrain, BrainDrainService's folder paths,
    /// OverlayService.BrainDrainWithheld and the shell folder-open - all WPF-head services with
    /// no seam. Mod art is NOT a blocker any more (CoreModArt plus Helpers.ModArt), but there is
    /// no art in this panel's markup to write into.
    ///
    /// The clip count is a placeholder 0, which is also the honest default for a fresh install and
    /// is what puts the empty-state banner on screen in the render.
    /// </summary>
    public partial class BrainDrainFeatureControl : UserControl
    {
        public BrainDrainFeatureControl()
        {
            AvaloniaXamlLoader.Load(this);

            SliderLabel.Wire(this, "SliderIntensity", "TxtIntensity", v => $"{(int)v}%");
            SliderLabel.Wire(this, "SliderBlurStrength", "TxtBlurStrength", v => $"{(int)v}%");

            RefreshClipCount();

            // ponytail: needs App.BrainDrain, Services.BrainDrainService and OverlayService -
            // NOT App.Settings, which is CoreSettings.Current today. See the class summary for
            // why the settings writes are still deliberately absent. ChkEnable, ChkMelt,
            // ChkHighRefresh, ChkAllowCapture, both sliders' setting writes, BtnOpenAudioFolder
            // (Empty) and BtnRefreshAudio are inert until the service lands, and WithheldNotice
            // stays hidden (OverlayService.BrainDrainWithheld is false anyway).
            this.FindControl<Button>("BtnRefreshAudio")!.Click += (_, _) => RefreshClipCount();
        }

        /// <summary>
        /// Repaint the clip-library readout. An empty pool makes the whole audio half a silent
        /// no-op, so the count is shown even when it is fine - "0 clips" is the answer to "why is
        /// nothing happening?". The resolved folder path is the service's to give; until then the
        /// line stays empty rather than printing a folder that may not exist.
        /// </summary>
        private void RefreshClipCount()
        {
            // ponytail: needs BrainDrainService.AudioFileCount, wired when it moves to Core.
            const int clips = 0;
            this.FindControl<TextBlock>("TxtClipCount")!.Text = Loc.GetF("st4_braindrain_clips_loaded_0", clips);
            this.FindControl<Border>("NoAudioHint")!.IsVisible = clips == 0;
        }
    }
}
