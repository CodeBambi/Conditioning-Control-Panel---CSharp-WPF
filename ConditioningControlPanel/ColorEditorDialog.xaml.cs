using System.Windows;
using System.Windows.Media;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Dialog for editing subliminal text colors
    /// </summary>
    public partial class ColorEditorDialog : Window
    {
        private Color _bgColor;
        private Color _textColor;
        private Color _borderColor;

        public ColorEditorDialog()
        {
            InitializeComponent();
            LoadCurrentSettings();
            UpdatePreview();
        }

        private void LoadCurrentSettings()
        {
            var settings = App.Settings.Current;
            
            _bgColor = ParseColor(settings.SubBackgroundColor, Colors.Black);
            _textColor = ParseColor(settings.SubTextColor, Color.FromRgb(255, 0, 255));
            _borderColor = ParseColor(settings.SubBorderColor, Colors.White);
            
            ChkBgTransparent.IsChecked = settings.SubBackgroundTransparent;
            ChkTextTransparent.IsChecked = settings.SubTextTransparent;
            
            UpdateColorButtons();
        }

        private void UpdateColorButtons()
        {
            BtnBgColor.Background = new SolidColorBrush(_bgColor);
            BtnTextColor.Background = new SolidColorBrush(_textColor);
            BtnBorderColor.Background = new SolidColorBrush(_borderColor);
        }

        private void UpdatePreview()
        {
            if (ChkBgTransparent.IsChecked == true)
            {
                PreviewBorder.Background = new SolidColorBrush(Color.FromRgb(26, 26, 46)); // Dark purple like app bg
            }
            else
            {
                PreviewBorder.Background = new SolidColorBrush(_bgColor);
            }

            // Create text with outline effect in preview
            PreviewText.Foreground = new SolidColorBrush(_textColor);
            
            // Add stroke effect using TextBlock's effect
            var effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = _borderColor,
                Direction = 0,
                ShadowDepth = 0,
                BlurRadius = 3,
                Opacity = 1
            };
            PreviewText.Effect = effect;
        }

        private void BtnBgColor_Click(object sender, RoutedEventArgs e)
        {
            var color = ShowColorPicker(_bgColor);
            if (color.HasValue)
            {
                _bgColor = color.Value;
                UpdateColorButtons();
                UpdatePreview();
            }
        }

        private void BtnTextColor_Click(object sender, RoutedEventArgs e)
        {
            var color = ShowColorPicker(_textColor);
            if (color.HasValue)
            {
                _textColor = color.Value;
                UpdateColorButtons();
                UpdatePreview();
            }
        }

        private void BtnBorderColor_Click(object sender, RoutedEventArgs e)
        {
            var color = ShowColorPicker(_borderColor);
            if (color.HasValue)
            {
                _borderColor = color.Value;
                UpdateColorButtons();
                UpdatePreview();
            }
        }

        private Color? ShowColorPicker(Color currentColor)
        {
            // Use Windows Forms color dialog (WPF doesn't have a built-in one)
            var dialog = new System.Windows.Forms.ColorDialog
            {
                Color = System.Drawing.Color.FromArgb(currentColor.A, currentColor.R, currentColor.G, currentColor.B),
                FullOpen = true,
                AnyColor = true
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                return Color.FromArgb(
                    dialog.Color.A,
                    dialog.Color.R,
                    dialog.Color.G,
                    dialog.Color.B
                );
            }

            return null;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var settings = App.Settings.Current;
            
            settings.SubBackgroundColor = ColorToHex(_bgColor);
            settings.SubTextColor = ColorToHex(_textColor);
            settings.SubBorderColor = ColorToHex(_borderColor);
            settings.SubBackgroundTransparent = ChkBgTransparent.IsChecked ?? false;
            settings.SubTextTransparent = ChkTextTransparent.IsChecked ?? false;
            
            App.Logger?.Information("Subliminal colors updated");
            
            DialogResult = true;
            Close();
        }

        private Color ParseColor(string hex, Color fallback)
        {
            try
            {
                if (string.IsNullOrEmpty(hex)) return fallback;
                if (!hex.StartsWith("#")) hex = "#" + hex;
                return (Color)ColorConverter.ConvertFromString(hex);
            }
            catch
            {
                return fallback;
            }
        }

        private string ColorToHex(Color color)
        {
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }
    }
}
