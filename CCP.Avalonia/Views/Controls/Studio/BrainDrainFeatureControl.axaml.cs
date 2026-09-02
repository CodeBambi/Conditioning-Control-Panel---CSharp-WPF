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
    /// empty-state banner it drives. What is not: everything the WPF code-behind reaches for -
    /// App.Settings (BrainDrainEnabled / Intensity / HighRefresh / BlurStrength / MeltEnabled /
    /// AllowOverlayCapture), App.BrainDrain (the clip scan, ReloadAudioFiles, Start/Stop),
    /// BrainDrainService's folder paths, OverlayService.BrainDrainWithheld, the mod art resolver
    /// and the Explorer shell-open. Each is a WPF-head service.
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

            // ponytail: needs App.Settings, App.BrainDrain, Services.BrainDrainService,
            // OverlayService and ModResourceResolver, wired when they move to Core. ChkEnable,
            // ChkMelt, ChkHighRefresh, ChkAllowCapture, both sliders' setting writes,
            // BtnOpenAudioFolder(Empty) and BtnRefreshAudio are inert until then, and
            // WithheldNotice stays hidden (OverlayService.BrainDrainWithheld is false anyway).
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
