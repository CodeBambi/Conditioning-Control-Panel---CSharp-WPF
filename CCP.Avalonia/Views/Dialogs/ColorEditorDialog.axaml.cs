using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>
    /// Dialog for editing subliminal text colors.
    ///
    /// PORTED from ConditioningControlPanel/Dialogs/ColorEditorDialog.xaml.cs. Deviations:
    ///  - DialogResult becomes Close(bool).
    ///  - System.Windows.Forms.ColorDialog becomes <see cref="ColorPickerDialog"/>, which is async
    ///    and needs an owner, so the three swatch handlers await where WPF blocked inline.
    ///  - FontPickerHelper is a WPF-head helper (it resolves the bundled Fredoka face from a
    ///    pack:// URI), so the preview keeps the markup's Arial rather than the chosen face.
    ///  - The text outline stays a DropShadowEffect - Avalonia.Media has the same type.
    ///
    /// Save mutates <c>CoreSettings.Current</c> WITHOUT calling Save(), exactly as the WPF original
    /// does: it is the live instance, and the shutdown SaveImmediate flushes it.
    /// </summary>
    public partial class ColorEditorDialog : Window
    {
        private Color _bgColor;
        private Color _textColor;
        private Color _borderColor;

        private readonly Button _btnBgColor;
        private readonly Button _btnTextColor;
        private readonly Button _btnBorderColor;
        private readonly CheckBox _chkBgTransparent;
        private readonly CheckBox _chkTextTransparent;
        private readonly CheckBox _chkStealsFocus;
        private readonly Border _previewBorder;
        private readonly TextBlock _previewText;

        public ColorEditorDialog()
        {
            AvaloniaXamlLoader.Load(this);

            _btnBgColor = this.FindControl<Button>("BtnBgColor")!;
            _btnTextColor = this.FindControl<Button>("BtnTextColor")!;
            _btnBorderColor = this.FindControl<Button>("BtnBorderColor")!;
            _chkBgTransparent = this.FindControl<CheckBox>("ChkBgTransparent")!;
            _chkTextTransparent = this.FindControl<CheckBox>("ChkTextTransparent")!;
            _chkStealsFocus = this.FindControl<CheckBox>("ChkStealsFocus")!;
            _previewBorder = this.FindControl<Border>("PreviewBorder")!;
            _previewText = this.FindControl<TextBlock>("PreviewText")!;

            LoadCurrentSettings();
            UpdatePreview();

            _btnBgColor.Click += (_, _) => Pick(_bgColor, c => _bgColor = c);
            _btnTextColor.Click += (_, _) => Pick(_textColor, c => _textColor = c);
            _btnBorderColor.Click += (_, _) => Pick(_borderColor, c => _borderColor = c);
            _chkBgTransparent.IsCheckedChanged += (_, _) => UpdatePreview();
            this.FindControl<Button>("BtnCancel")!.Click += (_, _) => Close(false);
            this.FindControl<Button>("BtnSave")!.Click += (_, _) => BtnSave_Click();
        }

        private void LoadCurrentSettings()
        {
            var settings = CoreSettings.Current;

            _bgColor = ParseColor(settings.SubBackgroundColor, Colors.Black);
            _textColor = ParseColor(settings.SubTextColor, Color.FromRgb(255, 0, 255));
            _borderColor = ParseColor(settings.SubBorderColor, Colors.White);

            _chkBgTransparent.IsChecked = settings.SubBackgroundTransparent;
            _chkTextTransparent.IsChecked = settings.SubTextTransparent;
            _chkStealsFocus.IsChecked = settings.SubliminalStealsFocus;

            UpdateColorButtons();
        }

        private void UpdateColorButtons()
        {
            _btnBgColor.Background = new SolidColorBrush(_bgColor);
            _btnTextColor.Background = new SolidColorBrush(_textColor);
            _btnBorderColor.Background = new SolidColorBrush(_borderColor);
        }

        private void UpdatePreview()
        {
            if (_chkBgTransparent.IsChecked == true)
            {
                _previewBorder.Background = this.TryFindResource("DarkerBgBrush", out var brush) && brush is IBrush b
                    ? b
                    : new SolidColorBrush(Color.FromRgb(26, 26, 46));
            }
            else
            {
                _previewBorder.Background = new SolidColorBrush(_bgColor);
            }

            // Create text with outline effect in preview
            _previewText.Foreground = new SolidColorBrush(_textColor);

            // Add stroke effect using TextBlock's effect
            _previewText.Effect = new DropShadowEffect
            {
                Color = _borderColor,
                OffsetX = 0,
                OffsetY = 0,
                BlurRadius = 3,
                Opacity = 1
            };
        }

        /// <summary>The three swatch handlers, WPF's body with the blocking ColorDialog swapped for
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

            settings.SubBackgroundColor = ColorToHex(_bgColor);
            settings.SubTextColor = ColorToHex(_textColor);
            settings.SubBorderColor = ColorToHex(_borderColor);
            settings.SubBackgroundTransparent = _chkBgTransparent.IsChecked ?? false;
            settings.SubTextTransparent = _chkTextTransparent.IsChecked ?? false;
            settings.SubliminalStealsFocus = _chkStealsFocus.IsChecked ?? false;

            Serilog.Log.Information("Subliminal settings updated");
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
