using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ConditioningControlPanel.Features
{
    public partial class BouncingTextFeatureControl : UserControl
    {
        private bool _isLoading = true;

        public BouncingTextFeatureControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            LoadFromSettings();
            if (App.Settings?.Current is INotifyPropertyChanged inpc)
                inpc.PropertyChanged += OnSettingsPropertyChanged;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (App.Settings?.Current is INotifyPropertyChanged inpc)
                inpc.PropertyChanged -= OnSettingsPropertyChanged;
        }

        private void LoadFromSettings()
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            _isLoading = true;
            try
            {
                ChkEnable.IsChecked = s.BouncingTextEnabled;
                SliderSpeed.Value = s.BouncingTextSpeed;
                TxtSpeed.Text = s.BouncingTextSpeed.ToString();
                SliderSize.Value = s.BouncingTextSize;
                TxtSize.Text = $"{s.BouncingTextSize}%";
                SliderOpacity.Value = Math.Max(10, s.BouncingTextOpacity);
                TxtOpacity.Text = $"{s.BouncingTextOpacity}%";
                CmbColorMode.SelectedIndex = s.BouncingTextColorMode;
                ChkFxBreathing.IsChecked = s.BouncingTextFxBreathing;
                ChkFxWobble.IsChecked = s.BouncingTextFxWobble;
                ChkFxSpin.IsChecked = s.BouncingTextFxSpin;
                ChkFxVelocityTilt.IsChecked = s.BouncingTextFxVelocityTilt;
                ChkFxSquash.IsChecked = s.BouncingTextFxSquashStretch;
                ChkFxCornerBurst.IsChecked = s.BouncingTextFxCornerBurst;
                ChkOutline.IsChecked = s.BouncingTextOutline;
                ChkSecondText.IsChecked = s.BouncingTextSecondText;
                ChkAlwaysOnTop.IsChecked = s.BouncingTextAlwaysOnTop;
                UpdateSwatch();
                UpdateFixedColorPanel();
            }
            finally { _isLoading = false; }
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Models.AppSettings.BouncingTextEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.BouncingTextSpeed) ||
                e.PropertyName == nameof(Models.AppSettings.BouncingTextSize) ||
                e.PropertyName == nameof(Models.AppSettings.BouncingTextOpacity) ||
                e.PropertyName == nameof(Models.AppSettings.BouncingTextColorMode) ||
                e.PropertyName == nameof(Models.AppSettings.BouncingTextFixedColor) ||
                e.PropertyName == nameof(Models.AppSettings.BouncingTextFxBreathing) ||
                e.PropertyName == nameof(Models.AppSettings.BouncingTextFxWobble) ||
                e.PropertyName == nameof(Models.AppSettings.BouncingTextFxSpin) ||
                e.PropertyName == nameof(Models.AppSettings.BouncingTextFxVelocityTilt) ||
                e.PropertyName == nameof(Models.AppSettings.BouncingTextFxSquashStretch) ||
                e.PropertyName == nameof(Models.AppSettings.BouncingTextFxCornerBurst) ||
                e.PropertyName == nameof(Models.AppSettings.BouncingTextOutline) ||
                e.PropertyName == nameof(Models.AppSettings.BouncingTextSecondText) ||
                e.PropertyName == nameof(Models.AppSettings.BouncingTextAlwaysOnTop))
            {
                Dispatcher.BeginInvoke(new Action(LoadFromSettings));
            }
        }

        private void ChkEnable_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var on = ChkEnable.IsChecked ?? false;
            s.BouncingTextEnabled = on;
            App.Settings?.Save();

            // Live-apply: start/stop bouncing text if engine is running
            if (App.IsEngineRunning)
            {
                if (on)
                    App.BouncingText?.Start();
                else
                    App.BouncingText?.Stop();
            }
        }

        private void SliderSpeed_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtSpeed.Text = v.ToString();
            s.BouncingTextSpeed = v;
            SafeRefresh();
            App.Settings?.Save();
        }

        private void SliderSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtSize.Text = $"{v}%";
            s.BouncingTextSize = v;
            SafeRefresh();
            App.Settings?.Save();
        }

        private void SliderOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtOpacity.Text = $"{v}%";
            s.BouncingTextOpacity = v;
            SafeRefresh();
            App.Settings?.Save();
        }

        private void CmbColorMode_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var mode = CmbColorMode.SelectedIndex;
            if (mode < 0 || s.BouncingTextColorMode == mode) return;
            s.BouncingTextColorMode = mode;
            App.Settings?.Save();
            UpdateFixedColorPanel();
            SafeRefresh();
        }

        private void BtnChooseColor_Click(object sender, RoutedEventArgs e)
        {
            var s = App.Settings?.Current;
            if (s == null) return;

            var (er, eg, eb) = EffectiveColor();
            using var dialog = new System.Windows.Forms.ColorDialog
            {
                FullOpen = true,
                Color = System.Drawing.Color.FromArgb(er, eg, eb)
            };
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

            s.BouncingTextFixedColor = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
            App.Settings?.Save();
            UpdateSwatch();
            SafeRefresh();
        }

        private void BtnResetColor_Click(object sender, RoutedEventArgs e)
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            s.BouncingTextFixedColor = ""; // empty = hot pink default
            App.Settings?.Save();
            UpdateSwatch();
            SafeRefresh();
        }

        // The transform effects are read per-frame by the service, so writing the
        // setting is all a live apply needs.
        private void FxToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.BouncingTextFxBreathing = ChkFxBreathing.IsChecked ?? false;
            s.BouncingTextFxWobble = ChkFxWobble.IsChecked ?? false;
            s.BouncingTextFxSpin = ChkFxSpin.IsChecked ?? false;
            s.BouncingTextFxVelocityTilt = ChkFxVelocityTilt.IsChecked ?? false;
            s.BouncingTextFxSquashStretch = ChkFxSquash.IsChecked ?? false;
            s.BouncingTextFxCornerBurst = ChkFxCornerBurst.IsChecked ?? false;
            App.Settings?.Save();
        }

        // Outlined style swaps the rendered element type, so a running instance restarts.
        private void ChkOutline_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.BouncingTextOutline = ChkOutline.IsChecked ?? false;
            App.Settings?.Save();
            SafeRestart();
        }

        // The second logo needs its own window visuals, so a running instance restarts.
        private void ChkSecondText_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.BouncingTextSecondText = ChkSecondText.IsChecked ?? false;
            App.Settings?.Save();
            SafeRestart();
        }

        private void ChkAlwaysOnTop_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.BouncingTextAlwaysOnTop = ChkAlwaysOnTop.IsChecked ?? false;
            App.Settings?.Save();
            SafeRefresh(); // applies mid-video (pause/resume the loop accordingly)
        }

        private void BtnEditPhrases_Click(object sender, RoutedEventArgs e)
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            var editor = new TextEditorDialog("Bouncing Text Phrases", s.BouncingTextPool)
            {
                Owner = Window.GetWindow(this) ?? Application.Current.MainWindow
            };
            if (editor.ShowDialog() == true && editor.ResultData != null)
            {
                s.BouncingTextPool = editor.ResultData;
                App.Settings?.Save();
                App.Logger?.Information("Bouncing text phrases updated: {Count} items", editor.ResultData.Count);
            }
        }

        private static void SafeRefresh()
        {
            try { App.BouncingText?.Refresh(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "BouncingText Refresh failed"); }
        }

        private static void SafeRestart()
        {
            try { App.BouncingText?.Restart(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "BouncingText Restart failed"); }
        }

        private void UpdateFixedColorPanel()
        {
            PanelFixedColor.Visibility = CmbColorMode.SelectedIndex == 1
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void UpdateSwatch()
        {
            var (r, g, b) = EffectiveColor();
            ColorSwatch.Background = new SolidColorBrush(Color.FromRgb(r, g, b));
        }

        // The color Fixed mode actually renders: the user's pick if set, else hot pink.
        // Mirrors BouncingTextService.GetFixedColor.
        private static (byte R, byte G, byte B) EffectiveColor()
        {
            var custom = App.Settings?.Current?.BouncingTextFixedColor;
            if (TryParseHex(custom, out var rgb)) return rgb;
            return ((byte)255, (byte)105, (byte)180);
        }

        private static bool TryParseHex(string? hex, out (byte R, byte G, byte B) rgb)
        {
            rgb = (255, 105, 180);
            if (string.IsNullOrWhiteSpace(hex)) return false;
            hex = hex.Trim().TrimStart('#');
            if (hex.Length != 6) return false;
            try
            {
                rgb = (Convert.ToByte(hex.Substring(0, 2), 16),
                       Convert.ToByte(hex.Substring(2, 2), 16),
                       Convert.ToByte(hex.Substring(4, 2), 16));
                return true;
            }
            catch { return false; }
        }
    }
}
