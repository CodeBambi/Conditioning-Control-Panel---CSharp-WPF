using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// Spiral Overlay panel, ported from the WPF head, against <see cref="CoreSettings"/>.
    ///
    /// <para>The library is real: the folder is <c>CorePaths.UserData/Spirals</c>, which resolves
    /// itself on every platform, and the thumbnails decode through Avalonia's own bitmap loader.
    /// The file picker is <c>TopLevel.StorageProvider</c> and the folder button is
    /// <c>TopLevel.Launcher</c> - both cross-platform, so neither needs a head service.</para>
    ///
    /// <para>WPF's <c>ISettingsRebindable</c> + <c>SettingsHook</c> pair is reproduced inline: a
    /// cloud restore SWAPS the AppSettings instance, so the PropertyChanged subscription is tracked
    /// per instance and re-pointed on <c>SettingsService.CurrentReplaced</c>.</para>
    /// </summary>
    public partial class SpiralFeatureControl : UserControl
    {
        // ponytail: local copy of App.MonitorTargetFollowGlobal / App.MonitorTargetAll
        // (ConditioningControlPanel/App.ScreenResolver.cs), still in the WPF head. They are the
        // sentinels persisted in AppSettings.SpiralTargetMonitor, so both heads must agree.
        private const int MonitorTargetFollowGlobal = -1;
        private const int MonitorTargetAll = -2;

        private static readonly string[] SpiralImageExts =
            { ".gif", ".png", ".jpg", ".jpeg", ".webp", ".bmp" };
        private static readonly string[] SpiralVideoExts =
            { ".mp4", ".webm", ".mov", ".avi", ".mkv" };

        private static readonly Color SelectedAccent = Color.FromRgb(0xFF, 0x69, 0xB4);
        private static readonly Color IdleAccent = Color.FromRgb(0x33, 0x33, 0x3A);

        /// <summary>User spiral folder: <c>&lt;user data&gt;/Spirals</c>. WPF reads it off
        /// <c>App.UserDataPath</c>; <see cref="CorePaths.UserData"/> is the same folder.</summary>
        private static string SpiralsFolderPath => Path.Combine(CorePaths.UserData, "Spirals");

        private bool _isLoading = true;
        private bool _monitorPopulating;
        private AppSettings? _hooked;

        public SpiralFeatureControl()
        {
            InitializeComponent(); // generated: loads the XAML and fills the x:Name fields

            ChkEnable.IsCheckedChanged += ChkEnable_Changed;
            ChkRandomize.IsCheckedChanged += ChkRandomize_Changed;
            ChkSessionCornerGif.IsCheckedChanged += ChkSessionCornerGif_Changed;
            SliderOpacity.ValueChanged += SliderOpacity_Changed;
            CmbMonitor.DropDownOpened += (_, _) => PopulateMonitors();
            CmbMonitor.SelectionChanged += CmbMonitor_Changed;
            BtnOpenLoom.Click += (_, _) =>
            {
                // ponytail: needs Services.Chaos.LoomHostService.Launch()
                // (ConditioningControlPanel/Services/Chaos/), still in the WPF head. Its live-save
                // feed, Services.Chaos.DtrhLoomStore.Changed, is in the same place - which is why
                // this panel does not subscribe to it either.
            };
            BtnCornerGifs.Click += BtnCornerGifs_Click;
            BtnSelectGif.Click += BtnSelectGif_Click;
            BtnOpenSpiralFolder.Click += BtnOpenSpiralFolder_Click;
            BtnRefreshSpirals.Click += (_, _) => RefreshLibrary();

            LoadFromSettings();
            RefreshLibrary();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced += RebindToCurrentSettings;
            RebindToCurrentSettings();
            RefreshLibrary();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced -= RebindToCurrentSettings;
            Unhook();
            base.OnDetachedFromVisualTree(e);
        }

        /// <summary>WPF's <c>ISettingsRebindable.RebindToCurrentSettings</c>: detach from whichever
        /// instance we were on, attach to the live one, repaint from it.</summary>
        private void RebindToCurrentSettings()
        {
            Unhook();
            _hooked = CoreSettings.Current;
            _hooked.PropertyChanged += OnSettingsPropertyChanged;
            LoadFromSettings();
        }

        private void Unhook()
        {
            if (_hooked != null) _hooked.PropertyChanged -= OnSettingsPropertyChanged;
            _hooked = null;
        }

        private void LoadFromSettings()
        {
            var s = CoreSettings.Current;
            _isLoading = true;
            try
            {
                ChkEnable.IsChecked = s.SpiralEnabled;
                ChkRandomize.IsChecked = s.SpiralRandomize;
                ChkSessionCornerGif.IsChecked = s.SessionCornerGifAllowed;
                SliderOpacity.Value = s.SpiralOpacity;
                TxtOpacity.Text = $"{s.SpiralOpacity}%";
                PopulateMonitors();
            }
            finally { _isLoading = false; }
        }

        /// <summary>Reflects external writes (Ramp, presets, the session engine) back into the
        /// panel. Marshalled: those writers are not all on the UI thread.</summary>
        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppSettings.SpiralEnabled) ||
                e.PropertyName == nameof(AppSettings.SpiralOpacity) ||
                e.PropertyName == nameof(AppSettings.SpiralRandomize) ||
                e.PropertyName == nameof(AppSettings.SessionCornerGifAllowed) ||
                e.PropertyName == nameof(AppSettings.SpiralTargetMonitor))
            {
                Dispatcher.UIThread.Post(LoadFromSettings);
            }
            else if (e.PropertyName == nameof(AppSettings.SpiralPath))
            {
                Dispatcher.UIThread.Post(UpdateSelectionHighlight);
                // ponytail: WPF also re-renders the corner-GIF slots left on "built-in"
                // (App.CornerGif.RefreshOverlays, ConditioningControlPanel/Services/CornerGifService.cs),
                // still in the WPF head.
            }
        }

        // =====================================================================================
        //  toggles and sliders
        // =====================================================================================

        private void ChkEnable_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var want = ChkEnable.IsChecked ?? false;
            if (s.SpiralEnabled == want) return;   // an echo of the seed must not save
            s.SpiralEnabled = want;
            CoreSettings.Save();
            // ponytail: WPF then calls App.Overlay.RefreshOverlays()
            // (ConditioningControlPanel/Services/Notifications/OverlayService.cs), still in the WPF
            // head - the spiral overlays are Win32 layered windows with no port yet.
        }

        /// <summary>Takes effect on the next spiral overlay/session start, never mid-run: the
        /// decoded frame cache is keyed by path, so re-picking live would cause a hitch.</summary>
        private void ChkRandomize_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var want = ChkRandomize.IsChecked ?? false;
            if (s.SpiralRandomize == want) return;
            s.SpiralRandomize = want;
            CoreSettings.Save();
        }

        /// <summary>User master for the SESSION-scoped corner GIF. On WPF it is honoured LIVE - a
        /// session already on screen drops its corner overlay the moment this is unticked. The
        /// standalone Corner GIF slots are NOT touched: those are a separate surface.</summary>
        private void ChkSessionCornerGif_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var want = ChkSessionCornerGif.IsChecked ?? false;
            if (s.SessionCornerGifAllowed == want) return;
            s.SessionCornerGifAllowed = want;
            CoreSettings.Save();
            // ponytail: WPF then calls SessionEngine.Active.RefreshCornerGifPolicy()
            // (ConditioningControlPanel/Services/Session/SessionEngine.cs), still in the WPF head,
            // so the change lands on the next session rather than the running one.
        }

        private void SliderOpacity_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            var value = (int)e.NewValue;
            TxtOpacity.Text = $"{value}%";
            CoreSettings.Current.SpiralOpacity = value;
            CoreSettings.Save();
            // ponytail: WPF then calls App.Overlay.RefreshOverlays() - see ChkEnable_Changed.
        }

        // ── Display monitor picker (#639) ─────────────────────────────────

        /// <summary>Rebuild the monitor dropdown from the current display topology and select the
        /// entry matching the saved <see cref="AppSettings.SpiralTargetMonitor"/>. A saved index that
        /// no longer exists (unplugged monitor) matches nothing and shows "Default" WITHOUT writing
        /// back (the populate guard blocks SelectionChanged), so the target survives a reconnect.</summary>
        private void PopulateMonitors()
        {
            int saved = CoreSettings.Current.SpiralTargetMonitor;
            _monitorPopulating = true;
            try
            {
                CmbMonitor.Items.Clear();
                CmbMonitor.Items.Add(new ComboBoxItem { Content = Loc.Get("monitor_target_default"), Tag = MonitorTargetFollowGlobal });
                CmbMonitor.Items.Add(new ComboBoxItem { Content = Loc.Get("monitor_target_all"), Tag = MonitorTargetAll });

                var screens = ScreenList.Enumerate(this);
                string monitorLabel = Loc.Get("monitor_label");
                string primaryMarker = Loc.Get("monitor_primary_marker");
                for (int i = 0; i < screens.Count; i++)
                {
                    var b = screens[i].Bounds;
                    string prefix = screens[i].IsPrimary ? primaryMarker + ", " : "";
                    CmbMonitor.Items.Add(new ComboBoxItem
                    {
                        Content = $"{monitorLabel} {i + 1} ({prefix}{b.Width}x{b.Height})",
                        Tag = i,
                    });
                }

                ComboBoxItem? match = null;
                foreach (var obj in CmbMonitor.Items)
                    if (obj is ComboBoxItem it && it.Tag is int t && t == saved) { match = it; break; }
                CmbMonitor.SelectedItem = match ?? (CmbMonitor.Items.Count > 0 ? CmbMonitor.Items[0] : null);
            }
            finally { _monitorPopulating = false; }
        }

        private void CmbMonitor_Changed(object? sender, SelectionChangedEventArgs e)
        {
            if (_monitorPopulating || _isLoading) return;
            if (CmbMonitor.SelectedItem is not ComboBoxItem item || item.Tag is not int target) return;

            var s = CoreSettings.Current;
            if (s.SpiralTargetMonitor == target) return;

            s.SpiralTargetMonitor = target;
            CoreSettings.Save();
            // ponytail: WPF then calls App.Overlay.RefreshOverlays() - see ChkEnable_Changed.
        }

        // ── Spiral library ────────────────────────────────────────────────

        private static string NormPath(string? p)
        {
            if (string.IsNullOrWhiteSpace(p)) return "";
            try { return Path.GetFullPath(p).TrimEnd('\\', '/'); }
            catch { return p.Trim(); }
        }

        /// <summary>
        /// Rebuilds the spiral preview gallery: a "Default" card for the built-in spiral plus one
        /// card per file dropped into the Spirals folder.
        /// </summary>
        private void RefreshLibrary()
        {
            SpiralLibraryPanel.Children.Clear();

            // Built-in spiral (active when SpiralPath is empty / missing).
            // ponytail: WPF thumbnails it with Services.ModResourceResolver.ResolveSpiralUri()
            // (ConditioningControlPanel/Services/ModResourceResolver.cs), still in the WPF head, so
            // the Default card falls through to the glyph here.
            SpiralLibraryPanel.Children.Add(BuildSpiralCard("", "Default", null));

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
                Log.Warning(ex, "Spiral library: enumeration failed");
            }

            SpiralEmptyState.IsVisible = fileCount == 0;
        }

        /// <summary>
        /// Builds a clickable preview card. <paramref name="path"/> is the spiral file path ("" for
        /// the built-in default). <paramref name="thumbPath"/> is a file to render as a thumbnail,
        /// or null to show a glyph (video / unloadable).
        /// </summary>
        private Border BuildSpiralCard(string path, string display, string? thumbPath)
        {
            var card = new Border
            {
                Width = 120,
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E)),
                BorderBrush = new SolidColorBrush(IdleAccent),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 8, 8),
                Cursor = new Cursor(StandardCursorType.Hand),
                Tag = path,
            };
            ToolTip.SetTip(card, string.IsNullOrEmpty(path) ? "Built-in spiral" : path);

            var stack = new StackPanel();

            var thumbHost = new Border
            {
                Height = 80,
                Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x0A, 0x14)),
                CornerRadius = new CornerRadius(6, 6, 0, 0),
                ClipToBounds = true,
            };
            if (thumbPath != null)
            {
                try
                {
                    // WPF: BitmapImage with DecodePixelWidth = 120. DecodeToWidth is the twin, and
                    // decoding at card width rather than full size is what keeps a folder of 4K
                    // spirals from costing hundreds of MB.
                    using var fs = File.OpenRead(thumbPath);
                    thumbHost.Child = new Image { Source = Bitmap.DecodeToWidth(fs, 120), Stretch = Stretch.UniformToFill };
                }
                catch
                {
                    thumbHost.Child = SpiralGlyph("🌀");
                }
            }
            else
            {
                thumbHost.Child = SpiralGlyph(string.IsNullOrEmpty(path) ? "🌀" : "🎬");
            }
            stack.Children.Add(thumbHost);

            stack.Children.Add(new TextBlock
            {
                Text = display,
                Foreground = Brushes.White,
                FontWeight = FontWeight.SemiBold,
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(6, 6, 6, 8),
            });

            card.Child = stack;
            // WPF's MouseLeftButtonUp. The InitialPressMouseButton check is what keeps a drag that
            // started elsewhere, or a right-click release, from selecting a spiral.
            card.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton == MouseButton.Left) SelectSpiral(path);
            };
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
            var s = CoreSettings.Current;

            // Clicking a missing file is a no-op (keeps the previous selection).
            if (!string.IsNullOrEmpty(path) && !File.Exists(path)) return;

            if (NormPath(s.SpiralPath) == NormPath(path)) return; // already active

            s.SpiralPath = path; // "" => built-in default
            CoreSettings.Save();

            UpdateSelectionHighlight();
            // ponytail: WPF then calls App.Overlay.RefreshOverlays() - see ChkEnable_Changed.
        }

        private void UpdateSelectionHighlight()
        {
            foreach (var child in SpiralLibraryPanel.Children)
                if (child is Border b)
                    ApplyHighlight(b);
        }

        private void ApplyHighlight(Border card)
        {
            var stored = CoreSettings.Current.SpiralPath;
            var current = NormPath(stored);
            var tag = NormPath(card.Tag as string);
            // The Default card (empty tag) is active when no valid custom spiral is set.
            bool defaultActive = string.IsNullOrEmpty(current) || !File.Exists(stored ?? "");
            bool selected = string.IsNullOrEmpty(tag) ? defaultActive : tag == current;
            card.BorderBrush = new SolidColorBrush(selected ? SelectedAccent : IdleAccent);
        }

        // =====================================================================================
        //  buttons
        // =====================================================================================

        /// <summary>Opens the Spirals folder in the platform file manager. WPF uses
        /// <c>Process.Start(UseShellExecute)</c>; <c>TopLevel.Launcher</c> is the cross-platform
        /// twin and does not need a shell helper per OS.</summary>
        private async void BtnOpenSpiralFolder_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var folder = SpiralsFolderPath;
                Directory.CreateDirectory(folder);
                var launcher = TopLevel.GetTopLevel(this)?.Launcher;
                if (launcher is null) return;
                await launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(folder));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Spiral library: open folder failed");
            }
        }

        /// <summary>Opens the standalone corner-GIF overlay config window (two pinnable corners,
        /// independent of any running session).</summary>
        private void BtnCornerGifs_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var win = new Windows.CornerGifWindow();
                if (TopLevel.GetTopLevel(this) is Window owner) win.Show(owner);
                else win.Show();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Spiral card: Corner GIF window launch failed");
            }
        }

        /// <summary>
        /// Browse for a spiral anywhere on disk. WPF blocks on <c>OpenFileDialog</c> and then shows
        /// a confirmation MessageBox; Avalonia's picker is async, and there is no MessageBox on this
        /// head, so the gallery highlight is the confirmation.
        /// </summary>
        private async void BtnSelectGif_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var provider = TopLevel.GetTopLevel(this)?.StorageProvider;
                if (provider is null || !provider.CanOpen) return;

                IStorageFolder? start = null;
                var currentPath = CoreSettings.Current.SpiralPath;
                if (!string.IsNullOrEmpty(currentPath) && File.Exists(currentPath))
                {
                    var dir = Path.GetDirectoryName(currentPath);
                    if (!string.IsNullOrEmpty(dir))
                        start = await provider.TryGetFolderFromPathAsync(dir);
                }

                var picked = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = Loc.Get("title_select_spiral_gif"),
                    AllowMultiple = false,
                    SuggestedStartLocation = start,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("GIF Files") { Patterns = new[] { "*.gif" } },
                        new FilePickerFileType("All Image Files") { Patterns = new[] { "*.gif", "*.png", "*.jpg", "*.jpeg" } },
                    },
                });

                var file = picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
                if (string.IsNullOrEmpty(file)) return;

                CoreSettings.Current.SpiralPath = file!;
                CoreSettings.Save();

                // Reflect the new choice in the gallery (highlights it if it lives in the Spirals
                // folder, otherwise just clears the Default highlight).
                RefreshLibrary();
                // ponytail: WPF then calls App.Overlay.RefreshOverlays() - see ChkEnable_Changed.
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Spiral select: file picker failed");
            }
        }
    }
}
