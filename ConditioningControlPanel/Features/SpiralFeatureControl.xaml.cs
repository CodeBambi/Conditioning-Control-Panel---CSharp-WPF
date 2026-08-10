using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Features
{
    /// <summary>
    /// Popup-hostable control for the Spiral Overlay feature.
    /// Reads/writes App.Settings.Current.SpiralEnabled / SpiralOpacity / SpiralPath
    /// and stays in sync with external changes (Intensity Ramp, presets, sessions)
    /// via INotifyPropertyChanged.
    /// </summary>
    public partial class SpiralFeatureControl : UserControl, ISettingsRebindable
    {
        private bool _isLoading = true;
        private bool _monitorPopulating; // guards the monitor combo while it is rebuilt

        public SpiralFeatureControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        // Tracks WHICH AppSettings instance the hook is attached to, so a cloud restore - which
        // SWAPS the instance - can be followed instead of leaving this permanently-mounted rack
        // panel listening to, and displaying, the discarded object. See ISettingsRebindable.
        private SettingsHook? _settingsHook;

        /// <inheritdoc/>
        public void RebindToCurrentSettings()
        {
            (_settingsHook ??= new SettingsHook(OnSettingsPropertyChanged)).Rebind();
            LoadFromSettings();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            RebindToCurrentSettings();
            RefreshLibrary();

            // Loom saves/deletes (game pane or the main-app Loom window) show up live.
            Services.Chaos.DtrhLoomStore.Changed += OnLoomStoreChanged;
            // The "Default" card's thumbnail comes from ModResourceResolver.ResolveSpiralUri();
            // the rack hosts this control permanently, so a mod switch must repaint the library
            // (a popup instance was rebuilt on every open and never saw a switch).
            if (App.Mods != null) App.Mods.ModChanged += OnModChanged;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _settingsHook?.Unhook();
            Services.Chaos.DtrhLoomStore.Changed -= OnLoomStoreChanged;
            if (App.Mods != null) App.Mods.ModChanged -= OnModChanged;
        }

        private void OnModChanged(object? sender, Models.ModPackage mod)
        {
            Dispatcher.BeginInvoke(new Action(RefreshLibrary));
        }

        private void LoadFromSettings()
        {
            var s = App.Settings?.Current;
            if (s == null) return;

            _isLoading = true;
            try
            {
                ChkEnable.IsChecked = s.SpiralEnabled;
                ChkRandomize.IsChecked = s.SpiralRandomize;
                SliderOpacity.Value = s.SpiralOpacity;
                TxtOpacity.Text = $"{s.SpiralOpacity}%";
                PopulateMonitors();
            }
            finally
            {
                _isLoading = false;
            }
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Reflect external writes (Ramp, presets, session engine) back into our UI.
            if (e.PropertyName == nameof(Models.AppSettings.SpiralEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.SpiralOpacity) ||
                e.PropertyName == nameof(Models.AppSettings.SpiralRandomize) ||
                e.PropertyName == nameof(Models.AppSettings.SpiralTargetMonitor))
            {
                Dispatcher.BeginInvoke(new Action(LoadFromSettings));
            }
            else if (e.PropertyName == nameof(Models.AppSettings.SpiralPath))
            {
                Dispatcher.BeginInvoke(new Action(UpdateSelectionHighlight));
                // Corner-GIF slots left on "built-in" follow the pool selection, so a spiral
                // change must re-render them too (RefreshOverlays no-ops if none are enabled).
                try { App.CornerGif?.RefreshOverlays(); } catch { /* overlay refresh best-effort */ }
            }
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

        private void ChkRandomize_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;

            // Takes effect on the next spiral overlay/session start (never mid-run — the decoded
            // frame cache is keyed by path, so re-picking live would cause a hitch).
            s.SpiralRandomize = ChkRandomize.IsChecked ?? false;
            App.Settings?.Save();
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

        // ── Display monitor picker (#639) ─────────────────────────────────

        /// <summary>Rebuild the monitor dropdown from the current display topology and select the
        /// entry matching the saved <see cref="Models.AppSettings.SpiralTargetMonitor"/>. A saved
        /// index that no longer exists (unplugged monitor) matches nothing and shows "Default"
        /// WITHOUT writing back (the populate guard blocks SelectionChanged), so the target survives
        /// a reconnect.</summary>
        private void PopulateMonitors()
        {
            if (CmbMonitor == null) return;
            int saved = App.Settings?.Current?.SpiralTargetMonitor ?? App.MonitorTargetFollowGlobal;
            _monitorPopulating = true;
            try
            {
                CmbMonitor.Items.Clear();
                CmbMonitor.Items.Add(new ComboBoxItem { Content = Loc.Get("monitor_target_default"), Tag = App.MonitorTargetFollowGlobal });
                CmbMonitor.Items.Add(new ComboBoxItem { Content = Loc.Get("monitor_target_all"), Tag = App.MonitorTargetAll });

                var screens = App.GetAllScreensCached();
                string monitorLabel = Loc.Get("monitor_label");
                string primaryMarker = Loc.Get("monitor_primary_marker");
                for (int i = 0; i < screens.Length; i++)
                {
                    var b = screens[i].Bounds;
                    string prefix = screens[i].Primary ? primaryMarker + ", " : "";
                    CmbMonitor.Items.Add(new ComboBoxItem
                    {
                        Content = $"{monitorLabel} {i + 1} ({prefix}{b.Width}x{b.Height})",
                        Tag = i
                    });
                }

                ComboBoxItem? match = null;
                foreach (ComboBoxItem it in CmbMonitor.Items)
                    if (it.Tag is int t && t == saved) { match = it; break; }
                CmbMonitor.SelectedItem = match ?? (CmbMonitor.Items.Count > 0 ? CmbMonitor.Items[0] : null);
            }
            finally { _monitorPopulating = false; }
        }

        // Re-enumerate on open so a monitor plugged in since load appears without reopening the card.
        private void CmbMonitor_DropDownOpened(object sender, EventArgs e)
        {
            App.InvalidateScreenCache();
            PopulateMonitors();
        }

        private void CmbMonitor_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_monitorPopulating || _isLoading) return;
            if (CmbMonitor.SelectedItem is not ComboBoxItem item || item.Tag is not int target) return;

            var s = App.Settings?.Current;
            if (s == null) return;
            if (s.SpiralTargetMonitor == target) return;

            s.SpiralTargetMonitor = target;
            App.Settings?.Save();

            // Compositor picks the new target up next frame (per-monitor ShouldRenderOnScreen);
            // RefreshOverlays reconciles the legacy per-screen windows.
            try { App.Overlay?.RefreshOverlays(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "Spiral monitor: RefreshOverlays failed"); }
        }

        // ── Spiral library ────────────────────────────────────────────────

        private static readonly string[] SpiralImageExts =
            { ".gif", ".png", ".jpg", ".jpeg", ".webp", ".bmp" };
        private static readonly string[] SpiralVideoExts =
            { ".mp4", ".webm", ".mov", ".avi", ".mkv" };

        private static readonly Color SelectedAccent = Color.FromRgb(0xFF, 0x69, 0xB4);
        private static readonly Color IdleAccent = Color.FromRgb(0x33, 0x33, 0x3A);

        /// <summary>User spiral folder: %LOCALAPPDATA%/ConditioningControlPanel/Spirals.</summary>
        private static string SpiralsFolderPath => Path.Combine(App.UserDataPath, "Spirals");

        private static string NormPath(string? p)
        {
            if (string.IsNullOrWhiteSpace(p)) return "";
            try { return Path.GetFullPath(p).TrimEnd('\\', '/'); }
            catch { return p.Trim(); }
        }

        /// <summary>
        /// Rebuilds the spiral preview gallery: a "Default" card for the built-in
        /// spiral plus one card per file dropped into the Spirals folder.
        /// </summary>
        private void RefreshLibrary()
        {
            if (SpiralLibraryPanel == null) return;
            SpiralLibraryPanel.Children.Clear();

            // Built-in spiral (active when SpiralPath is empty / missing).
            string? defaultThumb = null;
            try { defaultThumb = ModResourceResolver.ResolveSpiralUri(); }
            catch { /* fall back to glyph */ }
            SpiralLibraryPanel.Children.Add(BuildSpiralCard("", "Default", defaultThumb));

            int fileCount = 0;
            try
            {
                var folder = SpiralsFolderPath;
                if (Directory.Exists(folder))
                {
                    var files = Directory.EnumerateFiles(folder)
                        .Where(f => SpiralImageExts.Contains(Path.GetExtension(f).ToLowerInvariant()) ||
                                    SpiralVideoExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                        .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase);

                    foreach (var file in files)
                    {
                        fileCount++;
                        bool isVideo = SpiralVideoExts.Contains(Path.GetExtension(file).ToLowerInvariant());
                        SpiralLibraryPanel.Children.Add(
                            BuildSpiralCard(file, Path.GetFileNameWithoutExtension(file),
                                            isVideo ? null : file));
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Spiral library: enumeration failed");
            }

            if (SpiralEmptyState != null)
                SpiralEmptyState.Visibility = fileCount == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Builds a clickable preview card. <paramref name="path"/> is the spiral file
        /// path ("" for the built-in default). <paramref name="thumbUri"/> is a file/pack
        /// URI to render as a thumbnail, or null to show a glyph (video / unloadable).
        /// </summary>
        private Border BuildSpiralCard(string path, string display, string? thumbUri)
        {
            var card = new Border
            {
                Width = 120,
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E)),
                BorderBrush = new SolidColorBrush(IdleAccent),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 8, 8),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = path,
                ToolTip = string.IsNullOrEmpty(path) ? "Built-in spiral" : path,
            };

            var stack = new StackPanel();

            var thumbHost = new Border
            {
                Height = 80,
                Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x0A, 0x14)),
                CornerRadius = new CornerRadius(6, 6, 0, 0),
                ClipToBounds = true,
            };
            if (thumbUri != null)
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(thumbUri);
                    bmp.DecodePixelWidth = 120;
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                    bmp.EndInit();
                    bmp.Freeze();
                    thumbHost.Child = new Image { Source = bmp, Stretch = Stretch.UniformToFill };
                }
                catch
                {
                    thumbHost.Child = SpiralGlyph("🌀");
                }
            }
            else
            {
                thumbHost.Child = SpiralGlyph("🎬");
            }
            stack.Children.Add(thumbHost);

            stack.Children.Add(new TextBlock
            {
                Text = display,
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(6, 6, 6, 8),
            });

            card.Child = stack;
            card.MouseLeftButtonUp += (_, _) => SelectSpiral(path);
            ApplyHighlight(card);
            return card;
        }

        private static TextBlock SpiralGlyph(string glyph) => new TextBlock
        {
            Text = glyph,
            FontSize = 32,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
        };

        /// <summary>Sets the chosen spiral as the single active spiral.</summary>
        private void SelectSpiral(string path)
        {
            var s = App.Settings?.Current;
            if (s == null) return;

            // Clicking a missing file is a no-op (keeps the previous selection).
            if (!string.IsNullOrEmpty(path) && !File.Exists(path)) return;

            if (NormPath(s.SpiralPath) == NormPath(path)) return; // already active

            s.SpiralPath = path; // "" => built-in default
            App.Settings?.Save();

            UpdateSelectionHighlight();

            try
            {
                App.Overlay?.RefreshOverlays();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Spiral select: RefreshOverlays failed");
            }
        }

        private void UpdateSelectionHighlight()
        {
            if (SpiralLibraryPanel == null) return;
            foreach (var child in SpiralLibraryPanel.Children)
                if (child is Border b)
                    ApplyHighlight(b);
        }

        private void ApplyHighlight(Border card)
        {
            var current = NormPath(App.Settings?.Current?.SpiralPath);
            var tag = NormPath(card.Tag as string);
            // The Default card (empty tag) is active when no valid custom spiral is set.
            bool defaultActive = string.IsNullOrEmpty(current) ||
                                 !File.Exists(App.Settings?.Current?.SpiralPath ?? "");
            bool selected = string.IsNullOrEmpty(tag) ? defaultActive : tag == current;
            card.BorderBrush = new SolidColorBrush(selected ? SelectedAccent : IdleAccent);
        }

        private void BtnOpenSpiralFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var folder = SpiralsFolderPath;
                Directory.CreateDirectory(folder);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Spiral library: open folder failed");
            }
        }

        private void BtnRefreshSpirals_Click(object sender, RoutedEventArgs e) => RefreshLibrary();

        /// <summary>Open THE LOOM editor window (the enhanced spiral creator).
        /// Saves land in the Spirals folder; DtrhLoomStore.Changed refreshes us live.</summary>
        private void BtnOpenLoom_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Services.Chaos.LoomHostService.Launch();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Spiral card: Loom launch failed");
            }
        }

        /// <summary>Open the standalone corner-GIF overlay config window (two pinnable corners,
        /// independent of any running session — driven by App.CornerGif).</summary>
        private void BtnCornerGifs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new CornerGifWindow
                {
                    Owner = Window.GetWindow(this),
                };
                win.Show();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Spiral card: Corner GIF window launch failed");
            }
        }

        private void OnLoomStoreChanged()
        {
            // Raised on the loom window's bridge thread contractually near the UI
            // thread, but marshal defensively - and the control may be unloading.
            Dispatcher.BeginInvoke(new Action(RefreshLibrary));
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

                // Reflect the new choice in the gallery (highlights it if it lives
                // in the Spirals folder, otherwise just clears the Default highlight).
                RefreshLibrary();

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
