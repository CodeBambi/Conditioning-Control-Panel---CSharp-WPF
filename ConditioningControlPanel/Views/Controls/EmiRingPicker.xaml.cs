using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Views.Controls
{
    /// <summary>
    /// HER RING, AS A WALL OF TILES. Check a target to pin it; six is the whole ring.
    ///
    /// <para><b>There is one pin store and this is not it.</b> The checkboxes ARE
    /// <c>EmiState.Pins</c>, always written through <see cref="EmiSuggester"/> - the same list the
    /// ring window's own right-click pin writes. This control keeps no list of its own, and
    /// <c>EmiGestureAndPinWiringTests</c> is a source tripwire that says so.</para>
    ///
    /// <para><b>Why it is a control and not a block of settings code.</b> It was born inside
    /// <c>EmiDeskSettingsSection</c> in wave 3. Her options panel (2026-08-30) needs the identical
    /// wall, and a second copy of "build the tiles, respect the six, put the tile back to whatever
    /// the store said" is exactly how two front ends onto one store drift apart. Both hosts share
    /// this file; the settings tab hides the header row and draws the count line and the reset
    /// button in its own section hue.</para>
    ///
    /// <para><b>Every brush and style in the XAML is a literal.</b> One host has MainWindow's
    /// resource dictionary behind it and the other has nothing, so a StaticResource reaching out of
    /// this control would resolve in the settings tab and take a BAML EndOfStream in the panel.</para>
    /// </summary>
    public partial class EmiRingPicker : UserControl
    {
        /// <summary>Suppresses the toggle handler while the code is setting boxes.</summary>
        private bool _loading;

        /// <summary>Every tile in the picker, by target id, so a refresh does not rebuild the wall.</summary>
        private readonly Dictionary<string, ToggleButton> _ringTiles = new(StringComparer.Ordinal);

        /// <summary>The tiles that are gated. Kept beside the wall rather than re-probed, because a
        /// lock probe that throws reads as locked and would flicker a tile in and out of reach.</summary>
        private readonly HashSet<string> _ringLocked = new(StringComparer.Ordinal);

        public EmiRingPicker()
        {
            InitializeComponent();
            BtnReset.Click += (_, _) => ResetPins();
            Loaded += (_, _) => Rebuild();
        }

        /// <summary>
        /// Something changed the pin set. The settings host listens so its own count line and its
        /// own "let her choose" button follow the wall it is not drawing.
        /// </summary>
        public event EventHandler? StateChanged;

        /// <summary>
        /// Draw the built-in count line and reset button. False for the settings tab, which has its
        /// own row for both in the section's hue.
        /// </summary>
        public bool ShowHeader
        {
            get => HeaderRow.Visibility == Visibility.Visible;
            set => HeaderRow.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>The count line as it stands: "n of 6 pinned", or the full-ring line at six.</summary>
        public string HintText { get; private set; } = string.Empty;

        /// <summary>False when there is nothing to hand back to her.</summary>
        public bool CanReset { get; private set; }

        // ------------------------------------------------------------------ the wall

        /// <summary>
        /// Build the wall from the catalogue.
        ///
        /// <para>Rebuilt rather than refreshed because both delegates on a target can change while
        /// the host is closed: a pledge lands and six locks come off, a mod is uninstalled and a
        /// whole target stops existing. Unavailable targets are SKIPPED, exactly as the ring skips
        /// them; locked ones are shown and disabled, because "this exists and you have not got it
        /// yet" is information and an empty space is not.</para>
        /// </summary>
        public void Rebuild()
        {
            try
            {
                PnlRing.Children.Clear();
                _ringTiles.Clear();
                _ringLocked.Clear();

                BtnReset.Content = Loc.Get("emi_desk_ring_reset");

                foreach (var t in EmiTargets.All)
                {
                    if (!t.Available) continue;

                    bool locked = t.Locked;
                    var tile = new ToggleButton
                    {
                        Style = (Style)FindResource("EmiRingTile"),
                        Content = BuildTileFace(t, locked),
                        IsChecked = EmiSuggester.IsPinned(t.Id),
                        IsEnabled = !locked,
                        Tag = t.Id,
                        ToolTip = locked ? Loc.Get("emi_desk_ring_tile_locked") : t.Label,
                    };
                    // A locked tile is disabled, and a disabled control eats its own tooltip
                    // unless told not to. The reason IS the point of showing it at all.
                    ToolTipService.SetShowOnDisabled(tile, true);

                    tile.Checked += OnRingTileToggled;
                    tile.Unchecked += OnRingTileToggled;

                    PnlRing.Children.Add(tile);
                    _ringTiles[t.Id] = tile;
                    if (locked) _ringLocked.Add(t.Id);
                }

                Refresh();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[EmiDesk] ring picker build failed");
            }
        }

        /// <summary>The card art with its name on a strip, or the target's flat hue when it has no art.</summary>
        private static UIElement BuildTileFace(EmiTarget t, bool locked)
        {
            var grid = new Grid();

            ImageSource? art = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(t.ThumbPath))
                    art = Services.ModResourceResolver.ResolveImageDecoded(t.ThumbPath!, 192);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] picker art missing for {Target}", t.Id);
            }

            if (art != null)
            {
                grid.Children.Add(new Image
                {
                    Source = art,
                    Stretch = Stretch.UniformToFill,
                    IsHitTestVisible = false,
                    Opacity = locked ? 0.42 : 0.92,
                });
            }
            else
            {
                grid.Children.Add(new System.Windows.Shapes.Rectangle
                {
                    Fill = new SolidColorBrush(t.Hue) { Opacity = locked ? 0.28 : 0.62 },
                    IsHitTestVisible = false,
                });
            }

            var strip = new Border
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = new SolidColorBrush(Color.FromArgb(0xD9, 0x0E, 0x0E, 0x1C)),
                Padding = new Thickness(3, 2, 3, 2),
                IsHitTestVisible = false,
            };
            strip.Child = new TextBlock
            {
                Text = (locked ? "\U0001F512 " : "") + t.Label,
                FontSize = 9.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0xF0, 0xE1)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center,
            };
            grid.Children.Add(strip);

            return grid;
        }

        /// <summary>
        /// A tile flipped. The pin store is the arbiter, not the checkbox: <c>TogglePin</c> refuses
        /// a seventh pin, so the tile is put back to whatever the store ended up saying rather than
        /// to whatever the click asked for.
        /// </summary>
        private void OnRingTileToggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            try
            {
                if (sender is not ToggleButton tb || tb.Tag is not string id) return;

                bool nowPinned = EmiSuggester.TogglePin(id);
                if (tb.IsChecked != nowPinned)
                {
                    _loading = true;
                    try { tb.IsChecked = nowPinned; }
                    finally { _loading = false; }
                }

                // The ledger is debounced; a deliberate act should survive a hard kill on the next
                // second.
                EmiState.SaveNow();

                // A fan that happens to be open under the pointer shows the change now.
                try { App.EmiDesk?.RefreshRing(); }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ring refresh after pin failed"); }

                Refresh();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[EmiDesk] ring pin toggle failed");
            }
        }

        /// <summary>"Let her choose": drop every pin and hand the six slots back to the scores.</summary>
        public void ResetPins()
        {
            try
            {
                if (EmiSuggester.ClearPins() > 0)
                {
                    EmiState.SaveNow();
                    try { App.EmiDesk?.RefreshRing(); }
                    catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ring refresh after clear failed"); }
                }

                _loading = true;
                try
                {
                    foreach (var tb in _ringTiles.Values) tb.IsChecked = false;
                }
                finally { _loading = false; }

                Refresh();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[EmiDesk] ring reset failed");
            }
        }

        /// <summary>
        /// The count line and the "full" state. At six pins every UNCHECKED unlocked tile goes
        /// disabled, so the refusal inside TogglePin is something the user sees coming rather than
        /// a click that silently does nothing.
        /// </summary>
        public void Refresh()
        {
            try
            {
                int pins = 0;
                try { pins = EmiState.Current.Pins.Count; }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] pin count failed"); }

                bool full = pins >= EmiSuggester.MaxPins;

                HintText = full
                    ? Loc.Get("emi_desk_ring_full")
                    : Loc.GetF("emi_desk_ring_count", pins, EmiSuggester.MaxPins);
                CanReset = pins > 0;

                TxtHint.Text = HintText;
                BtnReset.IsEnabled = CanReset;

                foreach (var kv in _ringTiles)
                {
                    var tb = kv.Value;
                    bool checkedNow = tb.IsChecked == true;
                    tb.IsEnabled = !_ringLocked.Contains(kv.Key) && (checkedNow || !full);
                }

                try { StateChanged?.Invoke(this, EventArgs.Empty); }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ring picker StateChanged threw"); }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] ring picker refresh failed");
            }
        }
    }
}
