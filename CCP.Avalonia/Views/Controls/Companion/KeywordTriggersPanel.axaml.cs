using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
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
    /// <para>The trigger list (add, import, per-row editors) is in KeywordTriggersPanel.TriggerList.cs.
    /// <b>Still stubbed:</b> the live services - <c>App.ScreenOcr</c>, <c>App.KeywordHighlight</c>
    /// and the KeywordTriggerService engine that would fire these triggers.</para>
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

            this.FindControl<Button>("BtnAddKeywordTrigger")!.Click += (_, _) => AddTrigger();
            this.FindControl<Button>("BtnImportFromCustomTriggers")!.Click += async (_, _) => await ImportFromCustomTriggersAsync();
            // WPF KeywordTriggers.cs:135-151 persists both; the OCR scanner / highlight overlay
            // that read them are not on this head yet (ponytail: ScreenOcrService, KeywordHighlightService).
            var confirm = this.FindControl<ComboBox>("CmbOcrConfirmation")!;
            confirm.SelectionChanged += (_, _) =>
            {
                if (_isLoading || confirm.SelectedIndex < 0) return;
                CoreSettings.Current.OcrConfirmationScans = confirm.SelectedIndex + 1;
                CoreSettings.Save();
            };
            var mode = this.FindControl<ComboBox>("CmbOcrHighlightMode")!;
            mode.SelectionChanged += (_, _) =>
            {
                if (_isLoading || mode.SelectedIndex < 0) return;
                CoreSettings.Current.OcrHighlightAll = mode.SelectedIndex == 0;
                CoreSettings.Save();
            };

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
            LocalizationManager.Instance.LanguageChanged += OnLanguageChanged;
            SyncFromSettings();
        }

        protected override void OnDetachedFromVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
        {
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced -= OnCurrentReplaced;
            LocalizationManager.Instance.LanguageChanged -= OnLanguageChanged;
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

                this.FindControl<ComboBox>("CmbOcrConfirmation")!.SelectedIndex = Math.Clamp(s.OcrConfirmationScans - 1, 0, 2);
                this.FindControl<ComboBox>("CmbOcrHighlightMode")!.SelectedIndex = s.OcrHighlightAll ? 0 : 1;

                // The masters themselves live on the Awareness tab; this panel only follows them.
                // WPF SyncKeywordRescuePanelUi ANDs the OCR one with KeywordTriggerService.HasAccess.
                SetScreenOcrDetail(s.ScreenOcrEnabled && HasAccess());
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
            RefreshTriggerList();

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
                    // WPF MainWindow.KeywordTriggers.cs:119: re-time the running scanner.
                    Platform.ScreenOcrService.UpdateInterval(ocr);
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
