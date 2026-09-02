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
    ///  - App.Settings (AppSettings lives in the WPF head) is stubbed: the dialog opens on the
    ///    WPF defaults and Save writes nowhere. FontPickerHelper is likewise a head helper, so the
    ///    preview keeps the markup's Arial.
    ///  - System.Windows.Forms.ColorDialog has no Avalonia twin in the referenced packages, so
    ///    ShowColorPicker is a stub that returns null (no change).
    ///  - The text outline stays a DropShadowEffect - Avalonia.Media has the same type.
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

            _btnBgColor.Click += (_, _) => Pick(ref _bgColor);
            _btnTextColor.Click += (_, _) => Pick(ref _textColor);
            _btnBorderColor.Click += (_, _) => Pick(ref _borderColor);
            _chkBgTransparent.IsCheckedChanged += (_, _) => UpdatePreview();
            this.FindControl<Button>("BtnCancel")!.Click += (_, _) => Close(false);
            this.FindControl<Button>("BtnSave")!.Click += (_, _) => BtnSave_Click();
        }

        private void LoadCurrentSettings()
        {
            // ponytail: needs SettingsManager (App.Settings.Current.Sub*), wired when it moves to Core.
            // Values below are the WPF fallbacks for an unset setting.
            _bgColor = ParseColor("", Colors.Black);
            _textColor = ParseColor("", Color.FromRgb(255, 0, 255));
            _borderColor = ParseColor("", Colors.White);

            _chkBgTransparent.IsChecked = false;
            _chkTextTransparent.IsChecked = false;
            _chkStealsFocus.IsChecked = false;

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
            // ponytail: needs SettingsManager to persist Sub* colours and the two toggles, wired when it moves to Core
            Serilog.Log.Information("Subliminal settings updated");
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
