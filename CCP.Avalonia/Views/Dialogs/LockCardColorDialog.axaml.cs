using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>
    /// Dialog for editing lock card colors.
    ///
    /// PORTED from ConditioningControlPanel/Dialogs/LockCardColorDialog.xaml.cs. Deviations:
    ///  - DialogResult becomes Close(bool).
    ///  - App.Settings and App.Mods (both WPF-head services) are stubbed: the dialog opens on the
    ///    WPF defaults with the stock #FF69B4 accent, and Save writes nowhere.
    ///  - System.Windows.Forms.ColorDialog has no Avalonia twin in the referenced packages, so
    ///    ShowColorPicker is a stub that returns null (no change).
    /// </summary>
    public partial class LockCardColorDialog : Window
    {
        private Color _bgColor;
        private Color _textColor;
        private Color _inputBgColor;
        private Color _inputTextColor;
        private Color _accentColor;

        private readonly Button _btnBgColor;
        private readonly Button _btnTextColor;
        private readonly Button _btnInputBgColor;
        private readonly Button _btnInputTextColor;
        private readonly Button _btnAccentColor;

        public LockCardColorDialog()
        {
            AvaloniaXamlLoader.Load(this);

            _btnBgColor = this.FindControl<Button>("BtnBgColor")!;
            _btnTextColor = this.FindControl<Button>("BtnTextColor")!;
            _btnInputBgColor = this.FindControl<Button>("BtnInputBgColor")!;
            _btnInputTextColor = this.FindControl<Button>("BtnInputTextColor")!;
            _btnAccentColor = this.FindControl<Button>("BtnAccentColor")!;

            LoadCurrentSettings();
            UpdatePreview();

            _btnBgColor.Click += (_, _) => Pick(ref _bgColor);
            _btnTextColor.Click += (_, _) => Pick(ref _textColor);
            _btnInputBgColor.Click += (_, _) => Pick(ref _inputBgColor);
            _btnInputTextColor.Click += (_, _) => Pick(ref _inputTextColor);
            _btnAccentColor.Click += (_, _) => Pick(ref _accentColor);
            this.FindControl<Button>("BtnCancel")!.Click += (_, _) => Close(false);
            this.FindControl<Button>("BtnSave")!.Click += (_, _) => BtnSave_Click();
        }

        private void LoadCurrentSettings()
        {
            // ponytail: needs SettingsManager (App.Settings.Current.LockCard*) and Mods.GetAccentColorHex,
            // wired when they move to Core. Values below are the WPF fallbacks for an unset setting.
            var accent = Color.Parse("#FF69B4");
            _bgColor = ParseColor("", Color.FromRgb(26, 26, 46));
            _textColor = ParseColor("", accent);
            _inputBgColor = ParseColor("", Color.FromRgb(37, 37, 66));
            _inputTextColor = ParseColor("", Colors.White);
            _accentColor = ParseColor("", accent);

            UpdateColorButtons();
        }

        private void UpdateColorButtons()
        {
            _btnBgColor.Background = new SolidColorBrush(_bgColor);
            _btnTextColor.Background = new SolidColorBrush(_textColor);
            _btnInputBgColor.Background = new SolidColorBrush(_inputBgColor);
            _btnInputTextColor.Background = new SolidColorBrush(_inputTextColor);
            _btnAccentColor.Background = new SolidColorBrush(_accentColor);
        }

        private void UpdatePreview()
        {
            // Background
            this.FindControl<Border>("PreviewBorder")!.Background = new SolidColorBrush(_bgColor);

            // Phrase text
            this.FindControl<TextBlock>("PreviewPhrase")!.Foreground = new SolidColorBrush(_textColor);

            // Input field
            var inputBorder = this.FindControl<Border>("PreviewInputBorder")!;
            inputBorder.Background = new SolidColorBrush(_inputBgColor);
            inputBorder.BorderBrush = new SolidColorBrush(_accentColor);
            this.FindControl<TextBlock>("PreviewInputText")!.Foreground = new SolidColorBrush(_inputTextColor);

            // Progress
            this.FindControl<TextBlock>("PreviewProgress")!.Foreground = new SolidColorBrush(_accentColor);
            this.FindControl<Border>("PreviewProgressBar")!.Background = new SolidColorBrush(_accentColor);
        }

        private void Pick(ref Color target)
        {
            var color = ShowColorPicker(target);
            if (color.HasValue)
            {
                target = color.Value;
                UpdateColorButtons();
                UpdatePreview();
            }
        }

        private Color? ShowColorPicker(Color currentColor)
        {
            // ponytail: needs a colour picker (WPF used System.Windows.Forms.ColorDialog); Avalonia's
            // ColorPicker lives in a package this head does not reference yet. Returns "no change".
            return null;
        }

        private void BtnSave_Click()
        {
            // ponytail: needs SettingsManager to persist the five LockCard* colours, wired when it moves to Core
            Serilog.Log.Information("Lock card colors updated");
            Close(true);
        }

        private static Color ParseColor(string hex, Color fallback)
        {
            if (string.IsNullOrEmpty(hex)) return fallback;
            if (!hex.StartsWith("#")) hex = "#" + hex;
            return Color.TryParse(hex, out var color) ? color : fallback;
        }
    }
}
