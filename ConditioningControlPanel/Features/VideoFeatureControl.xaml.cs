using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Features
{
    public partial class VideoFeatureControl : UserControl
    {
        private bool _isLoading;

        public VideoFeatureControl()
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
                ChkEnable.IsChecked = s.MandatoryVideosEnabled;
                SliderPerHour.Value = s.VideosPerHour;
                TxtPerHour.Text = s.VideosPerHour.ToString();
                ChkStrict.IsChecked = s.StrictLockEnabled;
                ChkMiniGame.IsChecked = s.AttentionChecksEnabled;
                SliderTargets.Value = s.AttentionDensity;
                TxtTargets.Text = s.AttentionDensity.ToString();
                ChkRandomize.IsChecked = s.RandomizeAttentionTargets;
                SliderDuration.Value = s.AttentionLifespan;
                TxtDuration.Text = s.AttentionLifespan.ToString();
                SliderTargetSize.Value = s.AttentionSize;
                TxtTargetSize.Text = s.AttentionSize.ToString();
            }
            finally { _isLoading = false; }
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Models.AppSettings.MandatoryVideosEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.VideosPerHour) ||
                e.PropertyName == nameof(Models.AppSettings.StrictLockEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.AttentionChecksEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.AttentionDensity) ||
                e.PropertyName == nameof(Models.AppSettings.RandomizeAttentionTargets) ||
                e.PropertyName == nameof(Models.AppSettings.AttentionLifespan) ||
                e.PropertyName == nameof(Models.AppSettings.AttentionSize))
            {
                Dispatcher.BeginInvoke(new Action(LoadFromSettings));
            }
        }

        private void ChkEnable_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.MandatoryVideosEnabled = ChkEnable.IsChecked ?? false;
            App.Settings?.Save();
        }

        private void SliderPerHour_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtPerHour.Text = v.ToString();
            s.VideosPerHour = v;
            App.Settings?.Save();
        }

        private void ChkStrict_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;

            var on = ChkStrict.IsChecked ?? false;
            if (on)
            {
                var owner = Window.GetWindow(this) ?? Application.Current.MainWindow;
                var confirmed = WarningDialog.ShowDoubleWarning(owner,
                    "Strict Lock",
                    "• You will NOT be able to skip or close videos\n" +
                    "• Videos MUST be watched to completion\n" +
                    "• The only way out is the panic key (if enabled)\n" +
                    "• This can be very intense and restrictive");

                if (!confirmed)
                {
                    ChkStrict.Checked -= ChkStrict_Changed;
                    ChkStrict.Unchecked -= ChkStrict_Changed;
                    ChkStrict.IsChecked = false;
                    ChkStrict.Checked += ChkStrict_Changed;
                    ChkStrict.Unchecked += ChkStrict_Changed;
                    return;
                }
            }

            s.StrictLockEnabled = on;
            App.Settings?.Save();
        }

        private void ChkMiniGame_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.AttentionChecksEnabled = ChkMiniGame.IsChecked ?? false;
            App.Settings?.Save();
        }

        private void SliderTargets_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtTargets.Text = v.ToString();
            s.AttentionDensity = v;
            App.Settings?.Save();
        }

        private void ChkRandomize_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.RandomizeAttentionTargets = ChkRandomize.IsChecked ?? false;
            App.Settings?.Save();
        }

        private void SliderDuration_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtDuration.Text = v.ToString();
            s.AttentionLifespan = v;
            App.Settings?.Save();
        }

        private void SliderTargetSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtTargetSize.Text = v.ToString();
            s.AttentionSize = v;
            App.Settings?.Save();
        }

        private void BtnManageAttention_Click(object sender, RoutedEventArgs e)
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            var dialog = new TextEditorDialog("Attention Targets", s.AttentionPool)
            {
                Owner = Window.GetWindow(this) ?? Application.Current.MainWindow
            };
            if (dialog.ShowDialog() == true && dialog.ResultData != null)
            {
                s.AttentionPool = dialog.ResultData;
                App.Settings?.Save();
                App.Logger?.Information("Attention pool updated: {Count} items", dialog.ResultData.Count);
            }
        }

        private void BtnAttentionStyle_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new AttentionTargetEditorDialog
            {
                Owner = Window.GetWindow(this) ?? Application.Current.MainWindow
            };
            dialog.ShowDialog();
        }

        private void BtnTestVideo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (App.Video?.IsPlaying == true)
                {
                    var result = MessageBox.Show(
                        "A video appears to be playing.\n\nIf you don't see a video, it may be stuck. Click Yes to force reset and try again.",
                        "Video Playing",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        App.Logger?.Warning("User requested force reset of stuck video state");
                        App.Video?.ForceCleanup();
                        App.InteractionQueue?.ForceReset();
                    }
                    else return;
                }

                if (App.InteractionQueue != null && !App.InteractionQueue.CanStart)
                {
                    var result = MessageBox.Show(
                        $"Another interaction is in progress ({App.InteractionQueue.CurrentInteraction}).\n\nIf this seems stuck, click Yes to force reset and try again.",
                        "Please Wait",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        App.Video?.ForceCleanup();
                        App.InteractionQueue.ForceReset();
                    }
                    else return;
                }

                App.Video?.TriggerVideo();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Error in BtnTestVideo_Click");
                MessageBox.Show($"Error triggering video: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
