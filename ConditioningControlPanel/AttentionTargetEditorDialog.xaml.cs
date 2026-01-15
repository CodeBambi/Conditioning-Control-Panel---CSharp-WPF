using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Dialog for customizing attention target appearance
    /// </summary>
    public partial class AttentionTargetEditorDialog : Window
    {
        private string _color1;
        private string _color2;
        private string _textColor;
        private string _borderColor;
        private bool _showBorder;
        private bool _floatingText;
        private string _font;

        public AttentionTargetEditorDialog()
        {
            InitializeComponent();

            // Load current settings
            var settings = App.Settings.Current;
            _color1 = settings.AttentionColor1;
            _color2 = settings.AttentionColor2;
            _textColor = settings.AttentionTextColor;
            _borderColor = settings.AttentionBorderColor;
            _showBorder = settings.AttentionShowBorder;
            _floatingText = settings.AttentionFloatingText;
            _font = settings.AttentionFont;

            // Initialize UI
            UpdateColorButtons();
            ChkFloatingText.IsChecked = _floatingText;
            ChkShowBorder.IsChecked = _showBorder;
            UpdateRowVisibility();
            SelectFontInCombo(_font);
            UpdatePreview();
        }

        private void SelectFontInCombo(string fontName)
        {
            foreach (ComboBoxItem item in CmbFont.Items)
            {
                if (item.Tag?.ToString() == fontName)
                {
                    CmbFont.SelectedItem = item;
                    return;
                }
            }
            CmbFont.SelectedIndex = 0; // Default to first
        }

        private void UpdateColorButtons()
        {
            try
            {
                BtnColor1.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_color1));
                TxtColor1.Text = _color1;

                BtnColor2.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_color2));
                TxtColor2.Text = _color2;

                BtnTextColor.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_textColor));
                TxtTextColor.Text = _textColor;

                BtnBorderColor.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_borderColor));
                TxtBorderColor.Text = _borderColor;
            }
            catch { }
        }

        private void UpdateRowVisibility()
        {
            // When floating text is enabled, hide background/border options
            BorderToggleRow.Visibility = _floatingText ? Visibility.Collapsed : Visibility.Visible;
            BorderColorRow.Visibility = (_showBorder && !_floatingText) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdatePreview()
        {
            try
            {
                var color1 = (Color)ColorConverter.ConvertFromString(_color1);
                var color2 = (Color)ColorConverter.ConvertFromString(_color2);
                var textColor = (Color)ColorConverter.ConvertFromString(_textColor);
                var borderColor = (Color)ColorConverter.ConvertFromString(_borderColor);

                // Background - transparent for floating text mode
                if (_floatingText)
                {
                    PreviewBorder.Background = Brushes.Transparent;
                    PreviewBorder.BorderBrush = Brushes.Transparent;
                    PreviewBorder.BorderThickness = new Thickness(0);
                }
                else
                {
                    // Gradient background
                    PreviewBorder.Background = new LinearGradientBrush(color1, color2, 90);

                    // Border
                    if (_showBorder)
                    {
                        PreviewBorder.BorderBrush = new SolidColorBrush(borderColor);
                        PreviewBorder.BorderThickness = new Thickness(3);
                    }
                    else
                    {
                        PreviewBorder.BorderBrush = Brushes.Transparent;
                        PreviewBorder.BorderThickness = new Thickness(0);
                    }
                }

                // Text
                PreviewText.Foreground = new SolidColorBrush(textColor);
                PreviewText.FontFamily = new FontFamily(_font);

                // Text shadow - darker version of text color for floating, or primary color otherwise
                var shadowBase = _floatingText ? textColor : color1;
                var shadowColor = Color.FromRgb(
                    (byte)(shadowBase.R * 0.4),
                    (byte)(shadowBase.G * 0.4),
                    (byte)(shadowBase.B * 0.4));
                PreviewTextShadow.Color = shadowColor;
            }
            catch { }
        }

        private string? PickColor(string currentColor)
        {
            var dialog = new System.Windows.Forms.ColorDialog
            {
                FullOpen = true
            };

            try
            {
                var color = (Color)ColorConverter.ConvertFromString(currentColor);
                dialog.Color = System.Drawing.Color.FromArgb(color.R, color.G, color.B);
            }
            catch { }

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                return $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
            }
            return null;
        }

        private void BtnColor1_Click(object sender, RoutedEventArgs e)
        {
            var color = PickColor(_color1);
            if (color != null)
            {
                _color1 = color;
                UpdateColorButtons();
                UpdatePreview();
            }
        }

        private void BtnColor2_Click(object sender, RoutedEventArgs e)
        {
            var color = PickColor(_color2);
            if (color != null)
            {
                _color2 = color;
                UpdateColorButtons();
                UpdatePreview();
            }
        }

        private void BtnTextColor_Click(object sender, RoutedEventArgs e)
        {
            var color = PickColor(_textColor);
            if (color != null)
            {
                _textColor = color;
                UpdateColorButtons();
                UpdatePreview();
            }
        }

        private void BtnBorderColor_Click(object sender, RoutedEventArgs e)
        {
            var color = PickColor(_borderColor);
            if (color != null)
            {
                _borderColor = color;
                UpdateColorButtons();
                UpdatePreview();
            }
        }

        private void ChkFloatingText_Changed(object sender, RoutedEventArgs e)
        {
            _floatingText = ChkFloatingText.IsChecked == true;
            UpdateRowVisibility();
            UpdatePreview();
        }

        private void ChkShowBorder_Changed(object sender, RoutedEventArgs e)
        {
            _showBorder = ChkShowBorder.IsChecked == true;
            UpdateRowVisibility();
            UpdatePreview();
        }

        private void CmbFont_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbFont.SelectedItem is ComboBoxItem item && item.Tag is string font)
            {
                _font = font;
                UpdatePreview();
            }
        }

        #region Presets

        private void PresetPurple_Click(object sender, RoutedEventArgs e)
        {
            _color1 = "#9B59B6";
            _color2 = "#8E44AD";
            _textColor = "#FFFFFF";
            _showBorder = false;
            _floatingText = false;
            _font = "Segoe UI";
            ApplyPreset();
        }

        private void PresetPink_Click(object sender, RoutedEventArgs e)
        {
            _color1 = "#FF64C8";
            _color2 = "#FF3296";
            _textColor = "#FFFFFF";
            _showBorder = true;
            _floatingText = false;
            _borderColor = "#FFFFFF";
            _font = "Comic Sans MS";
            ApplyPreset();
        }

        private void PresetGreen_Click(object sender, RoutedEventArgs e)
        {
            _color1 = "#2ECC71";
            _color2 = "#27AE60";
            _textColor = "#FFFFFF";
            _showBorder = false;
            _floatingText = false;
            _font = "Impact";
            ApplyPreset();
        }

        private void PresetBlue_Click(object sender, RoutedEventArgs e)
        {
            _color1 = "#3498DB";
            _color2 = "#2980B9";
            _textColor = "#FFFFFF";
            _showBorder = false;
            _floatingText = false;
            _font = "Arial Black";
            ApplyPreset();
        }

        private void ApplyPreset()
        {
            ChkFloatingText.IsChecked = _floatingText;
            ChkShowBorder.IsChecked = _showBorder;
            UpdateRowVisibility();
            SelectFontInCombo(_font);
            UpdateColorButtons();
            UpdatePreview();
        }

        #endregion

        private void BtnTest_Click(object sender, RoutedEventArgs e)
        {
            // Temporarily apply settings and spawn a test target
            var settings = App.Settings.Current;
            var oldC1 = settings.AttentionColor1;
            var oldC2 = settings.AttentionColor2;
            var oldText = settings.AttentionTextColor;
            var oldBorder = settings.AttentionBorderColor;
            var oldShowBorder = settings.AttentionShowBorder;
            var oldFloating = settings.AttentionFloatingText;
            var oldFont = settings.AttentionFont;

            try
            {
                // Apply current dialog values temporarily
                settings.AttentionColor1 = _color1;
                settings.AttentionColor2 = _color2;
                settings.AttentionTextColor = _textColor;
                settings.AttentionBorderColor = _borderColor;
                settings.AttentionShowBorder = _showBorder;
                settings.AttentionFloatingText = _floatingText;
                settings.AttentionFont = _font;

                // Get a random text from the attention pool
                var pool = settings.AttentionPool;
                string text = "BAMBI";
                foreach (var kvp in pool)
                {
                    if (kvp.Value)
                    {
                        text = kvp.Key;
                        break;
                    }
                }

                // Spawn test target on primary screen
                var screen = System.Windows.Forms.Screen.PrimaryScreen;
                if (screen != null)
                {
                    var target = new Services.FloatingText(text, screen, settings.AttentionSize, () =>
                    {
                        // Just log when clicked
                        App.Logger?.Debug("Test target clicked");
                    });
                }
            }
            finally
            {
                // Restore original settings (user hasn't saved yet)
                settings.AttentionColor1 = oldC1;
                settings.AttentionColor2 = oldC2;
                settings.AttentionTextColor = oldText;
                settings.AttentionBorderColor = oldBorder;
                settings.AttentionShowBorder = oldShowBorder;
                settings.AttentionFloatingText = oldFloating;
                settings.AttentionFont = oldFont;
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            // Save to settings
            var settings = App.Settings.Current;
            settings.AttentionColor1 = _color1;
            settings.AttentionColor2 = _color2;
            settings.AttentionTextColor = _textColor;
            settings.AttentionBorderColor = _borderColor;
            settings.AttentionShowBorder = _showBorder;
            settings.AttentionFloatingText = _floatingText;
            settings.AttentionFont = _font;

            DialogResult = true;
            Close();
        }
    }
}
