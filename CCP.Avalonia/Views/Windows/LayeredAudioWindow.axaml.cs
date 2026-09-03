using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// Config window for suggestion #659 "Audio Layers". Maintains the user's list of looping
    /// audio tracks (add file / per-track enable + volume / remove) plus a master enable and
    /// master volume.
    ///
    /// PORTED from ConditioningControlPanel/Windows/LayeredAudioWindow.xaml.cs. Deviations:
    ///  - App.Settings and App.LayeredAudio are both still in the WPF head, so the track list is
    ///    an in-memory placeholder seeded with two sample tracks, and every Save/Start/Stop/
    ///    Restart/SetVolumeLive call is a marked stub. The card building, the toggles, the
    ///    dim-when-off body, the empty state and its master-on warning are ported for real -
    ///    they are the whole point of this window and none of them touches a service.
    ///  - The file picker uses Avalonia's StorageProvider (async) instead of
    ///    Microsoft.Win32.OpenFileDialog, and adds to the placeholder list.
    ///  - The static Open(DependencyObject) helper is DROPPED. Its three jobs - best-effort
    ///    Owner, single-instance re-surface, and EnsureOnScreen against SystemParameters'
    ///    virtual-desktop metrics - are WPF window-manager repairs with no caller in this head.
    ///    It comes back (over Avalonia's Screens API) with the call sites that need it.
    ///  - TryFindResource("ToggleStyle") returns a ControlTheme here, assigned to Theme.
    ///  - Checked/Unchecked collapse into IsCheckedChanged; Slider.ValueChanged carries
    ///    RangeBaseValueChangedEventArgs.
    /// </summary>
    public partial class LayeredAudioWindow : Window
    {
        private static readonly Color Accent = Color.FromRgb(0xFF, 0x69, 0xB4);

        // Slider drags apply live (no rebuild); debounce the settings save so a drag
        // doesn't hammer the disk.
        private readonly DispatcherTimer _saveDebounce;
        // Kept from the original: the master slider's Value="70" is set during XAML load, and the
        // handlers must stay inert until LoadMasterControls owns the flag.
        private bool _loading = true;

        // ponytail: needs App.Settings.Current.AudioLayers, wired when settings move to Core.
        // Two sample tracks so --render-view draws the row cards rather than the empty state;
        // the empty state and its master-on warning are still reachable by removing both.
        private readonly List<AudioLayerTrack> _tracks = new()
        {
            new AudioLayerTrack { Path = "/music/rain-loop.mp3", Volume = 70, Enabled = true },
            new AudioLayerTrack { Path = "/music/deep-hum.ogg", Volume = 45, Enabled = false },
        };

        private readonly CheckBox _chkMasterEnable, _chkAudioOnly;
        private readonly Slider _sliderMaster;
        private readonly TextBlock _txtMaster;
        private readonly StackPanel _slotsPanel;

        public LayeredAudioWindow()
        {
            AvaloniaXamlLoader.Load(this);

            _chkMasterEnable = this.FindControl<CheckBox>("ChkMasterEnable")!;
            _chkAudioOnly = this.FindControl<CheckBox>("ChkAudioOnly")!;
            _sliderMaster = this.FindControl<Slider>("SliderMaster")!;
            _txtMaster = this.FindControl<TextBlock>("TxtMaster")!;
            _slotsPanel = this.FindControl<StackPanel>("SlotsPanel")!;

            _saveDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _saveDebounce.Tick += (_, _) => { _saveDebounce.Stop(); SaveSettings(); };

            _chkMasterEnable.IsCheckedChanged += (_, _) => ChkMasterEnable_Changed();
            _chkAudioOnly.IsCheckedChanged += (_, _) => ChkAudioOnly_Changed();
            _sliderMaster.AddHandler(RangeBase.ValueChangedEvent, SliderMaster_Changed);
            this.FindControl<Button>("BtnAddTrack")!.Click += async (_, _) => await AddTrackAsync();
            this.FindControl<Button>("BtnClose")!.Click += (_, _) => Close();

            LoadMasterControls();
            BuildRows();
        }

        private List<AudioLayerTrack> Tracks() => _tracks;

        private void LoadMasterControls()
        {
            _loading = true;
            // ponytail: needs App.Settings.Current (AudioLayersEnabled / AudioLayersMasterVolume /
            // AudioOnlySession). The XAML defaults stand in until settings move to Core.
            _chkMasterEnable.IsChecked = true;
            _sliderMaster.Value = 70;
            _txtMaster.Text = $"{(int)_sliderMaster.Value}%";
            _chkAudioOnly.IsChecked = false;
            _loading = false;
        }

        private void BuildRows()
        {
            _slotsPanel.Children.Clear();
            var list = Tracks();
            if (list.Count == 0)
            {
                _slotsPanel.Children.Add(new TextBlock
                {
                    Text = "No tracks yet. Add an audio file to get started.",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x90)),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(2, 4, 0, 0),
                });
                // "Is this button bugged?" support-cost guard: with the master on and nothing
                // in the list the feature is audibly indistinguishable from broken.
                if (_chkMasterEnable.IsChecked == true)
                {
                    _slotsPanel.Children.Add(new TextBlock
                    {
                        Text = "⚠ The master switch is on, but with no layers there is nothing to play - it stays silent until you add a file.",
                        Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xA8, 0x4C)),
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(2, 8, 0, 0),
                    });
                }
                return;
            }
            for (int i = 0; i < list.Count; i++)
                _slotsPanel.Children.Add(BuildRowCard(list[i]));
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
            if (this.TryFindResource("ToggleStyle", out var toggleTheme) && toggleTheme is ControlTheme ct)
                toggle.Theme = ct;
            Grid.SetColumn(toggle, 0);
            header.Children.Add(toggle);

            var name = new TextBlock
            {
                Text = track.DisplayName,
                Foreground = Brushes.White,
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            if (!string.IsNullOrEmpty(track.Path)) ToolTip.SetTip(name, track.Path);
            Grid.SetColumn(name, 1);
            header.Children.Add(name);

            var remove = new Button
            {
                Content = "✕",
                Width = 26,
                Height = 26,
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Color.FromRgb(0x33, 0x1A, 0x2A)),
                Foreground = new SolidColorBrush(Accent),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x5A, 0x2A, 0x40)),
                BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand),
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTip.SetTip(remove, "Remove this track");
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
                FontWeight = FontWeight.SemiBold,
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
            volSlider.AddHandler(RangeBase.ValueChangedEvent, (EventHandler<RangeBaseValueChangedEventArgs>)((_, e) =>
            {
                track.Volume = (int)e.NewValue;
                volValue.Text = $"{track.Volume}%";
                // ponytail: needs App.LayeredAudio.SetTrackVolumeLive - live per-track gain, no
                // graph rebuild, so drags stay smooth.
                SaveDebounced();
            }));
            Grid.SetColumn(volSlider, 1);
            volGrid.Children.Add(volSlider);
            volGrid.Children.Add(volValue);
            body.Children.Add(volGrid);

            body.IsEnabled = track.Enabled;
            body.Opacity = track.Enabled ? 1.0 : 0.45;
            root.Children.Add(body);

            toggle.IsCheckedChanged += (_, _) =>
            {
                var on = toggle.IsChecked == true;
                track.Enabled = on;
                body.IsEnabled = on;
                body.Opacity = on ? 1.0 : 0.45;
                ApplyStructural(); // enable/disable adds/removes a mixer input -> rebuild
            };

            remove.Click += (_, _) =>
            {
                Tracks().Remove(track);
                BuildRows();
                ApplyStructural();
            };

            card.Child = root;
            return card;
        }

        private async System.Threading.Tasks.Task AddTrackAsync()
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Add an audio layer",
                AllowMultiple = true,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Audio Files")
                    {
                        Patterns = new[] { "*.mp3", "*.wav", "*.ogg", "*.flac", "*.m4a", "*.aac", "*.wma" }
                    },
                    FilePickerFileTypes.All,
                },
            });
            if (files.Count == 0) return;

            foreach (var file in files)
                Tracks().Add(new AudioLayerTrack { Path = file.TryGetLocalPath() ?? file.Name, Volume = 70, Enabled = true });

            BuildRows();
            ApplyStructural();
        }

        private void ChkMasterEnable_Changed()
        {
            if (_loading) return;
            // ponytail: needs App.Settings (AudioLayersEnabled) and App.LayeredAudio.Start/Stop.
            SaveSettings();

            // The empty-state warning above depends on this toggle.
            if (Tracks().Count == 0) BuildRows();
        }

        private void ChkAudioOnly_Changed()
        {
            if (_loading) return;
            // ponytail: needs App.Settings (AudioOnlySession).
            SaveSettings();
        }

        private void SliderMaster_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_loading) return;
            // ponytail: needs App.Settings (AudioLayersMasterVolume) and
            // App.LayeredAudio.SetMasterVolumeLive.
            _txtMaster.Text = $"{(int)e.NewValue}%";
            SaveDebounced();
        }

        /// <summary>A structural change (add/remove/enable): rebuild the mixer if it's on.</summary>
        private void ApplyStructural()
        {
            SaveSettings();
            // ponytail: needs App.LayeredAudio.Restart when AudioLayersEnabled is true.
        }

        /// <summary>ponytail: needs App.Settings.Save, wired when settings move to Core.</summary>
        private void SaveSettings() { }

        private void SaveDebounced()
        {
            _saveDebounce.Stop();
            _saveDebounce.Start();
        }
    }
}
