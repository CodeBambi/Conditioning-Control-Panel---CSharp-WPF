using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services.Super;

namespace ConditioningControlPanel.Controls
{
    /// <summary>
    /// Super Afterglow's own option box: Frequency, How many, Size and two glow colours. Writes the
    /// <c>Afterglow*</c> settings, which the driver and the layer read at every pop, so a change
    /// lands on the next pop. Shown while the Afterglow switch is on or the player is unlocked
    /// (BASIC and up, or this week's try); a locked player with the switch off never sees it.
    /// Collapsed by default behind its header.
    /// </summary>
    public partial class AfterglowOptionsBox : UserControl
    {
        private bool _loading = true;   // no writes while InitializeComponent sets the XAML values

        public AfterglowOptionsBox()
        {
            InitializeComponent();
            _loading = false;
            Loaded += (_, _) =>
            {
                SuperAccess.Changed += OnSuperChanged;
                RefreshVisibility();
                LoadValues();
            };
            Unloaded += (_, _) => SuperAccess.Changed -= OnSuperChanged;
        }

        /// <summary>True while the body is open.</summary>
        public bool IsOpen => Body.Visibility == Visibility.Visible;

        /// <summary>Open or close the body (the header click does this).</summary>
        public void SetOpen(bool open)
        {
            Body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            ChevronTurn.Angle = open ? 90 : 0;
            if (open) LoadValues();
        }

        private void OnSuperChanged(SuperEffect effect)
        {
            if (effect != SuperEffect.Afterglow) return;
            DispatcherHelper.RunOnUI(RefreshVisibility);
        }

        /// <summary>Visible while switched on or unlocked; the same gate the strip draws.</summary>
        public void RefreshVisibility()
        {
            bool show = SuperAccess.IsSwitchedOn(SuperEffect.Afterglow) || SuperAccess.IsUnlocked(SuperEffect.Afterglow);
            Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        private void LoadValues()
        {
            var s = App.Settings?.Current;
            _loading = true;
            try
            {
                SliderFrequency.Value = s?.AfterglowFrequency ?? AfterglowOptions.FrequencyDefault;
                SliderCount.Value = s?.AfterglowCount ?? AfterglowOptions.CountDefault;
                SliderSize.Value = s?.AfterglowSize ?? AfterglowOptions.SizeDefault;
                ChkOnly.IsChecked = s?.AfterglowOnly == true;
                SliderDuration.Value = s?.AfterglowDuration ?? AfterglowOptions.DurationDefault;
                SliderOpacity.Value = s?.AfterglowOpacity ?? AfterglowOptions.OpacityDefault;
                SliderTilt.Value = s?.AfterglowTilt ?? AfterglowOptions.TiltSliderDefault;
                TxtTilt.Text = ((int)SliderTilt.Value).ToString();
                TxtDuration.Text = ((int)SliderDuration.Value).ToString();
                TxtOpacity.Text = ((int)SliderOpacity.Value) + "%";
                TxtFrequency.Text = ((int)SliderFrequency.Value).ToString();
                TxtCount.Text = ((int)SliderCount.Value).ToString();
                TxtSize.Text = ((int)SliderSize.Value).ToString();
            }
            finally { _loading = false; }
            UpdateSwatches();
        }

        private void SliderDuration_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtDuration == null) return;
            TxtDuration.Text = ((int)SliderDuration.Value).ToString();
            Write(s => s.AfterglowDuration = (int)SliderDuration.Value);
        }

        private void SliderOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtOpacity == null) return;
            TxtOpacity.Text = ((int)SliderOpacity.Value) + "%";
            Write(s => s.AfterglowOpacity = (int)SliderOpacity.Value);
        }

        private void SliderTilt_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtTilt == null) return;
            TxtTilt.Text = ((int)SliderTilt.Value).ToString();
            Write(s => s.AfterglowTilt = (int)SliderTilt.Value);
        }

        private void ChkOnly_Changed(object sender, RoutedEventArgs e)
            => Write(s => s.AfterglowOnly = ChkOnly.IsChecked == true);

        private void BtnHeader_Click(object sender, RoutedEventArgs e) => SetOpen(!IsOpen);

        private void SliderFrequency_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            var v = (int)e.NewValue;
            if (TxtFrequency != null) TxtFrequency.Text = v.ToString();
            Write(s => s.AfterglowFrequency = v);
        }

        private void SliderCount_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            var v = (int)e.NewValue;
            if (TxtCount != null) TxtCount.Text = v.ToString();
            Write(s => s.AfterglowCount = v);
        }

        private void SliderSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            var v = (int)e.NewValue;
            if (TxtSize != null) TxtSize.Text = v.ToString();
            Write(s => s.AfterglowSize = v);
        }

        private void Write(Action<Models.AppSettings> set)
        {
            if (_loading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            set(s);
            App.Settings?.Save();
        }

        private void BtnColorA_Click(object sender, RoutedEventArgs e)
            => PickColour(s => s.AfterglowColorA, (s, v) => s.AfterglowColorA = v, AfterglowOptions.ColorADefault);

        private void BtnColorB_Click(object sender, RoutedEventArgs e)
            => PickColour(s => s.AfterglowColorB, (s, v) => s.AfterglowColorB = v, AfterglowOptions.ColorBDefault);

        /// <summary>The same colour dialog the bouncing text's fixed colour uses.</summary>
        private void PickColour(Func<Models.AppSettings, string> get, Action<Models.AppSettings, string> set, string fallback)
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            var (r, g, b) = AfterglowOptions.ParseColor(get(s), fallback);
            using var dialog = new System.Windows.Forms.ColorDialog
            {
                FullOpen = true,
                Color = System.Drawing.Color.FromArgb(r, g, b)
            };
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            set(s, $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}");
            App.Settings?.Save();
            UpdateSwatches();
        }

        private void BtnResetColors_Click(object sender, RoutedEventArgs e)
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            s.AfterglowColorA = AfterglowOptions.ColorADefault;
            s.AfterglowColorB = AfterglowOptions.ColorBDefault;
            App.Settings?.Save();
            UpdateSwatches();
        }

        private void UpdateSwatches()
        {
            var s = App.Settings?.Current;
            var a = ToColor(AfterglowOptions.ParseColor(s?.AfterglowColorA, AfterglowOptions.ColorADefault));
            var b = ToColor(AfterglowOptions.ParseColor(s?.AfterglowColorB, AfterglowOptions.ColorBDefault));
            SwatchA.Background = new SolidColorBrush(a);
            SwatchB.Background = new SolidColorBrush(b);
            // Fresh brush: the XAML one may be frozen.
            Preview.Background = new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(a, 0.2), new GradientStop(b, 0.8)
            }, new Point(0, 0), new Point(1, 0));
        }

        private static Color ToColor((byte R, byte G, byte B) c) => Color.FromRgb(c.R, c.G, c.B);
    }
}
