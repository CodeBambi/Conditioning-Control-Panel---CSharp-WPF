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
                // "Is this button bugged?" support-cost guard: with the master on and nothing
                // in the list the feature is audibly indistinguishable from broken.
                if (ChkMasterEnable.IsChecked == true)
                {
                    SlotsPanel.Children.Add(new TextBlock
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

            // The empty-state warning above depends on this toggle.
            if (Tracks().Count == 0) BuildRows();
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

        // ---------------------------------------------------------------------------------
        //  Opening the window  (field report v6.8.2: "the Audio Layers button does nothing")
        //
        //  Both call sites (Settings > Audio, and the old Home dashboard card) used to do
        //  `new LayeredAudioWindow { Owner = Window.GetWindow(this) }.Show()` inside a
        //  try/catch that only logged at Warning. Three separate ways that reads as a dead
        //  button, all of them closed here:
        //
        //   1. Setting Owner throws (WPF refuses an owner that has never been shown, and
        //      throws again if the owner is mid-close). That killed the whole open even
        //      though the window itself was fine - Owner is best-effort now, and the window
        //      still opens unowned.
        //   2. WindowStartupLocation=CenterOwner does its own arithmetic off the owner's
        //      Left/Top, which are the RESTORE bounds while the owner is maximized, against
        //      the maximized ActualWidth. A main window whose restore position sits toward
        //      the right of a monitor therefore centres this one clean off the desktop, and
        //      WPF does not clamp - EnsureOnScreen pulls it back after Show().
        //   3. Any other construction failure was a Warning in a log nobody reads. It is an
        //      Error plus a message box now: the user gets told, not left clicking a brick.
        //
        //  Also single-instance: a second click surfaces the window that is already up
        //  instead of stacking another copy on top of it.
        // ---------------------------------------------------------------------------------

        private static LayeredAudioWindow? _open;

        /// <summary>
        /// Open (or re-surface) the Audio Layers window. <paramref name="context"/> is any
        /// element inside the window that should own it; null falls back to MainWindow.
        /// Never throws.
        /// </summary>
        internal static void Open(DependencyObject? context)
        {
            LayeredAudioWindow? win = null;
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;

                if (_open != null)
                {
                    if (_open.WindowState == WindowState.Minimized) _open.WindowState = WindowState.Normal;
                    _open.Show();
                    _open.Activate();
                    EnsureOnScreen(_open);
                    return;
                }

                win = new LayeredAudioWindow();

                // Best-effort ownership: a bad owner must never cost the user the window.
                try
                {
                    var owner = (context != null ? Window.GetWindow(context) : null)
                                ?? Application.Current?.MainWindow;
                    if (owner != null && owner.IsLoaded && owner.IsVisible && !ReferenceEquals(owner, win))
                        win.Owner = owner;
                }
                catch (Exception ownerEx)
                {
                    App.Logger?.Warning(ownerEx,
                        "[AudioLayers] Could not attach the window to an owner; opening it unowned.");
                }

                // CenterOwner with no owner silently degrades to the primary screen's top-left.
                if (win.Owner == null) win.WindowStartupLocation = WindowStartupLocation.CenterScreen;

                var opened = win;
                opened.Closed += (_, _) => { if (ReferenceEquals(_open, opened)) _open = null; };
                _open = opened;

                opened.Show();
                opened.Activate();
                EnsureOnScreen(opened);
                App.Logger?.Information("[AudioLayers] Window opened at {Left},{Top}.", opened.Left, opened.Top);
            }
            catch (Exception ex)
            {
                _open = null;
                try { win?.Close(); } catch { }
                App.Logger?.Error(ex, "[AudioLayers] Could not open the Audio Layers window.");
                try
                {
                    MessageBox.Show(
                        "Audio Layers could not open.\n\n" + ex.Message +
                        "\n\nThe full details are in the app log - please include them if you report this.",
                        "Audio Layers", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                catch { }
            }
        }

        /// <summary>
        /// Pull a just-shown window back inside the virtual desktop. Guards the CenterOwner
        /// arithmetic above (and a position inherited from a monitor that is gone) from
        /// parking the window where the user can never reach it.
        /// </summary>
        private static void EnsureOnScreen(Window win)
        {
            try
            {
                double vl = SystemParameters.VirtualScreenLeft;
                double vt = SystemParameters.VirtualScreenTop;
                double vw = SystemParameters.VirtualScreenWidth;
                double vh = SystemParameters.VirtualScreenHeight;
                if (vw <= 0 || vh <= 0) return;

                double w = win.ActualWidth > 0 ? win.ActualWidth : win.Width;
                double h = win.ActualHeight > 0 ? win.ActualHeight : win.Height;
                double left = win.Left, top = win.Top;
                if (double.IsNaN(w) || double.IsNaN(h) || w <= 0 || h <= 0) return;
                if (double.IsNaN(left) || double.IsNaN(top)) return;

                // Clamp so the whole window sits in the virtual desktop when it fits, and so
                // its top-left corner does at minimum when it does not.
                double newLeft = Math.Min(Math.Max(left, vl), Math.Max(vl, vl + vw - w));
                double newTop = Math.Min(Math.Max(top, vt), Math.Max(vt, vt + vh - h));
                if (Math.Abs(newLeft - left) < 0.5 && Math.Abs(newTop - top) < 0.5) return;

                win.Left = newLeft;
                win.Top = newTop;
                App.Logger?.Warning(
                    "[AudioLayers] Window opened off-screen at {OldLeft},{OldTop} (virtual desktop {VL},{VT} {VW}x{VH}); moved it to {NewLeft},{NewTop}.",
                    left, top, vl, vt, vw, vh, newLeft, newTop);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[AudioLayers] On-screen check failed.");
            }
        }
    }
}
