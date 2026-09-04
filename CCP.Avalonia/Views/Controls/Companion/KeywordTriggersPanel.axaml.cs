using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion
{
    /// <summary>
    /// PHASE 5 (G3): the custom keyword-trigger + Screen OCR editors, rescued from the
    /// permanently-Collapsed <c>PatreonTabView</c> and mounted on the Awareness tab.
    ///
    /// <para>Ported from the WPF code-behind, where every handler forwards to a
    /// <c>MainWindow</c> method. Those handlers split cleanly in two, and so does this port.</para>
    ///
    /// <para><b>Restored:</b> the four sliders and the two master-driven detail sections. Their
    /// state is plain <c>AppSettings</c>, which is in Core, so they are seeded from
    /// <see cref="CoreSettings"/> and write back through it - the same fields and the same clamps
    /// as <c>MainWindow.KeywordTriggers.cs</c>. The panel used to hardcode both masters ON, which
    /// showed the detail rows over a source that was switched off.</para>
    ///
    /// <para><b>Still stubbed:</b> everything that needs a live service - <c>App.ScreenOcr</c>,
    /// <c>App.KeywordHighlight</c> and the trigger-row builder. Named at each one below.</para>
    /// </summary>
    public partial class KeywordTriggersPanel : UserControl
    {
        private readonly Expander _expander;
        private readonly TextBlock _txtScreenOcrOffHint;
        private readonly StackPanel _screenOcrIntervalPanel;
        private readonly TextBlock _txtHighlightOffHint;
        private readonly StackPanel _highlightDurationPanel;

        /// <summary>Raised while the seed writes the sliders, so an echo is not a user edit.</summary>
        private bool _isLoading = true;

        public KeywordTriggersPanel()
        {
            AvaloniaXamlLoader.Load(this);

            _expander = this.FindControl<Expander>("KeywordTriggersExpander")!;
            _txtScreenOcrOffHint = this.FindControl<TextBlock>("TxtScreenOcrOffHint")!;
            _screenOcrIntervalPanel = this.FindControl<StackPanel>("ScreenOcrIntervalPanel")!;
            _txtHighlightOffHint = this.FindControl<TextBlock>("TxtHighlightOffHint")!;
            _highlightDurationPanel = this.FindControl<StackPanel>("HighlightDurationPanel")!;

            // ponytail: the PRESET half is in Core already (CCP.Core/Services/
            // KeywordTriggerPresetService.cs, CCP.Core/Models/KeywordTrigger.cs). What is missing
            // is MainWindow.KeywordTriggers.cs's row BUILDER - it constructs WPF rows into
            // TriggerRowsHost and has no Avalonia twin - and the editor dialog those rows open.
            this.FindControl<Button>("BtnAddKeywordTrigger")!.Click += (_, _) => { };
            this.FindControl<Button>("BtnImportFromCustomTriggers")!.Click += (_, _) => { };
            // ponytail: needs App.ScreenOcr / App.KeywordHighlight. Both are live Win32/OCR
            // services with no Core seam at all (there is no CoreOcr), so these two combos have
            // nothing to push a mode to on this head. Deliberately NOT persisted meanwhile: a
            // combo that stores a mode nothing reads is a control claiming a setting took effect.
            this.FindControl<ComboBox>("CmbOcrConfirmation")!.SelectionChanged += (_, _) => { };
            this.FindControl<ComboBox>("CmbOcrHighlightMode")!.SelectionChanged += (_, _) => { };

            SyncFromSettings();

            var sliders = new[]
            {
                "SliderKeywordBufferTimeout", "SliderKeywordSessionMultiplier",
                "SliderScreenOcrInterval", "SliderKeywordHighlightDuration",
            };
            foreach (var name in sliders)
                this.FindControl<Slider>(name)!.ValueChanged += OnSliderChanged;
        }

        protected override void OnAttachedToVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced += OnCurrentReplaced;
            SyncFromSettings();
        }

        protected override void OnDetachedFromVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
        {
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced -= OnCurrentReplaced;
            base.OnDetachedFromVisualTree(e);
        }

        private void OnCurrentReplaced() => Dispatcher.UIThread.Post(SyncFromSettings);

        /// <summary>
        /// WPF's <c>SyncKeywordRescuePanelUi</c> half that this control owns: the four slider
        /// positions and the two master-driven sections. The clamps are the WPF ones, so a
        /// settings file written by a build with different bounds cannot throw a slider out of
        /// range here either.
        /// </summary>
        internal void SyncFromSettings()
        {
            _isLoading = true;
            try
            {
                var s = CoreSettings.Current;
                Set("SliderKeywordBufferTimeout", Math.Clamp(s.KeywordBufferTimeoutMs, 1000, 10000));
                Set("SliderKeywordSessionMultiplier", Math.Clamp(s.KeywordSessionMultiplier, 1.0, 3.0));
                Set("SliderScreenOcrInterval", Math.Clamp(s.ScreenOcrIntervalMs / 1000.0, 2, 10));
                Set("SliderKeywordHighlightDuration", Math.Clamp(s.KeywordHighlightDurationMs / 1000.0, 0.3, 5.0));

                // The masters themselves live on the Awareness tab; this panel only follows them.
                SetScreenOcrDetail(s.ScreenOcrEnabled);
                SetHighlightDetail(s.KeywordHighlightEnabled);
            }
            catch (Exception ex)
            {
                Log.Debug("KeywordTriggersPanel.SyncFromSettings failed: {E}", ex.Message);
            }
            finally
            {
                _isLoading = false;
            }

            void Set(string name, double value)
            {
                var slider = this.FindControl<Slider>(name)!;
                slider.Value = value;
                UpdateLabel(name, value);   // ValueChanged does not fire when the value is unchanged
            }
        }

        /// <summary>
        /// The four sliders' single editor. WPF keeps four one-line handlers; the fields differ but
        /// the shape does not, so one switch is the whole of it.
        /// </summary>
        private void OnSliderChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (sender is not Slider slider || slider.Name is not { } name) return;
            UpdateLabel(name, e.NewValue);
            if (_isLoading) return;

            var s = CoreSettings.Current;
            switch (name)
            {
                case "SliderKeywordBufferTimeout":
                    var buffer = (int)e.NewValue;
                    if (s.KeywordBufferTimeoutMs == buffer) return;
                    s.KeywordBufferTimeoutMs = buffer;
                    break;
                case "SliderKeywordSessionMultiplier":
                    if (Math.Abs(s.KeywordSessionMultiplier - e.NewValue) < 0.0001) return;
                    s.KeywordSessionMultiplier = e.NewValue;
                    break;
                case "SliderScreenOcrInterval":
                    var ocr = (int)e.NewValue * 1000;
                    if (s.ScreenOcrIntervalMs == ocr) return;
                    s.ScreenOcrIntervalMs = ocr;
                    // ponytail: WPF also pushes the new interval into the running scanner
                    // (App.ScreenOcr.UpdateInterval). No OCR service and no Core seam for one on
                    // this head, so there is nothing running to re-time - the stored value is the
                    // whole effect here, which is why this is a restore and not a half-port.
                    break;
                case "SliderKeywordHighlightDuration":
                    var ms = (int)(e.NewValue * 1000);
                    if (s.KeywordHighlightDurationMs == ms) return;
                    s.KeywordHighlightDurationMs = ms;
                    break;
                default:
                    return;
            }
            CoreSettings.Save();
        }

        /// <summary>Follows the Screen OCR master: detail rows when on, the "needs source" hint when off.</summary>
        internal void SetScreenOcrDetail(bool masterOn)
        {
            _screenOcrIntervalPanel.IsVisible = masterOn;
            _txtScreenOcrOffHint.IsVisible = !masterOn;
        }

        /// <summary>Follows the highlight master: detail rows when on, the "needs source" hint when off.</summary>
        internal void SetHighlightDetail(bool masterOn)
        {
            _highlightDurationPanel.IsVisible = masterOn;
            _txtHighlightOffHint.IsVisible = !masterOn;
        }

        /// <summary>
        /// Opens the drawer and scrolls it into view. Used by the Awareness tab's "advanced editor"
        /// hyperlink. The WPF version also drives the ancestor ScrollViewer by hand and pulses a
        /// DropShadowEffect; Avalonia's <see cref="Control.BringIntoView"/> resolves against the
        /// post-layout geometry, and the pulse is dropped (decorative; no bitmap-effect animation
        /// budget on this head yet).
        /// </summary>
        internal void RevealTriggerEditor()
        {
            try
            {
                _expander.IsExpanded = true;
                UpdateLayout();
                this.BringIntoView();
            }
            catch (InvalidOperationException)
            {
                // Layout torn down mid-navigation - the drawer is still expanded, which is the
                // part that matters.
            }
        }

        /// <summary>Value labels; formats copied from MainWindow.KeywordTriggers.cs, which owns
        /// them on WPF.</summary>
        private void UpdateLabel(string sliderName, double value)
        {
            var (label, text) = sliderName switch
            {
                "SliderKeywordBufferTimeout" => ("TxtKeywordBufferTimeout", $"{(int)value / 1000.0:F1}s"),
                "SliderKeywordSessionMultiplier" => ("TxtKeywordSessionMultiplier", $"{value:F1}x"),
                "SliderScreenOcrInterval" => ("TxtScreenOcrInterval", $"{(int)value}s"),
                "SliderKeywordHighlightDuration" => ("TxtKeywordHighlightDuration", $"{value:0.0}s"),
                _ => (null, null),
            };
            if (label == null) return;
            var block = this.FindControl<TextBlock>(label);
            if (block != null) block.Text = text;
        }
    }
}
