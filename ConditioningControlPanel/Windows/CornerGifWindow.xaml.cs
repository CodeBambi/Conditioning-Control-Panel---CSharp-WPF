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
    /// Config window for the standalone corner-GIF overlays (opened from the Spiral card).
    /// Exposes two slots — each an on/off toggle, a GIF picker, a corner selector, and
    /// size/opacity sliders — and live-applies every change via <see cref="App.CornerGif"/>.
    /// </summary>
    public partial class CornerGifWindow : Window
    {
        private const int SlotCount = 2;

        private static readonly Color SelectedAccent = Color.FromRgb(0xFF, 0x69, 0xB4);
        private static readonly Color IdleAccent = Color.FromRgb(0x33, 0x33, 0x3A);

        // Slider drags recreate the overlay windows on every tick; debounce so a drag
        // doesn't thrash Close/Show (the recreate is the safe path, but still not free).
        private readonly DispatcherTimer _applyDebounce;

        public CornerGifWindow()
        {
            InitializeComponent();

            _applyDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _applyDebounce.Tick += (_, _) =>
            {
                _applyDebounce.Stop();
                ApplyLive();
            };

            EnsureSlots();
            BuildSlots();
        }

        /// <summary>Pads the persisted list to <see cref="SlotCount"/> entries with sensible defaults.</summary>
        private static List<CornerGifOverlaySetting> EnsureSlots()
        {
            var s = App.Settings?.Current;
            var list = s?.CornerGifOverlays ?? new List<CornerGifOverlaySetting>();
            while (list.Count < SlotCount)
            {
                // Second slot defaults to the opposite bottom corner so two-at-once looks sensible.
                list.Add(new CornerGifOverlaySetting
                {
                    Position = list.Count == 0 ? CornerPosition.BottomLeft : CornerPosition.BottomRight
                });
            }
            if (s != null) s.CornerGifOverlays = list;
            return list;
        }

        private void BuildSlots()
        {
            SlotsPanel.Children.Clear();
            var list = EnsureSlots();
            for (int i = 0; i < SlotCount; i++)
                SlotsPanel.Children.Add(BuildSlotCard(i, list[i]));
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

            // ── Header row: title + enable toggle ────────────────────────────
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = new TextBlock
            {
                Text = $"Corner GIF {index + 1}",
                Foreground = Brushes.White,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(title, 0);
            headerGrid.Children.Add(title);

            var toggle = new CheckBox
            {
                IsChecked = setting.Enabled,
                VerticalAlignment = VerticalAlignment.Center,
            };
            if (TryFindResource("ToggleStyle") is Style toggleStyle) toggle.Style = toggleStyle;
            Grid.SetColumn(toggle, 1);
            headerGrid.Children.Add(toggle);
            root.Children.Add(headerGrid);

            // ── Settings sub-panel (dimmed/disabled when off) ────────────────
            var body = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };

            // GIF picker
            body.Children.Add(SectionLabel("GIF"));
            var pickBtn = new Button
            {
                Content = PickerLabel(setting.GifPath),
                Padding = new Thickness(12, 8, 12, 8),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 2),
            };
            if (TryFindResource("OutlineButton") is Style outline) pickBtn.Style = outline;
            pickBtn.Click += (_, _) =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "GIF Files (*.gif)|*.gif|All Image Files|*.gif;*.png;*.jpg;*.jpeg;*.webp;*.bmp",
                    Title = "Select a corner GIF",
                };
                if (!string.IsNullOrEmpty(setting.GifPath) && File.Exists(setting.GifPath))
                    dlg.InitialDirectory = Path.GetDirectoryName(setting.GifPath);

                if (dlg.ShowDialog() == true)
                {
                    setting.GifPath = dlg.FileName;
                    pickBtn.Content = PickerLabel(setting.GifPath);
                    ApplyLive();
                }
            };
            body.Children.Add(pickBtn);
            body.Children.Add(new TextBlock
            {
                Text = "Leave unset to use the built-in spiral.",
                Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x90)),
                FontSize = 10,
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
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Child = new TextBlock
                    {
                        Text = label,
                        Foreground = Brushes.White,
                        FontSize = 11,
                    },
                };
                b.MouseLeftButtonUp += (_, _) =>
                {
                    setting.Position = pos;
                    foreach (var kv in cornerButtons)
                        kv.Value.BorderBrush = new SolidColorBrush(kv.Key == pos ? SelectedAccent : IdleAccent);
                    ApplyLive();
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
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            body.Children.Add(LabeledRow("Size", sizeValue));
            var sizeSlider = new Slider { Minimum = 100, Maximum = 800, Value = setting.Size };
            sizeSlider.ValueChanged += (_, e) =>
            {
                setting.Size = (int)e.NewValue;
                sizeValue.Text = $"{setting.Size}px";
                ApplyLiveDebounced();
            };
            body.Children.Add(sizeSlider);

            // Opacity slider
            var opacityValue = new TextBlock
            {
                Text = $"{setting.Opacity}%",
                Foreground = new SolidColorBrush(SelectedAccent),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            body.Children.Add(LabeledRow("Opacity", opacityValue));
            var opacitySlider = new Slider { Minimum = 5, Maximum = 100, Value = setting.Opacity, Margin = new Thickness(0, 0, 0, 2) };
            opacitySlider.ValueChanged += (_, e) =>
            {
                setting.Opacity = (int)e.NewValue;
                opacityValue.Text = $"{setting.Opacity}%";
                ApplyLiveDebounced();
            };
            body.Children.Add(opacitySlider);

            body.IsEnabled = setting.Enabled;
            body.Opacity = setting.Enabled ? 1.0 : 0.45;
            root.Children.Add(body);

            toggle.Checked += (_, _) => OnToggle(true);
            toggle.Unchecked += (_, _) => OnToggle(false);
            void OnToggle(bool on)
            {
                setting.Enabled = on;
                body.IsEnabled = on;
                body.Opacity = on ? 1.0 : 0.45;
                ApplyLive();
            }

            card.Child = root;
            return card;
        }

        private static TextBlock SectionLabel(string text) => new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xC0)),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        };

        private static Grid LabeledRow(string label, UIElement right)
        {
            var g = new Grid { Margin = new Thickness(0, 6, 0, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var l = new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xC0)),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(l, 0);
            Grid.SetColumn((FrameworkElement)right, 1);
            g.Children.Add(l);
            g.Children.Add(right);
            return g;
        }

        private static string PickerLabel(string path) =>
            string.IsNullOrEmpty(path) || !File.Exists(path)
                ? "📁 Choose GIF…  (built-in spiral)"
                : $"📁 {Path.GetFileName(path)}";

        private void ApplyLiveDebounced()
        {
            _applyDebounce.Stop();
            _applyDebounce.Start();
        }

        private void ApplyLive()
        {
            App.Settings?.Save();
            App.CornerGif?.RefreshOverlays();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
