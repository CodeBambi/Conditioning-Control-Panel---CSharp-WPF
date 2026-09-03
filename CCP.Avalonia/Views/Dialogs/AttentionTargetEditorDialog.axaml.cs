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
    ///  - Settings load/save run for real against <see cref="CoreSettings"/>; the colour buttons
    ///    open this head's <see cref="ColorPickerDialog"/> instead of WinForms' ColorDialog.
    ///  - The test target (Services.FloatingText on a Win32 screen) is still a stub - see BtnTest.
    ///  - <c>PreviewTextShadow</c> is gone with the DropShadowEffect it coloured.
    ///  - <c>DialogResult = x; Close()</c> becomes <c>Close(x)</c>. WPF's picker was modal and
    ///    inline; Avalonia's is awaited, so the four colour handlers are async void.
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
            var settings = CoreSettings.Current;
            _color1 = settings.AttentionColor1;
            _color2 = settings.AttentionColor2;
            _textColor = settings.AttentionTextColor;
            _borderColor = settings.AttentionBorderColor;
            _showBorder = settings.AttentionShowBorder;
            _floatingText = settings.AttentionFloatingText;
            _font = settings.AttentionFont;

            this.FindControl<Button>("PresetPurple")!.Click += (_, _) => PresetPurple_Click();
            this.FindControl<Button>("PresetPink")!.Click += (_, _) => PresetPink_Click();
            this.FindControl<Button>("PresetGreen")!.Click += (_, _) => PresetGreen_Click();
            this.FindControl<Button>("PresetBlue")!.Click += (_, _) => PresetBlue_Click();
            this.FindControl<Button>("BtnColor1")!.Click += (_, _) => Pick(_color1, v => _color1 = v);
            this.FindControl<Button>("BtnColor2")!.Click += (_, _) => Pick(_color2, v => _color2 = v);
            this.FindControl<Button>("BtnTextColor")!.Click += (_, _) => Pick(_textColor, v => _textColor = v);
            this.FindControl<Button>("BtnBorderColor")!.Click += (_, _) => Pick(_borderColor, v => _borderColor = v);
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

        /// <summary>
        /// WPF's <c>PickColor</c>: seed the picker with the colour we already hold, and on OK take
        /// the six-digit hex the settings model stores. A ref parameter cannot cross an await, so
        /// the field is handed in as a setter instead. Cancel leaves everything alone, exactly as
        /// <c>DialogResult.Cancel</c> did.
        /// </summary>
        private async void Pick(string current, Action<string> assign)
        {
            Color initial;
            try { initial = Color.Parse(current); }
            catch { initial = Colors.Magenta; }   // WPF swallowed an unparseable hex the same way

            var chosen = await ColorPickerDialog.PickAsync(this, initial);
            if (chosen is not { } c) return;

            assign($"#{c.R:X2}{c.G:X2}{c.B:X2}");   // never Color.ToString(): Avalonia writes #AARRGGBB
            UpdateColorButtons();
            UpdatePreview();
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
            _color1 = CoreMods.SecondaryColorHex;
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
            // ponytail: needs ConditioningControlPanel/Services/Video/VideoService.cs:8130
            // (internal class FloatingText : IAttentionTarget - a Win32 layered
            // click-through window, bucket E) and System.Windows.Forms.Screen.PrimaryScreen. The
            // settings half is ready - WPF applies these seven fields, spawns the target on the
            // primary screen and restores the old values in a finally - so this is one window
            // reimplementation away, not a settings problem.
        }

        private void BtnSave_Click()
        {
            // Save to settings. No CoreSettings.Save() here on purpose: WPF's BtnSave_Click writes
            // the fields and closes, and neither caller (MainWindow.BtnAttentionStyle_Click,
            // VideoFeatureControl.BtnAttentionStyle_Click) saves either - the values ride out with
            // the next write of the settings file, and adding a flush would not be the same app.
            var settings = CoreSettings.Current;
            settings.AttentionColor1 = _color1;
            settings.AttentionColor2 = _color2;
            settings.AttentionTextColor = _textColor;
            settings.AttentionBorderColor = _borderColor;
            settings.AttentionShowBorder = _showBorder;
            settings.AttentionFloatingText = _floatingText;
            settings.AttentionFont = _font;

            Close(true);
        }
    }
}
