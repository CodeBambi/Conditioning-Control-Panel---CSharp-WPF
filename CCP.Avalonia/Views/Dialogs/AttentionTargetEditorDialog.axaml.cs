using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>
    /// Dialog for customizing attention target appearance.
    ///
    /// PORTED from ConditioningControlPanel/Dialogs/AttentionTargetEditorDialog.xaml.cs. Deviations:
    ///  - Settings load/save, the colour picker (WinForms ColorDialog) and the test target
    ///    (Services.FloatingText on a Win32 screen) are stubs - see the ponytail comments.
    ///  - <c>PreviewTextShadow</c> is gone with the DropShadowEffect it coloured.
    ///  - <c>DialogResult = x; Close()</c> becomes <c>Close(x)</c>.
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

        private readonly Border _previewBorder;
        private readonly TextBlock _previewText;
        private readonly CheckBox _chkFloatingText;
        private readonly CheckBox _chkShowBorder;
        private readonly ComboBox _cmbFont;
        private readonly Grid _borderToggleRow;
        private readonly Grid _borderColorRow;

        public AttentionTargetEditorDialog()
        {
            AvaloniaXamlLoader.Load(this);

            _previewBorder = this.FindControl<Border>("PreviewBorder")!;
            _previewText = this.FindControl<TextBlock>("PreviewText")!;
            _chkFloatingText = this.FindControl<CheckBox>("ChkFloatingText")!;
            _chkShowBorder = this.FindControl<CheckBox>("ChkShowBorder")!;
            _cmbFont = this.FindControl<ComboBox>("CmbFont")!;
            _borderToggleRow = this.FindControl<Grid>("BorderToggleRow")!;
            _borderColorRow = this.FindControl<Grid>("BorderColorRow")!;

            // Load current settings
            // ponytail: needs App.Settings.Current.Attention*, wired when settings move to Core.
            // These are the "purple classic" preset values.
            _color1 = "#9B59B6";
            _color2 = "#8E44AD";
            _textColor = "#FFFFFF";
            _borderColor = "#FFFFFF";
            _showBorder = false;
            _floatingText = false;
            _font = "Segoe UI";

            this.FindControl<Button>("PresetPurple")!.Click += (_, _) => PresetPurple_Click();
            this.FindControl<Button>("PresetPink")!.Click += (_, _) => PresetPink_Click();
            this.FindControl<Button>("PresetGreen")!.Click += (_, _) => PresetGreen_Click();
            this.FindControl<Button>("PresetBlue")!.Click += (_, _) => PresetBlue_Click();
            this.FindControl<Button>("BtnColor1")!.Click += (_, _) => PickInto(ref _color1);
            this.FindControl<Button>("BtnColor2")!.Click += (_, _) => PickInto(ref _color2);
            this.FindControl<Button>("BtnTextColor")!.Click += (_, _) => PickInto(ref _textColor);
            this.FindControl<Button>("BtnBorderColor")!.Click += (_, _) => PickInto(ref _borderColor);
            this.FindControl<Button>("BtnTest")!.Click += (_, _) => BtnTest_Click();
            this.FindControl<Button>("BtnCancel")!.Click += (_, _) => Close(false);
            this.FindControl<Button>("BtnSave")!.Click += (_, _) => BtnSave_Click();
            _chkFloatingText.IsCheckedChanged += (_, _) => ChkFloatingText_Changed();
            _chkShowBorder.IsCheckedChanged += (_, _) => ChkShowBorder_Changed();
            _cmbFont.SelectionChanged += (_, _) => CmbFont_SelectionChanged();

            // Initialize UI
            UpdateColorButtons();
            _chkFloatingText.IsChecked = _floatingText;
            _chkShowBorder.IsChecked = _showBorder;
            UpdateRowVisibility();
            SelectFontInCombo(_font);
            UpdatePreview();
        }

        private void SelectFontInCombo(string fontName)
        {
            foreach (var item in _cmbFont.Items)
            {
                if (item is ComboBoxItem cbi && cbi.Tag?.ToString() == fontName)
                {
                    _cmbFont.SelectedItem = item;
                    return;
                }
            }
            _cmbFont.SelectedIndex = 0; // Default to first
        }

        private void UpdateColorButtons()
        {
            try
            {
                Paint("BtnColor1", "TxtColor1", _color1);
                Paint("BtnColor2", "TxtColor2", _color2);
                Paint("BtnTextColor", "TxtTextColor", _textColor);
                Paint("BtnBorderColor", "TxtBorderColor", _borderColor);
            }
            catch { }
        }

        private void Paint(string button, string label, string hex)
        {
            this.FindControl<Button>(button)!.Background = new SolidColorBrush(Color.Parse(hex));
            this.FindControl<TextBlock>(label)!.Text = hex;
        }

        private void UpdateRowVisibility()
        {
            // When floating text is enabled, hide background/border options
            _borderToggleRow.IsVisible = !_floatingText;
            _borderColorRow.IsVisible = _showBorder && !_floatingText;
        }

        private void UpdatePreview()
        {
            try
            {
                var color1 = Color.Parse(_color1);
                var color2 = Color.Parse(_color2);
                var textColor = Color.Parse(_textColor);
                var borderColor = Color.Parse(_borderColor);

                // Background - transparent for floating text mode
                if (_floatingText)
                {
                    _previewBorder.Background = Brushes.Transparent;
                    _previewBorder.BorderBrush = Brushes.Transparent;
                    _previewBorder.BorderThickness = new Thickness(0);
                }
                else
                {
                    // Gradient background - WPF's (color1, color2, 90°) is top-to-bottom.
                    _previewBorder.Background = new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                        GradientStops = { new GradientStop(color1, 0), new GradientStop(color2, 1) }
                    };

                    // Border
                    if (_showBorder)
                    {
                        _previewBorder.BorderBrush = new SolidColorBrush(borderColor);
                        _previewBorder.BorderThickness = new Thickness(3);
                    }
                    else
                    {
                        _previewBorder.BorderBrush = Brushes.Transparent;
                        _previewBorder.BorderThickness = new Thickness(0);
                    }
                }

                // Text
                _previewText.Foreground = new SolidColorBrush(textColor);
                _previewText.FontFamily = new FontFamily(_font);
            }
            catch { }
        }

        private void PickInto(ref string field)
        {
            // ponytail: needs a colour picker; WPF used WinForms ColorDialog. Avalonia's ColorPicker
            // is a separate package and no dependency may be added here. Wired when one is chosen.
            string? color = null;
            if (color != null)
            {
                field = color;
                UpdateColorButtons();
                UpdatePreview();
            }
        }

        private void ChkFloatingText_Changed()
        {
            _floatingText = _chkFloatingText.IsChecked == true;
            UpdateRowVisibility();
            UpdatePreview();
        }

        private void ChkShowBorder_Changed()
        {
            _showBorder = _chkShowBorder.IsChecked == true;
            UpdateRowVisibility();
            UpdatePreview();
        }

        private void CmbFont_SelectionChanged()
        {
            if (_cmbFont.SelectedItem is ComboBoxItem item && item.Tag is string font)
            {
                _font = font;
                UpdatePreview();
            }
        }

        #region Presets

        private void PresetPurple_Click()
        {
            // ponytail: App.Mods?.GetSecondaryColorHex() fallback kept; wired when Mods moves to Core.
            _color1 = "#9B59B6";
            _color2 = "#8E44AD";
            _textColor = "#FFFFFF";
            _showBorder = false;
            _floatingText = false;
            _font = "Segoe UI";
            ApplyPreset();
        }

        private void PresetPink_Click()
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

        private void PresetGreen_Click()
        {
            _color1 = "#2ECC71";
            _color2 = "#27AE60";
            _textColor = "#FFFFFF";
            _showBorder = false;
            _floatingText = false;
            _font = "Impact";
            ApplyPreset();
        }

        private void PresetBlue_Click()
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
            _chkFloatingText.IsChecked = _floatingText;
            _chkShowBorder.IsChecked = _showBorder;
            UpdateRowVisibility();
            SelectFontInCombo(_font);
            UpdateColorButtons();
            UpdatePreview();
        }

        #endregion

        private void BtnTest_Click()
        {
            // ponytail: needs Services.FloatingText (a Win32 layered window, bucket E) and
            // App.Settings; wired when the overlay has a per-platform implementation.
        }

        private void BtnSave_Click()
        {
            // ponytail: needs App.Settings.Current.Attention* to persist; wired when settings move
            // to Core. The chosen values live in the fields above until then.
            Close(true);
        }
    }
}
