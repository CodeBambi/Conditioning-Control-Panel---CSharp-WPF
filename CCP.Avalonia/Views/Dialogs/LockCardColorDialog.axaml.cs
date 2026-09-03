using System;
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
    ///  - App.Settings / App.Mods become CoreSettings / CoreMods. CoreMods.AccentColorHex never
    ///    answers null, so WPF's <c>?? "#FF69B4"</c> survives only as the parse fallback.
    ///  - System.Windows.Forms.ColorDialog becomes <see cref="ColorPickerDialog"/>, which is async
    ///    and needs an owner, so the five swatch handlers await where WPF blocked inline.
    ///
    /// Save mutates <c>CoreSettings.Current</c> WITHOUT calling Save(), exactly as the WPF original
    /// does: it is the live instance, and the shutdown SaveImmediate flushes it.
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

            _btnBgColor.Click += (_, _) => Pick(_bgColor, c => _bgColor = c);
            _btnTextColor.Click += (_, _) => Pick(_textColor, c => _textColor = c);
            _btnInputBgColor.Click += (_, _) => Pick(_inputBgColor, c => _inputBgColor = c);
            _btnInputTextColor.Click += (_, _) => Pick(_inputTextColor, c => _inputTextColor = c);
            _btnAccentColor.Click += (_, _) => Pick(_accentColor, c => _accentColor = c);
            this.FindControl<Button>("BtnCancel")!.Click += (_, _) => Close(false);
            this.FindControl<Button>("BtnSave")!.Click += (_, _) => BtnSave_Click();
        }

        private void LoadCurrentSettings()
        {
            var settings = CoreSettings.Current;

            var accent = ParseColor(CoreMods.AccentColorHex, Color.FromRgb(255, 105, 180)); // #FF69B4
            _bgColor = ParseColor(settings.LockCardBackgroundColor, Color.FromRgb(26, 26, 46));
            _textColor = ParseColor(settings.LockCardTextColor, accent);
            _inputBgColor = ParseColor(settings.LockCardInputBackgroundColor, Color.FromRgb(37, 37, 66));
            _inputTextColor = ParseColor(settings.LockCardInputTextColor, Colors.White);
            _accentColor = ParseColor(settings.LockCardAccentColor, accent);

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

        /// <summary>The five swatch handlers, WPF's body with the blocking ColorDialog swapped for
        /// the awaited head picker. A cancelled pick answers null and changes nothing.</summary>
        private async void Pick(Color current, Action<Color> assign)
        {
            var color = await ColorPickerDialog.PickAsync(this, current);
            if (color.HasValue)
            {
                assign(color.Value);
                UpdateColorButtons();
                UpdatePreview();
            }
        }

        private void BtnSave_Click()
        {
            var settings = CoreSettings.Current;

            settings.LockCardBackgroundColor = ColorToHex(_bgColor);
            settings.LockCardTextColor = ColorToHex(_textColor);
            settings.LockCardInputBackgroundColor = ColorToHex(_inputBgColor);
            settings.LockCardInputTextColor = ColorToHex(_inputTextColor);
            settings.LockCardAccentColor = ColorToHex(_accentColor);

            Serilog.Log.Information("Lock card colors updated");
            Close(true);
        }

        private static Color ParseColor(string hex, Color fallback)
        {
            if (string.IsNullOrEmpty(hex)) return fallback;
            if (!hex.StartsWith("#")) hex = "#" + hex;
            return Color.TryParse(hex, out var color) ? color : fallback;
        }

        private static string ColorToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}
