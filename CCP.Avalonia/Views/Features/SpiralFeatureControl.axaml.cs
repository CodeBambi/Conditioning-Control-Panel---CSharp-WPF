using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// Spiral Overlay panel, ported from the WPF head.
    ///
    /// What is real here: the opacity read-out, the monitor picker's two fixed entries, and the
    /// library's "Default" card. What is not: everything that reads App.Settings (SpiralEnabled,
    /// SpiralOpacity, SpiralPath, SpiralTargetMonitor...), the screen enumeration
    /// (App.GetAllScreensCached), the Spirals folder scan (App.UserDataPath), the Loom and
    /// Corner-GIF windows, the file picker and the overlay refresh. Each is a WPF-head service.
    /// </summary>
    public partial class SpiralFeatureControl : UserControl
    {
        private static readonly Color SelectedAccent = Color.FromRgb(0xFF, 0x69, 0xB4);
        private static readonly Color IdleAccent = Color.FromRgb(0x33, 0x33, 0x3A);

        public SpiralFeatureControl()
        {
            AvaloniaXamlLoader.Load(this);

            SliderLabel.Wire(this, "SliderOpacity", "TxtOpacity", v => $"{(int)v}%");
            PopulateMonitors();
            RefreshLibrary();

            // ponytail: needs App.Settings / App.Overlay / App.GetAllScreensCached / LoomHostService /
            // CornerGifWindow / a file picker, wired when they move to Core. ChkEnable, ChkRandomize,
            // ChkSessionCornerGif, CmbMonitor, BtnOpenLoom, BtnCornerGifs, BtnSelectGif,
            // BtnOpenSpiralFolder are inert until then.
            this.FindControl<Button>("BtnRefreshSpirals")!.Click += (_, _) => RefreshLibrary();
        }

        /// <summary>
        /// The two fixed entries come from the same keys WPF uses; the per-screen lines need the
        /// display topology, so one placeholder line in WPF's exact format stands in for them.
        /// </summary>
        private void PopulateMonitors()
        {
            var cmb = this.FindControl<ComboBox>("CmbMonitor")!;
            cmb.Items.Clear();
            cmb.Items.Add(new ComboBoxItem { Content = Loc.Get("monitor_target_default"), Tag = -1 });
            cmb.Items.Add(new ComboBoxItem { Content = Loc.Get("monitor_target_all"), Tag = -2 });
            // WPF: $"{monitorLabel} {i + 1} ({prefix}{b.Width}x{b.Height})" per screen.
            cmb.Items.Add(new ComboBoxItem
            {
                Content = $"{Loc.Get("monitor_label")} 1 ({Loc.Get("monitor_primary_marker")}, 1920x1080)",
                Tag = 0,
            });
            cmb.SelectedIndex = 0;
        }

        /// <summary>
        /// Rebuilds the gallery: the "Default" card only, since the user folder lives under
        /// App.UserDataPath. Empty-state text shows, as it does on WPF with an empty folder.
        /// </summary>
        private void RefreshLibrary()
        {
            var panel = this.FindControl<WrapPanel>("SpiralLibraryPanel")!;
            panel.Children.Clear();
            panel.Children.Add(BuildSpiralCard("", "Default", selected: true));
            this.FindControl<TextBlock>("SpiralEmptyState")!.IsVisible = true;
        }

        /// <summary>
        /// WPF's BuildSpiralCard minus the bitmap branch: no file to thumbnail here, so every card
        /// gets the glyph. Selection highlight is the same 2px accent swap.
        /// </summary>
        private static Border BuildSpiralCard(string path, string display, bool selected)
        {
            var card = new Border
            {
                Width = 120,
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E)),
                BorderBrush = new SolidColorBrush(selected ? SelectedAccent : IdleAccent),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 8, 8),
                Cursor = new Cursor(StandardCursorType.Hand),
                Tag = path,
            };
            ToolTip.SetTip(card, string.IsNullOrEmpty(path) ? "Built-in spiral" : path);

            var stack = new StackPanel();
            stack.Children.Add(new Border
            {
                Height = 80,
                Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x0A, 0x14)),
                CornerRadius = new CornerRadius(6, 6, 0, 0),
                ClipToBounds = true,
                Child = new TextBlock
                {
                    Text = "🌀",
                    FontSize = 32,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                },
            });
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
            return card;
        }
    }
}
