using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Features
{
    /// <summary>
    /// Popup-hostable control for the Spiral Overlay feature.
    /// Reads/writes App.Settings.Current.SpiralEnabled / SpiralOpacity / SpiralPath
    /// and stays in sync with external changes (Intensity Ramp, presets, sessions)
    /// via INotifyPropertyChanged. Subscribes to Progression level-ups to refresh
    /// its locked/unlocked view.
    /// </summary>
    public partial class SpiralFeatureControl : UserControl
    {
        private const int UnlockLevel = 10;
        private bool _isLoading;

        public SpiralFeatureControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            LoadFromSettings();
            RefreshLockState();

            if (App.Settings?.Current is INotifyPropertyChanged inpc)
            {
                inpc.PropertyChanged += OnSettingsPropertyChanged;
            }
            if (App.Progression != null)
            {
                App.Progression.LevelUp += OnLevelUp;
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (App.Settings?.Current is INotifyPropertyChanged inpc)
            {
                inpc.PropertyChanged -= OnSettingsPropertyChanged;
            }
            if (App.Progression != null)
            {
                App.Progression.LevelUp -= OnLevelUp;
            }
        }

        private void LoadFromSettings()
        {
            var s = App.Settings?.Current;
            if (s == null) return;

            _isLoading = true;
            try
            {
                ChkEnable.IsChecked = s.SpiralEnabled;
                SliderOpacity.Value = s.SpiralOpacity;
                TxtOpacity.Text = $"{s.SpiralOpacity}%";
            }
            finally
            {
                _isLoading = false;
            }
        }

        private void RefreshLockState()
        {
            var unlocked = App.Settings?.Current?.IsLevelUnlocked(UnlockLevel) ?? false;
            LockedPanel.Visibility = unlocked ? Visibility.Collapsed : Visibility.Visible;
            UnlockedPanel.Visibility = unlocked ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Reflect external writes (Ramp, presets, session engine) back into our UI.
            if (e.PropertyName == nameof(Models.AppSettings.SpiralEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.SpiralOpacity))
            {
                Dispatcher.BeginInvoke(new Action(LoadFromSettings));
            }
            else if (e.PropertyName == nameof(Models.AppSettings.PlayerLevel) ||
                     e.PropertyName == nameof(Models.AppSettings.HighestLevelEver))
            {
                Dispatcher.BeginInvoke(new Action(RefreshLockState));
            }
        }

        private void OnLevelUp(object? sender, int newLevel)
        {
            Dispatcher.BeginInvoke(new Action(RefreshLockState));
        }

        private void ChkEnable_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;

            s.SpiralEnabled = ChkEnable.IsChecked ?? false;
            App.Settings?.Save();

            try
            {
                App.Overlay?.RefreshOverlays();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Spiral toggle: RefreshOverlays failed");
            }
        }

        private void SliderOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;

            var value = (int)e.NewValue;
            TxtOpacity.Text = $"{value}%";
            s.SpiralOpacity = value;
            App.Settings?.Save();

            try
            {
                App.Overlay?.RefreshOverlays();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Spiral opacity: RefreshOverlays failed");
            }
        }

        private void BtnSelectGif_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "GIF Files (*.gif)|*.gif|All Image Files|*.gif;*.png;*.jpg;*.jpeg",
                Title = Loc.Get("title_select_spiral_gif")
            };

            var currentPath = App.Settings?.Current?.SpiralPath;
            if (!string.IsNullOrEmpty(currentPath) && File.Exists(currentPath))
            {
                dialog.InitialDirectory = Path.GetDirectoryName(currentPath);
            }

            if (dialog.ShowDialog() == true)
            {
                var s = App.Settings?.Current;
                if (s == null) return;

                s.SpiralPath = dialog.FileName;
                App.Settings?.Save();

                try
                {
                    App.Overlay?.RefreshOverlays();
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "Spiral select: RefreshOverlays failed");
                }

                MessageBox.Show(
                    $"Selected: {Path.GetFileName(dialog.FileName)}",
                    "Spiral Selected",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
    }
}
