using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls
{
    /// <summary>
    /// HER RING, AS A WALL OF TILES. Check a target to pin it; six is the whole ring.
    ///
    /// PORTED from ConditioningControlPanel/Views/Controls/EmiRingPicker.xaml.cs. The WPF control
    /// keeps no list of its own: the tiles ARE <c>EmiState.Pins</c>, written through
    /// <c>EmiSuggester</c>. Those, <c>EmiTargets</c> and <c>ModResourceResolver</c> are all in the
    /// WPF head, so this port carries a PLACEHOLDER catalogue and pin set (see the ponytail note)
    /// that must be deleted, not kept, when the store moves to Core.
    /// </summary>
    public partial class EmiRingPicker : UserControl
    {
        // ponytail: placeholder catalogue + pin set. EmiSuggester/EmiState are the ONLY pin store;
        // replace both with EmiTargets.All / EmiSuggester.IsPinned / TogglePin / ClearPins when
        // they move to Core. Ids are real so emi_desk_target_<id> resolves to real labels.
        private sealed record Target(string Id, Color Hue, bool Locked)
        {
            public string Label { get { try { return Loc.Get("emi_desk_target_" + Id); } catch { return Id; } } }
        }
        private static readonly Target[] Catalogue =
        {
            new("sessions", Color.Parse("#FF69B4"), false),
            new("flashes", Color.Parse("#FFB84E"), false),
            new("videos", Color.Parse("#5EA8FF"), false),
            new("subliminals", Color.Parse("#B47BFF"), false),
            new("bubbles", Color.Parse("#5CE0A5"), false),
            new("spiral", Color.Parse("#E85CE0"), false),
            new("loom", Color.Parse("#FF7E6B"), true),
            new("arcademy", Color.Parse("#5ED4E8"), true),
        };
        private const int MaxPins = 6;
        private readonly HashSet<string> _pins = new(StringComparer.Ordinal) { "sessions", "spiral" };

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

                foreach (var t in Catalogue)
                {
                    bool locked = t.Locked;
                    var tile = new ToggleButton
                    {
                        Theme = (ControlTheme)this.FindResource("EmiRingTile")!,
                        Content = BuildTileFace(t, locked),
                        IsChecked = _pins.Contains(t.Id),
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

        /// <summary>The target's flat hue with its name on a strip.
        /// ponytail: card art comes through ModResourceResolver (WPF head); the flat-hue branch
        /// is the one the WPF control takes when there is no art, so it is the only one here.</summary>
        private static Control BuildTileFace(Target t, bool locked)
        {
            var grid = new Grid();

            grid.Children.Add(new Rectangle
            {
                Fill = new SolidColorBrush(t.Hue) { Opacity = locked ? 0.28 : 0.62 },
                IsHitTestVisible = false,
            });

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

                bool nowPinned = TogglePin(id);
                if (tb.IsChecked != nowPinned)
                {
                    _loading = true;
                    try { tb.IsChecked = nowPinned; }
                    finally { _loading = false; }
                }

                // ponytail: EmiState.SaveNow() and App.EmiDesk.RefreshRing() go here when wired.
                Refresh();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[EmiDesk] ring pin toggle failed");
            }
        }

        /// <summary>Placeholder for EmiSuggester.TogglePin: refuses a seventh pin, returns the
        /// pinned state the store ended up in.</summary>
        private bool TogglePin(string id)
        {
            if (_pins.Remove(id)) return false;
            if (_pins.Count >= MaxPins) return false;
            _pins.Add(id);
            return true;
        }

        /// <summary>"Let her choose": drop every pin and hand the six slots back to the scores.</summary>
        public void ResetPins()
        {
            try
            {
                _pins.Clear(); // ponytail: EmiSuggester.ClearPins() + EmiState.SaveNow() when wired

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
                int pins = _pins.Count;
                bool full = pins >= MaxPins;

                HintText = full
                    ? Loc.Get("emi_desk_ring_full")
                    : Loc.GetF("emi_desk_ring_count", pins, MaxPins);
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
