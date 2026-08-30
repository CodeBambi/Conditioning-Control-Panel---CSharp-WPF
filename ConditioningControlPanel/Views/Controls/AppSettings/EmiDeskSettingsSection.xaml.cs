using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

            WireRingPicker();
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
                // Rebuilt rather than refreshed: availability and lock are DELEGATES, and both can
                // have changed while the tab was closed.
                RingPicker.Rebuild();
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

        /// <summary>
        /// The wall is <see cref="Views.Controls.EmiRingPicker"/>, shared verbatim with her options
        /// panel. This section owns only the count line and the reset button, so they can sit in the
        /// row above in the section's own hue; both follow the picker's <c>StateChanged</c>.
        ///
        /// <para>The pin logic deliberately does NOT live here any more. There is one pin store
        /// (<c>EmiState.Pins</c>, written through <c>EmiSuggester</c>) and the surest way to end up
        /// with two was to write the picker twice.</para>
        /// </summary>
        private void WireRingPicker()
        {
            RingPicker.StateChanged += (_, _) => RefreshRingRow();
            RefreshRingRow();
        }

        private void RefreshRingRow()
        {
            try
            {
                // Guarded: the ctor wires this before the picker has ever counted, and a blank
                // count line would wipe the loc string the XAML put there.
                if (!string.IsNullOrEmpty(RingPicker.HintText)) TxtRingHint.Text = RingPicker.HintText;
                BtnRingReset.IsEnabled = RingPicker.CanReset;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] ring row refresh failed");
            }
        }

        /// <summary>"Let her choose": the picker drops every pin through the suggester.</summary>
        private void BtnRingReset_Click(object sender, RoutedEventArgs e)
        {
            try { RingPicker.ResetPins(); }
            catch (Exception ex) { Log.Warning(ex, "[EmiDesk] ring reset failed"); }
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
