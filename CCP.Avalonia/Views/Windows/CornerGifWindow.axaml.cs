using System;
using System.Collections.Generic;
using System.IO;
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
    /// Config window for the standalone corner-GIF overlays (opened from the Spiral card).
    /// Exposes two slots - each an on/off toggle, a GIF picker, a corner selector, and
    /// size/opacity sliders - and live-applies every change.
    ///
    /// PORTED from ConditioningControlPanel/Windows/CornerGifWindow.xaml.cs. Deviations:
    ///  - App.CornerGif is still in the WPF head, so <see cref="ApplyLive"/> persists but cannot
    ///    re-realize the overlay windows; the slot list itself is the real persisted one.
    ///  - Microsoft.Win32.OpenFileDialog -> TopLevel.StorageProvider.OpenFilePickerAsync, which
    ///    is async, so the pick handler awaits instead of blocking.
    ///  - MouseLeftButtonUp -> PointerReleased with an InitialPressMouseButton check.
    ///  - The picker button holds a TextBlock, not a string Content: Avalonia parses "_" in
    ///    Content as an access key and GIF file names routinely carry underscores.
    /// </summary>
    public partial class CornerGifWindow : Window
    {
        private const int SlotCount = 2;

        private static readonly Color SelectedAccent = Color.FromRgb(0xFF, 0x69, 0xB4);
        private static readonly Color IdleAccent = Color.FromRgb(0x33, 0x33, 0x3A);

        private readonly StackPanel _slotsPanel;

        // Slider drags recreate an overlay window on every tick; debounce so a drag
        // doesn't thrash Close/Show (the recreate is the safe path, but still not free).
        private readonly DispatcherTimer _applyDebounce;

        // Slot awaiting the debounced apply: null = nothing pending, -1 = rebuild everything.
        private int? _pendingSlot;

        public CornerGifWindow()
        {
            AvaloniaXamlLoader.Load(this);

            _slotsPanel = this.FindControl<StackPanel>("SlotsPanel")!;
            this.FindControl<Button>("BtnClose")!.Click += (_, _) => Close();

            _applyDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _applyDebounce.Tick += (_, _) =>
            {
                _applyDebounce.Stop();
                var slot = _pendingSlot ?? -1;
                _pendingSlot = null;
                ApplyLive(slot);
            };

            BuildSlots();
        }

        /// <summary>Pads the persisted list to <see cref="SlotCount"/> entries with sensible defaults,
        /// and trims anything beyond them - the UI only ever edits the first <see cref="SlotCount"/>,
        /// so extra entries in a hand-edited settings file would be invisible overlays.</summary>
        private static List<CornerGifOverlaySetting> EnsureSlots()
        {
            var s = CoreSettings.Current;
            var list = s.CornerGifOverlays ?? new List<CornerGifOverlaySetting>();
            while (list.Count < SlotCount)
            {
                // Second slot defaults to the opposite bottom corner so two-at-once looks sensible.
                list.Add(new CornerGifOverlaySetting
                {
                    Position = list.Count == 0 ? CornerPosition.BottomLeft : CornerPosition.BottomRight
                });
            }
            if (list.Count > SlotCount) list.RemoveRange(SlotCount, list.Count - SlotCount);
            s.CornerGifOverlays = list;
            return list;
        }

        private void BuildSlots()
        {
            _slotsPanel.Children.Clear();
            var list = EnsureSlots();
            for (int i = 0; i < SlotCount; i++)
                _slotsPanel.Children.Add(BuildSlotCard(i, list[i]));
        }

        private Border BuildSlotCard(int index, CornerGifOverlaySetting setting)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x60)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 14, 16, 16),
                Margin = new Thickness(0, 0, 0, 12),
            };

            var root = new StackPanel();

            // -- Header row: title + enable toggle --------------------------------
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = new TextBlock
            {
                Text = $"Corner GIF {index + 1}",
                Foreground = Brushes.White,
                FontSize = 15,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(title, 0);
            headerGrid.Children.Add(title);

            var toggle = new CheckBox
            {
                IsChecked = setting.Enabled,
                VerticalAlignment = VerticalAlignment.Center,
            };
            if (this.TryFindResource("ToggleStyle", out var toggleTheme) && toggleTheme is ControlTheme ct)
                toggle.Theme = ct;
            Grid.SetColumn(toggle, 1);
            headerGrid.Children.Add(toggle);
            root.Children.Add(headerGrid);

            // -- Settings sub-panel (dimmed/disabled when off) --------------------
            var body = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };

            // GIF picker
            body.Children.Add(SectionLabel("GIF"));
            var pickLabel = new TextBlock { Text = PickerLabel(setting.GifPath) };
            var pickBtn = new Button
            {
                Content = pickLabel,
                Padding = new Thickness(12, 8, 12, 8),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 2),
            };
            if (this.TryFindResource("OutlineButton", out var outline) && outline is ControlTheme ot)
                pickBtn.Theme = ot;
            pickBtn.Click += async (_, _) =>
            {
                var top = GetTopLevel(this);
                if (top is null) return;

                var start = !string.IsNullOrEmpty(setting.GifPath) && File.Exists(setting.GifPath)
                    ? await top.StorageProvider.TryGetFolderFromPathAsync(Path.GetDirectoryName(setting.GifPath)!)
                    : null;

                var picked = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select a corner GIF",
                    AllowMultiple = false,
                    SuggestedStartLocation = start,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("GIF Files") { Patterns = new[] { "*.gif" } },
                        new FilePickerFileType("All Image Files")
                        {
                            Patterns = new[] { "*.gif", "*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp" }
                        },
                    },
                });

                var path = picked.FirstOrDefault()?.TryGetLocalPath();
                if (string.IsNullOrEmpty(path)) return;

                setting.GifPath = path;
                pickLabel.Text = PickerLabel(path);
                ApplyLive(index);
            };
            body.Children.Add(pickBtn);
            body.Children.Add(new TextBlock
            {
                Text = "Leave unset to use the built-in spiral (a mod pack can swap in its own).",
                Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x90)),
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12),
            });

            // Position selector (4 corner buttons)
            body.Children.Add(SectionLabel("Position"));
            var cornerPanel = new WrapPanel { Margin = new Thickness(0, 2, 0, 12) };
            var cornerButtons = new Dictionary<CornerPosition, Border>();
            void AddCorner(CornerPosition pos, string label)
            {
                var b = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x20)),
                    BorderBrush = new SolidColorBrush(pos == setting.Position ? SelectedAccent : IdleAccent),
                    BorderThickness = new Thickness(2),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 6, 10, 6),
                    Margin = new Thickness(0, 0, 6, 6),
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Child = new TextBlock
                    {
                        Text = label,
                        Foreground = Brushes.White,
                        FontSize = 11,
                    },
                };
                b.PointerReleased += (_, e) =>
                {
                    if (e.InitialPressMouseButton != MouseButton.Left) return;
                    setting.Position = pos;
                    foreach (var kv in cornerButtons)
                        kv.Value.BorderBrush = new SolidColorBrush(kv.Key == pos ? SelectedAccent : IdleAccent);
                    // Full rebuild: moving one slot into (or out of) another slot's corner changes
                    // the other slot's same-corner nudge too.
                    ApplyLiveAll();
                };
                cornerButtons[pos] = b;
                cornerPanel.Children.Add(b);
            }
            AddCorner(CornerPosition.TopLeft, "↖ Top left");
            AddCorner(CornerPosition.TopRight, "↗ Top right");
            AddCorner(CornerPosition.BottomLeft, "↙ Bottom left");
            AddCorner(CornerPosition.BottomRight, "↘ Bottom right");
            body.Children.Add(cornerPanel);

            // Size slider
            var sizeValue = new TextBlock
            {
                Text = $"{setting.Size}px",
                Foreground = new SolidColorBrush(SelectedAccent),
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            body.Children.Add(LabeledRow("Size", sizeValue));
            // Minimum/Maximum before Value: RangeBase clamps on assignment, so a Value set first
            // would be pinned to the default 0..100 range.
            var sizeSlider = NewSlider(100, 800, setting.Size);
            sizeSlider.ValueChanged += (_, e) =>
            {
                setting.Size = (int)e.NewValue;
                sizeValue.Text = $"{setting.Size}px";
                ApplyLiveDebounced(index);
            };
            body.Children.Add(sizeSlider);

            // Opacity slider
            var opacityValue = new TextBlock
            {
                Text = $"{setting.Opacity}%",
                Foreground = new SolidColorBrush(SelectedAccent),
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            body.Children.Add(LabeledRow("Opacity", opacityValue));
            var opacitySlider = NewSlider(5, 100, setting.Opacity);
            opacitySlider.Margin = new Thickness(0, 0, 0, 2);
            opacitySlider.ValueChanged += (_, e) =>
            {
                setting.Opacity = (int)e.NewValue;
                opacityValue.Text = $"{setting.Opacity}%";
                ApplyLiveDebounced(index);
            };
            body.Children.Add(opacitySlider);

            body.IsEnabled = setting.Enabled;
            body.Opacity = setting.Enabled ? 1.0 : 0.45;
            root.Children.Add(body);

            // WPF's Checked + Unchecked, which Avalonia folds into one event.
            toggle.IsCheckedChanged += (_, _) =>
            {
                bool on = toggle.IsChecked == true;
                setting.Enabled = on;
                body.IsEnabled = on;
                body.Opacity = on ? 1.0 : 0.45;
                ApplyLiveAll(); // enabling/disabling a slot shifts the other slot's same-corner nudge
            };

            card.Child = root;
            return card;
        }

        /// <summary>WPF applies PinkSlider implicitly (Resources/Theme/MainWindow.xaml has a keyless
        /// <c>Style TargetType="Slider" BasedOn="{StaticResource PinkSlider}"</c>), so the original's
        /// bare <c>new Slider</c> was pink. Avalonia has no implicit style, so the theme is looked up
        /// by key here. Height=32 because PinkSlider sets Height=18 and clips its own thumb without
        /// it - the same fix DevicesSettingsSection carries.
        /// Minimum/Maximum before Value: RangeBase clamps on assignment, so a Value set first would
        /// be pinned to the default 0..100 range.</summary>
        private Slider NewSlider(double min, double max, double value)
        {
            var s = new Slider { Minimum = min, Maximum = max, Value = value };
            if (this.TryFindResource("PinkSlider", out var theme) && theme is ControlTheme t)
            {
                s.Theme = t;
                s.Height = 32;
            }
            return s;
        }

        private static TextBlock SectionLabel(string text) => new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xC0)),
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        };

        private static Grid LabeledRow(string label, Control right)
        {
            var g = new Grid { Margin = new Thickness(0, 6, 0, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var l = new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xC0)),
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(l, 0);
            Grid.SetColumn(right, 1);
            g.Children.Add(l);
            g.Children.Add(right);
            return g;
        }

        private static string PickerLabel(string path) =>
            string.IsNullOrEmpty(path) || !File.Exists(path)
                ? "📁 Choose GIF…  (uses selected spiral)"
                : $"📁 {Path.GetFileName(path)}";

        private void ApplyLiveDebounced(int index)
        {
            // Two different slots dragged inside one debounce window would drop the first slot's
            // pending rebuild, so widen to a full refresh in that case.
            _pendingSlot = _pendingSlot == null || _pendingSlot == index ? index : -1;
            _applyDebounce.Stop();
            _applyDebounce.Start();
        }

        private void ApplyLiveAll()
        {
            _applyDebounce.Stop();
            _pendingSlot = null;
            ApplyLive(-1);
        }

        /// <summary>Persists and re-applies. <paramref name="index"/> &gt;= 0 rebuilds only that
        /// slot's overlay window - the other slot's handle (and its running animation) is left
        /// alone, which is what keeps a slider drag from re-realizing every overlay per tick.</summary>
        private void ApplyLive(int index = -1)
        {
            CoreSettings.Save();
            // Where the overlay goes is CornerGifPlanner's answer now, and it is the same on every
            // head. Raising the window is not: CoreCornerGif is the surface seam, seeded by the WPF
            // head to App.CornerGif and UNSEEDED here until this head's overlay surface lands
            // (CCP.Avalonia/Views/Overlays/), so this call is a deliberate no-op on Linux. The save
            // above is the live part - the slot is correct on disk and shows the moment a surface
            // exists.
            CoreCornerGif.Refresh(index);
        }
    }
}
