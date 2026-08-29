using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Views.Controls.AppSettingsSections
{
    /// <summary>
    /// SETTINGS - EMI DESK. Seven live editors for the summoned desktop widget.
    ///
    /// <para><b>Self-contained, with no passthrough partial.</b> Every value here is read at the
    /// moment it matters rather than at launch (the hotkey re-arms on the spot, the widget asks for
    /// spice when it picks a line), so this control reads <c>App.Settings.Current</c> on Loaded and
    /// writes it back plus <c>App.Settings.Save()</c> on every change. There is deliberately no row
    /// in MainWindow's LoadSettings / SaveSettings sweep to keep in step.</para>
    ///
    /// <para><b>The hotkey row captures a CHORD.</b> Not MainWindow's PauseKey state machine: that
    /// one is modifier-blind by design (so is the panic hook it mirrors), and a global summon bound
    /// to a bare key would swallow that letter in every other application on the machine.
    /// <see cref="EmiDeskService.ValidateChord"/> is the single arbiter of what is allowed, and the
    /// same rules run again inside <see cref="EmiDeskService.ApplyHotkey"/> at arm time, because a
    /// chord that was legal when it was captured can become a clash later (the panic key is
    /// rewritten by lockdown, remote control and preset loads).</para>
    /// </summary>
    public partial class EmiDeskSettingsSection : UserControl
    {
        private bool _loading;
        private bool _capturing;

        public EmiDeskSettingsSection()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;

            ChkEnabled.Checked += OnEnabledChanged;
            ChkEnabled.Unchecked += OnEnabledChanged;
            ChkMuteAvatar.Checked += OnMuteChanged;
            ChkMuteAvatar.Unchecked += OnMuteChanged;
            ChkOffers.Checked += OnOffersChanged;
            ChkOffers.Unchecked += OnOffersChanged;
            ChkGlass.Checked += OnGlassChanged;
            ChkGlass.Unchecked += OnGlassChanged;

            BtnHotkey.PreviewKeyDown += OnHotkeyPreviewKeyDown;
            BtnHotkey.LostKeyboardFocus += (_, _) => CancelCapture();
        }

        // ------------------------------------------------------------------ load

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _loading = true;

                if (CmbSpice.Items.Count == 0)
                {
                    CmbSpice.Items.Add(Loc.Get("emi_desk_spice_innocent"));
                    CmbSpice.Items.Add(Loc.Get("emi_desk_spice_suggestive"));
                    CmbSpice.Items.Add(Loc.Get("emi_desk_spice_anything"));
                }

                var s = App.Settings?.Current;
                if (s != null)
                {
                    ChkEnabled.IsChecked = s.EmiDeskEnabled;
                    ChkMuteAvatar.IsChecked = s.EmiDeskMuteAvatar;
                    ChkOffers.IsChecked = s.EmiDeskOffers;
                    ChkGlass.IsChecked = s.EmiDeskGlass;
                    // The combo's three rows ARE the 0..2 spice scale the lines file uses:
                    // 0 Innocent, 1 Suggestive, 2 Anything. No off-by-one translation.
                    CmbSpice.SelectedIndex = Math.Max(0, Math.Min(2, s.EmiDeskSpice));
                }
                RefreshHotkeyButton();
                BuildRingPicker();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[EmiDesk] settings section load failed");
            }
            finally
            {
                _loading = false;
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e) => CancelCapture();

        // ------------------------------------------------------------------ toggles

        private static void Persist(Action write)
        {
            try
            {
                var s = App.Settings?.Current;
                if (s == null) return;
                write();
                App.Settings?.Save();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[EmiDesk] settings write failed");
            }
        }

        private void OnEnabledChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            bool on = ChkEnabled.IsChecked == true;
            Persist(() => App.Settings!.Current.EmiDeskEnabled = on);
            // Turning her off must also take her off the screen and free the chord, not just stop
            // the next summon.
            try
            {
                if (!on) App.EmiDesk?.Dismiss();
                App.EmiDesk?.ApplyHotkey();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] enable toggle side effects failed");
            }
            RefreshHotkeyButton();
        }

        private void OnMuteChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            bool on = ChkMuteAvatar.IsChecked == true;
            Persist(() =>
            {
                App.Settings!.Current.EmiDeskMuteAvatar = on;
                // Flipping the switch clears "do not ask again": the user has just changed their
                // mind about the whole arrangement, so the next summon asks again.
                App.Settings!.Current.EmiDeskMuteDontAsk = false;
            });
        }

        private void OnOffersChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            bool on = ChkOffers.IsChecked == true;
            Persist(() => App.Settings!.Current.EmiDeskOffers = on);
        }

        private void OnGlassChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            bool on = ChkGlass.IsChecked == true;
            Persist(() => App.Settings!.Current.EmiDeskGlass = on);
        }

        private void CmbSpice_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            int spice = Math.Max(0, Math.Min(2, CmbSpice.SelectedIndex));
            Persist(() => App.Settings!.Current.EmiDeskSpice = spice);
        }

        // ------------------------------------------------------------------ hotkey capture

        private void BtnHotkey_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_capturing) { CancelCapture(); return; }
                _capturing = true;
                BtnHotkey.Content = Loc.Get("emi_desk_hotkey_capturing");
                Keyboard.Focus(BtnHotkey);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] hotkey capture start failed");
                CancelCapture();
            }
        }

        private void OnHotkeyPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!_capturing) return;
            try
            {
                e.Handled = true;
                var key = e.Key == Key.System ? e.SystemKey : e.Key;

                if (key == Key.Escape)
                {
                    CancelCapture();
                    return;
                }
                // Wait for a real key: the modifiers alone are not a chord.
                switch (key)
                {
                    case Key.LeftCtrl:
                    case Key.RightCtrl:
                    case Key.LeftAlt:
                    case Key.RightAlt:
                    case Key.LeftShift:
                    case Key.RightShift:
                    case Key.LWin:
                    case Key.RWin:
                    case Key.System:
                    case Key.None:
                        return;
                }

                var mods = Keyboard.Modifiers;
                var why = EmiDeskService.ValidateChord(mods, key);
                if (why != null)
                {
                    // Stay in capture so the user can just press something else.
                    TxtHotkeyHint.Text = why;
                    return;
                }

                var chord = EmiDeskService.FormatChord(mods, key);
                _capturing = false;
                Persist(() => App.Settings!.Current.EmiDeskHotkey = chord);
                TxtHotkeyHint.Text = Loc.Get("set2_emi_desk_hotkey_hint");
                RefreshHotkeyButton();

                try { App.EmiDesk?.ApplyHotkey(); }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] re-arm after rebind failed"); }

                if (App.EmiDesk?.HotkeyArmed == false)
                {
                    // Registration can still fail: another process may already hold the combo.
                    TxtHotkeyHint.Text = Loc.GetF("emi_desk_hotkey_err_taken", chord);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[EmiDesk] hotkey capture failed");
                CancelCapture();
            }
        }

        private void CancelCapture()
        {
            try
            {
                if (!_capturing) return;
                _capturing = false;
                RefreshHotkeyButton();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] hotkey capture cancel failed");
            }
        }

        // ------------------------------------------------------------------ her ring

        /// <summary>Every tile in the picker, by target id, so a refresh does not rebuild the wall.</summary>
        private readonly Dictionary<string, ToggleButton> _ringTiles = new(StringComparer.Ordinal);

        /// <summary>The tiles that are gated. Kept beside the wall rather than re-probed, because a
        /// lock probe that throws reads as locked and would flicker a tile in and out of reach.</summary>
        private readonly HashSet<string> _ringLocked = new(StringComparer.Ordinal);

        /// <summary>
        /// Build the 25-target wall once per Loaded.
        ///
        /// <para>Rebuilt rather than refreshed because both delegates on a target can change while
        /// the settings tab is closed: a pledge lands and six locks come off, a mod is uninstalled
        /// and a whole target stops existing. Unavailable targets are SKIPPED, exactly as the ring
        /// skips them; locked ones are shown and disabled, because "this exists and you have not
        /// got it yet" is information and an empty space is not.</para>
        /// </summary>
        private void BuildRingPicker()
        {
            try
            {
                PnlRing.Children.Clear();
                _ringTiles.Clear();
                _ringLocked.Clear();

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

                RefreshRingPicker();
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

                // The ledger is debounced; a settings write is a deliberate act and should survive
                // a hard kill on the next second.
                EmiState.SaveNow();

                // A fan that happens to be open under the pointer shows the change now.
                try { App.EmiDesk?.RefreshRing(); }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ring refresh after pin failed"); }

                RefreshRingPicker();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[EmiDesk] ring pin toggle failed");
            }
        }

        /// <summary>"Let her choose": drop every pin and hand the six slots back to the scores.</summary>
        private void BtnRingReset_Click(object sender, RoutedEventArgs e)
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

                RefreshRingPicker();
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
        private void RefreshRingPicker()
        {
            try
            {
                int pins = 0;
                try { pins = EmiState.Current.Pins.Count; }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] pin count failed"); }

                bool full = pins >= EmiSuggester.MaxPins;

                TxtRingHint.Text = full
                    ? Loc.Get("emi_desk_ring_full")
                    : Loc.GetF("emi_desk_ring_count", pins, EmiSuggester.MaxPins);

                BtnRingReset.IsEnabled = pins > 0;

                foreach (var kv in _ringTiles)
                {
                    var tb = kv.Value;
                    bool checkedNow = tb.IsChecked == true;
                    tb.IsEnabled = !_ringLocked.Contains(kv.Key) && (checkedNow || !full);
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] ring picker refresh failed");
            }
        }

        private void RefreshHotkeyButton()
        {
            try
            {
                var chord = App.Settings?.Current?.EmiDeskHotkey;
                BtnHotkey.Content = string.IsNullOrWhiteSpace(chord)
                    ? Loc.Get("emi_desk_hotkey_unbound")
                    : chord;
                BtnHotkey.IsEnabled = App.Settings?.Current?.EmiDeskEnabled != false;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] hotkey button refresh failed");
            }
        }
    }
}
