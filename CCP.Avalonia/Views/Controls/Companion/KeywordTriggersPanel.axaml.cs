using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion
{
    /// <summary>
    /// PHASE 5 (G3): the custom keyword-trigger + Screen OCR editors, rescued from the
    /// permanently-Collapsed <c>PatreonTabView</c> and mounted on the Awareness tab.
    ///
    /// <para>Ported from the WPF code-behind. The WPF version is pure re-hosting: every handler
    /// forwards to a <c>MainWindow</c> method that reads <c>App.Settings</c>,
    /// <c>KeywordTriggerService</c>, <c>App.ScreenOcr</c> and <c>App.KeywordHighlight</c>. None of
    /// those are in Core, so here the sliders keep their own value labels (the same format strings
    /// MainWindow uses) and everything else is a stub.</para>
    /// </summary>
    public partial class KeywordTriggersPanel : UserControl
    {
        private readonly Expander _expander;
        private readonly TextBlock _txtScreenOcrOffHint;
        private readonly StackPanel _screenOcrIntervalPanel;
        private readonly TextBlock _txtHighlightOffHint;
        private readonly StackPanel _highlightDurationPanel;

        public KeywordTriggersPanel()
        {
            AvaloniaXamlLoader.Load(this);

            _expander = this.FindControl<Expander>("KeywordTriggersExpander")!;
            _txtScreenOcrOffHint = this.FindControl<TextBlock>("TxtScreenOcrOffHint")!;
            _screenOcrIntervalPanel = this.FindControl<StackPanel>("ScreenOcrIntervalPanel")!;
            _txtHighlightOffHint = this.FindControl<TextBlock>("TxtHighlightOffHint")!;
            _highlightDurationPanel = this.FindControl<StackPanel>("HighlightDurationPanel")!;

            // Value labels: formats copied from MainWindow.KeywordTriggers.cs, which owns them on WPF.
            Track("SliderKeywordBufferTimeout", "TxtKeywordBufferTimeout", v => $"{v / 1000.0:F1}s");
            Track("SliderKeywordSessionMultiplier", "TxtKeywordSessionMultiplier", v => $"{v:F1}x");
            Track("SliderScreenOcrInterval", "TxtScreenOcrInterval", v => $"{v}s");
            Track("SliderKeywordHighlightDuration", "TxtKeywordHighlightDuration", v => $"{v:0.0}s");

            // ponytail: needs MainWindow.BtnAddKeywordTrigger_Click / BtnImportFromCustomTriggers_Click
            // (KeywordTriggerService + the trigger row builder), wired when they move to Core.
            this.FindControl<Button>("BtnAddKeywordTrigger")!.Click += (_, _) => { };
            this.FindControl<Button>("BtnImportFromCustomTriggers")!.Click += (_, _) => { };
            // ponytail: needs App.ScreenOcr / App.KeywordHighlight for the two ComboBoxes.
            this.FindControl<ComboBox>("CmbOcrConfirmation")!.SelectionChanged += (_, _) => { };
            this.FindControl<ComboBox>("CmbOcrHighlightMode")!.SelectionChanged += (_, _) => { };

            // ponytail: on WPF the SIGNAL SOURCES / SAFETY masters on the Awareness tab drive these
            // from settings at load. Placeholder: both masters on, so the detail rows render.
            SetScreenOcrDetail(true);
            SetHighlightDetail(true);
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

        private void Track(string slider, string label, Func<double, string> format)
        {
            var s = this.FindControl<Slider>(slider)!;
            var t = this.FindControl<TextBlock>(label)!;
            s.ValueChanged += (_, e) => t.Text = format(e.NewValue);
        }
    }
}
