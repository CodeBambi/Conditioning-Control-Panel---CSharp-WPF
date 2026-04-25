using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

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
                ChkAlwaysOnTop.IsChecked = s.BouncingTextAlwaysOnTop;
            }
            finally { _isLoading = false; }
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Models.AppSettings.BouncingTextEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.BouncingTextSpeed) ||
                e.PropertyName == nameof(Models.AppSettings.BouncingTextSize) ||
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
            try { App.BouncingText?.Refresh(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "BouncingText Refresh failed"); }
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
            try { App.BouncingText?.Refresh(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "BouncingText Refresh failed"); }
            App.Settings?.Save();
        }

        private void ChkAlwaysOnTop_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.BouncingTextAlwaysOnTop = ChkAlwaysOnTop.IsChecked ?? false;
            App.Settings?.Save();
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
    }
}
