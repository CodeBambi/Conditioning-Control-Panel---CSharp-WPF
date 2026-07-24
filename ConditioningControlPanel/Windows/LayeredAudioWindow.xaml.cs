using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Config window for suggestion #659 "Audio Layers". Maintains the user's list of looping
    /// audio tracks (add file / per-track enable + volume / remove) plus a master enable and
    /// master volume, and live-applies every change through <see cref="App.LayeredAudio"/>.
    /// Mirrors <see cref="CornerGifWindow"/>'s manual card-building idiom.
    /// </summary>
    public partial class LayeredAudioWindow : Window
    {
        private static readonly Color Accent = Color.FromRgb(0xFF, 0x69, 0xB4);

        // Slider drags apply live (no rebuild); debounce the settings save so a drag
        // doesn't hammer the disk.
        private readonly DispatcherTimer _saveDebounce;
        private bool _loading;

        public LayeredAudioWindow()
        {
            InitializeComponent();

            _saveDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _saveDebounce.Tick += (_, _) => { _saveDebounce.Stop(); App.Settings?.Save(); };

            LoadMasterControls();
            BuildRows();
        }

        private static List<AudioLayerTrack> Tracks()
        {
            var s = App.Settings?.Current;
            var list = s?.AudioLayers ?? new List<AudioLayerTrack>();
            if (s != null && s.AudioLayers == null) s.AudioLayers = list;
            return list;
        }

        private void LoadMasterControls()
        {
            _loading = true;
            var s = App.Settings?.Current;
            ChkMasterEnable.IsChecked = s?.AudioLayersEnabled ?? false;
            SliderMaster.Value = s?.AudioLayersMasterVolume ?? 70;
            TxtMaster.Text = $"{(int)SliderMaster.Value}%";
            ChkAudioOnly.IsChecked = s?.AudioOnlySession ?? false;
            _loading = false;
        }

        private void BuildRows()
        {
            SlotsPanel.Children.Clear();
            var list = Tracks();
            if (list.Count == 0)
            {
                SlotsPanel.Children.Add(new TextBlock
                {
                    Text = "No tracks yet. Add an audio file to get started.",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x90)),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(2, 4, 0, 0),
                });
                return;
            }
            for (int i = 0; i < list.Count; i++)
                SlotsPanel.Children.Add(BuildRowCard(list[i]));
        }

        private Border BuildRowCard(AudioLayerTrack track)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x60)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 0, 0, 10),
            };

            var root = new StackPanel();

            // Header: enable toggle + filename + remove
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var toggle = new CheckBox
            {
                IsChecked = track.Enabled,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
            };
            if (TryFindResource("ToggleStyle") is Style toggleStyle) toggle.Style = toggleStyle;
            Grid.SetColumn(toggle, 0);
            header.Children.Add(toggle);

            var name = new TextBlock
            {
                Text = track.DisplayName,
                Foreground = Brushes.White,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = string.IsNullOrEmpty(track.Path) ? null : track.Path,
            };
            Grid.SetColumn(name, 1);
            header.Children.Add(name);

            var remove = new Button
            {
                Content = "✕",
                Width = 26,
                Height = 26,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Color.FromRgb(0x33, 0x1A, 0x2A)),
                Foreground = new SolidColorBrush(Accent),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x5A, 0x2A, 0x40)),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "Remove this track",
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(remove, 2);
            header.Children.Add(remove);
            root.Children.Add(header);

            // Volume row (dimmed when the track is off)
            var body = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            var volGrid = new Grid();
            volGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            volGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            volGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var volLabel = new TextBlock
            {
                Text = "Volume",
                Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xC0)),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };
            Grid.SetColumn(volLabel, 0);
            volGrid.Children.Add(volLabel);

            var volValue = new TextBlock
            {
                Text = $"{track.Volume}%",
                Foreground = new SolidColorBrush(Accent),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Width = 40,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(volValue, 2);

            var volSlider = new Slider
            {
                Minimum = 0,
                Maximum = 100,
                Value = track.Volume,
                VerticalAlignment = VerticalAlignment.Center,
            };
            volSlider.ValueChanged += (_, e) =>
            {
                track.Volume = (int)e.NewValue;
                volValue.Text = $"{track.Volume}%";
                // Live per-track gain — no graph rebuild, so drags stay smooth.
                App.LayeredAudio?.SetTrackVolumeLive(track, track.Volume);
                SaveDebounced();
            };
            Grid.SetColumn(volSlider, 1);
            volGrid.Children.Add(volSlider);
            volGrid.Children.Add(volValue);
            body.Children.Add(volGrid);

            body.IsEnabled = track.Enabled;
            body.Opacity = track.Enabled ? 1.0 : 0.45;
            root.Children.Add(body);

            toggle.Checked += (_, _) => OnToggle(true);
            toggle.Unchecked += (_, _) => OnToggle(false);
            void OnToggle(bool on)
            {
                track.Enabled = on;
                body.IsEnabled = on;
                body.Opacity = on ? 1.0 : 0.45;
                ApplyStructural(); // enable/disable adds/removes a mixer input -> rebuild
            }

            remove.Click += (_, _) =>
            {
                var list = Tracks();
                list.Remove(track);
                var s = App.Settings?.Current;
                if (s != null) s.AudioLayers = list;
                BuildRows();
                ApplyStructural();
            };

            card.Child = root;
            return card;
        }

        private void BtnAddTrack_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Audio Files|*.mp3;*.wav;*.ogg;*.flac;*.m4a;*.aac;*.wma|All Files|*.*",
                Title = "Add an audio layer",
                Multiselect = true,
            };
            if (dlg.ShowDialog() != true) return;

            var list = Tracks();
            foreach (var file in dlg.FileNames)
                list.Add(new AudioLayerTrack { Path = file, Volume = 70, Enabled = true });

            var s = App.Settings?.Current;
            if (s != null) s.AudioLayers = list;
            BuildRows();
            ApplyStructural();
        }

        private void ChkMasterEnable_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.AudioLayersEnabled = ChkMasterEnable.IsChecked ?? false;
            App.Settings?.Save();

            if (s.AudioLayersEnabled) App.LayeredAudio?.Start();
            else App.LayeredAudio?.Stop();
        }

        private void ChkAudioOnly_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.AudioOnlySession = ChkAudioOnly.IsChecked ?? false;
            App.Settings?.Save();
        }

        private void SliderMaster_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.AudioLayersMasterVolume = (int)e.NewValue;
            TxtMaster.Text = $"{(int)e.NewValue}%";
            App.LayeredAudio?.SetMasterVolumeLive();
            SaveDebounced();
        }

        /// <summary>A structural change (add/remove/enable): rebuild the mixer if it's on.</summary>
        private void ApplyStructural()
        {
            App.Settings?.Save();
            var s = App.Settings?.Current;
            if (s?.AudioLayersEnabled == true) App.LayeredAudio?.Restart();
        }

        private void SaveDebounced()
        {
            _saveDebounce.Stop();
            _saveDebounce.Start();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
