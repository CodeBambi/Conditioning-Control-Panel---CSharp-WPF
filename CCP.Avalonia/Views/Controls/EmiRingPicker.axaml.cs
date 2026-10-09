using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls
{
    /// <summary>
    /// HER RING, AS A WALL OF TILES. Check a target to pin it; six is the whole ring.
    ///
    /// PORTED from ConditioningControlPanel/Views/Controls/EmiRingPicker.xaml.cs. The WPF control
    /// keeps no list of its own: the tiles ARE <c>EmiState.Pins</c>, written through
    /// <c>EmiSuggester</c> (Core) over this head's <c>EmiTargets</c> catalogue - the same store the
    /// ring window's right-click pin writes. Tile art is the ring's own <c>EmiRingWindow.AddArt</c>.
    /// </summary>
    public partial class EmiRingPicker : UserControl
    {
        /// <summary>Suppresses the toggle handler while the code is setting boxes.</summary>
        private bool _loading;

        /// <summary>Every tile in the picker, by target id, so a refresh does not rebuild the wall.</summary>
        private readonly Dictionary<string, ToggleButton> _ringTiles = new(StringComparer.Ordinal);

        /// <summary>The tiles that are gated, kept beside the wall rather than re-probed.</summary>
        private readonly HashSet<string> _ringLocked = new(StringComparer.Ordinal);

        private readonly Grid _headerRow;
        private readonly TextBlock _txtHint;
        private readonly Button _btnReset;
        private readonly WrapPanel _pnlRing;

        public EmiRingPicker()
        {
            AvaloniaXamlLoader.Load(this);
            _headerRow = this.FindControl<Grid>("HeaderRow")!;
            _txtHint = this.FindControl<TextBlock>("TxtHint")!;
            _btnReset = this.FindControl<Button>("BtnReset")!;
            _pnlRing = this.FindControl<WrapPanel>("PnlRing")!;

            _btnReset.Click += (_, _) => ResetPins();
            // Built in the constructor rather than on Loaded so a headless render sees the wall;
            // Loaded rebuilds it too, exactly as WPF does, because a host can be reopened.
            Rebuild();
            Loaded += (_, _) => Rebuild();
        }

        /// <summary>Something changed the pin set. The settings host listens so its own count line
        /// and its own "let her choose" button follow the wall it is not drawing.</summary>
        public event EventHandler? StateChanged;

        /// <summary>Draw the built-in count line and reset button. False for the settings tab.</summary>
        public bool ShowHeader
        {
            get => _headerRow.IsVisible;
            set => _headerRow.IsVisible = value;
        }

        /// <summary>The count line as it stands: "n of 6 pinned", or the full-ring line at six.</summary>
        public string HintText { get; private set; } = string.Empty;

        /// <summary>False when there is nothing to hand back to her.</summary>
        public bool CanReset { get; private set; }

        // ------------------------------------------------------------------ the wall

        /// <summary>Build the wall from the catalogue. Locked targets are shown and disabled,
        /// because "this exists and you have not got it yet" is information and an empty space is not.</summary>
        public void Rebuild()
        {
            try
            {
                _pnlRing.Children.Clear();
                _ringTiles.Clear();
                _ringLocked.Clear();

                foreach (var t in EmiTargets.All)
                {
                    if (!t.Available) continue;

                    bool locked = t.Locked;
                    var tile = new ToggleButton
                    {
                        Theme = (ControlTheme)this.FindResource("EmiRingTile")!,
                        Content = BuildTileFace(t, locked),
                        IsChecked = EmiSuggester.IsPinned(t.Id),
                        IsEnabled = !locked,
                        Tag = t.Id,
                    };
                    ToolTip.SetTip(tile, locked ? Loc.Get("emi_desk_ring_tile_locked") : t.Label);
                    // A locked tile is disabled, and a disabled control eats its own tooltip
                    // unless told not to. The reason IS the point of showing it at all.
                    ToolTip.SetShowOnDisabled(tile, true);

                    tile.IsCheckedChanged += OnRingTileToggled;

                    _pnlRing.Children.Add(tile);
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

        /// <summary>The card art (or medallion plate) with its name on a strip; the flat hue only
        /// when the art fails to load. WPF EmiCardFace.AddArt(iconSize: 34, stripReserve: 14).</summary>
        private static Control BuildTileFace(EmiTarget t, bool locked)
        {
            var grid = new Grid();
            var hue = Color.FromRgb((byte)(t.Hue >> 16), (byte)(t.Hue >> 8), (byte)t.Hue);
            EmiRingWindow.AddArt(grid, new EmiRingCard(t.Id, t.LabelKey, hue, locked, false, t.ThumbPath, t.ThumbIsIcon),
                iconSize: 34, stripReserve: 14);

            var strip = new Border
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = new SolidColorBrush(Color.FromArgb(0xD9, 0x0E, 0x0E, 0x1C)),
                Padding = new Thickness(3, 2, 3, 2),
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = (locked ? "\U0001F512 " : "") + t.Label,
                    FontSize = 9.5,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0xF0, 0xE1)),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextAlignment = TextAlignment.Center,
                },
            };
            grid.Children.Add(strip);

            return grid;
        }

        /// <summary>A tile flipped. The pin store is the arbiter, not the checkbox: a seventh pin is
        /// refused, so the tile is put back to whatever the store ended up saying.</summary>
        private void OnRingTileToggled(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
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

                // The ledger is debounced; a deliberate act should survive a hard kill.
                EmiState.SaveNow();
                RefreshRing();
                Refresh();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[EmiDesk] ring pin toggle failed");
            }
        }

        /// <summary>A fan that happens to be open shows the change now (WPF App.EmiDesk.RefreshRing).</summary>
        private static void RefreshRing()
        {
            try { EmiDeskService.Instance.Window?.RebuildRing(); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ring refresh after pin failed"); }
        }

        /// <summary>"Let her choose": drop every pin and hand the six slots back to the scores.</summary>
        public void ResetPins()
        {
            try
            {
                if (EmiSuggester.ClearPins() > 0)
                {
                    EmiState.SaveNow();
                    RefreshRing();
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

        /// <summary>The count line and the "full" state. At six pins every UNCHECKED unlocked tile
        /// goes disabled, so the refusal is something the user sees coming.</summary>
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

                _txtHint.Text = HintText;
                _btnReset.IsEnabled = CanReset;

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
