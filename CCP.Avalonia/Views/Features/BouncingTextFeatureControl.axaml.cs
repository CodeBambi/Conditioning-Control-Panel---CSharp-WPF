using System.Linq;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// Bouncing Text panel, ported from the WPF head. Slider read-outs, the fixed-colour row
    /// reveal and the font list are real; settings writes, BouncingTextService refresh/restart,
    /// the WinForms ColorDialog and the phrase editor over s.BouncingTextPool are not portable yet.
    /// </summary>
    public partial class BouncingTextFeatureControl : UserControl
    {
        public BouncingTextFeatureControl()
        {
            AvaloniaXamlLoader.Load(this);

            SliderLabel.Wire(this, "SliderSpeed", "TxtSpeed", v => ((int)v).ToString());
            SliderLabel.Wire(this, "SliderSize", "TxtSize", v => $"{(int)v}%");
            SliderLabel.Wire(this, "SliderOpacity", "TxtOpacity", v => $"{(int)v}%");

            var mode = this.FindControl<ComboBox>("CmbColorMode")!;
            var fixedRow = this.FindControl<Grid>("PanelFixedColor")!;
            mode.SelectedIndex = 0; // placeholder: AppSettings.BouncingTextColorMode default
            mode.SelectionChanged += (_, _) => fixedRow.IsVisible = mode.SelectedIndex == 1;

            PopulateFonts();

            // ponytail: needs App.Settings / App.BouncingText / a colour picker / TextEditorDialog over
            // the pool, wired when they move to Core. ChkEnable, ChkSecondText, ChkAlwaysOnTop, the
            // ChkFx* toggles, BtnChooseColor, BtnResetColor, BtnEditPhrases are inert.
        }

        /// <summary>
        /// WPF: Helpers.FontPickerHelper.Populate(CmbFont, s.BouncingTextFont, "Segoe UI") - installed
        /// families plus the bundled Fredoka. FontManager is cross-platform, so the installed half
        /// is real here; a headless run may enumerate nothing, hence the fallback.
        /// </summary>
        private void PopulateFonts()
        {
            var cmb = this.FindControl<ComboBox>("CmbFont")!;
            string[] names;
            try { names = FontManager.Current.SystemFonts.Select(f => f.Name).OrderBy(n => n).ToArray(); }
            catch { names = System.Array.Empty<string>(); }
            if (names.Length == 0) names = new[] { "Segoe UI" };
            foreach (var n in names) cmb.Items.Add(new ComboBoxItem { Content = n });
            cmb.SelectedIndex = 0;
        }
    }
}
