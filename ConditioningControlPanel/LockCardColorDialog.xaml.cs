using System.Windows;
using System.Windows.Media;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Dialog for editing lock card colors
    /// </summary>
    public partial class LockCardColorDialog : Window
    {
        private Color _bgColor;
        private Color _textColor;
        private Color _inputBgColor;
        private Color _inputTextColor;
        private Color _accentColor;

        public LockCardColorDialog()
        {
            InitializeComponent();
            LoadCurrentSettings();
            UpdatePreview();
        }

        private void LoadCurrentSettings()
        {
            var settings = App.Settings.Current;
            
            _bgColor = ParseColor(settings.LockCardBackgroundColor, Color.FromRgb(26, 26, 46));
            _textColor = ParseColor(settings.LockCardTextColor, Color.FromRgb(255, 105, 180));
            _inputBgColor = ParseColor(settings.LockCardInputBackgroundColor, Color.FromRgb(37, 37, 66));
            _inputTextColor = ParseColor(settings.LockCardInputTextColor, Colors.White);
            _accentColor = ParseColor(settings.LockCardAccentColor, Color.FromRgb(255, 105, 180));
            
            UpdateColorButtons();
        }

        private void UpdateColorButtons()
        {
            BtnBgColor.Background = new SolidColorBrush(_bgColor);
            BtnTextColor.Background = new SolidColorBrush(_textColor);
            BtnInputBgColor.Background = new SolidColorBrush(_inputBgColor);
            BtnInputTextColor.Background = new SolidColorBrush(_inputTextColor);
            BtnAccentColor.Background = new SolidColorBrush(_accentColor);
        }

        private void UpdatePreview()
        {
            // Background
            PreviewBorder.Background = new SolidColorBrush(_bgColor);
            
            // Phrase text
            PreviewPhrase.Foreground = new SolidColorBrush(_textColor);
            
            // Input field
            PreviewInputBorder.Background = new SolidColorBrush(_inputBgColor);
            PreviewInputBorder.BorderBrush = new SolidColorBrush(_accentColor);
            PreviewInputText.Foreground = new SolidColorBrush(_inputTextColor);
            
            // Progress
            PreviewProgress.Foreground = new SolidColorBrush(_accentColor);
            PreviewProgressBar.Background = new SolidColorBrush(_accentColor);
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

        private void BtnInputBgColor_Click(object sender, RoutedEventArgs e)
        {
            var color = ShowColorPicker(_inputBgColor);
            if (color.HasValue)
            {
                _inputBgColor = color.Value;
                UpdateColorButtons();
                UpdatePreview();
            }
        }

        private void BtnInputTextColor_Click(object sender, RoutedEventArgs e)
        {
            var color = ShowColorPicker(_inputTextColor);
            if (color.HasValue)
            {
                _inputTextColor = color.Value;
                UpdateColorButtons();
                UpdatePreview();
            }
        }

        private void BtnAccentColor_Click(object sender, RoutedEventArgs e)
        {
            var color = ShowColorPicker(_accentColor);
            if (color.HasValue)
            {
                _accentColor = color.Value;
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
            
            settings.LockCardBackgroundColor = ColorToHex(_bgColor);
            settings.LockCardTextColor = ColorToHex(_textColor);
            settings.LockCardInputBackgroundColor = ColorToHex(_inputBgColor);
            settings.LockCardInputTextColor = ColorToHex(_inputTextColor);
            settings.LockCardAccentColor = ColorToHex(_accentColor);
            
            App.Logger?.Information("Lock card colors updated");
            
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
