using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls.AppSettings
{
    /// <summary>
    /// SETTINGS - EMI DESK, ported from the WPF head with its settings logic restored against
    /// <see cref="CoreSettings"/>. Seven live editors for the summoned desktop widget.
    ///
    /// <para><b>Self-contained, with no passthrough partial.</b> Every value here is read at the
    /// moment it matters rather than at launch, so this section reads <c>CoreSettings.Current</c> on
    /// attach and writes it back plus <c>CoreSettings.Save()</c> on every change - the same shape
    /// the WPF original has, one for one.</para>
    ///
    /// <para><b>The hotkey row captures a CHORD.</b> Core <see cref="EmiDeskChord.Validate"/> is the
    /// single arbiter both heads use, and <see cref="EmiDeskService.ApplyHotkey"/> re-checks at arm
    /// time and grabs it on X11, as WPF does with RegisterHotKey.</para>
    /// </summary>
    public partial class EmiDeskSettingsSection : UserControl
    {
        private bool _loading = true;
        private bool _capturing;

        public EmiDeskSettingsSection()
        {
            // InitializeComponent, not AvaloniaXamlLoader.Load: only the generated one assigns the
            // x:Name fields, and the seed below reads them.
            InitializeComponent();

            // WPF wired Checked/Unchecked separately; Avalonia has one event for both.
            ChkEnabled.IsCheckedChanged += OnEnabledChanged;
            ChkMuteAvatar.IsCheckedChanged += OnMuteChanged;
            ChkOffers.IsCheckedChanged += OnOffersChanged;
            ChkGlass.IsCheckedChanged += OnGlassChanged;

            BtnHotkey.AddHandler(KeyDownEvent, OnHotkeyPreviewKeyDown, RoutingStrategies.Tunnel);
            BtnHotkey.LostFocus += (_, _) => CancelCapture();

            WireRingPicker();
            SyncFromSettings();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced += OnCurrentReplaced;
            SyncFromSettings();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced -= OnCurrentReplaced;
            CancelCapture();
            base.OnDetachedFromVisualTree(e);
        }

        // A cloud restore or a factory reset swaps the instance; repaint from it, on the UI thread.
        private void OnCurrentReplaced() => Dispatcher.UIThread.Post(SyncFromSettings);

        // ------------------------------------------------------------------ load

        internal void SyncFromSettings()
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

                var s = CoreSettings.Current;
                ChkEnabled.IsChecked = s.EmiDeskEnabled;
                ChkMuteAvatar.IsChecked = s.EmiDeskMuteAvatar;
                ChkOffers.IsChecked = s.EmiDeskOffers;
                ChkGlass.IsChecked = s.EmiDeskGlass;
                // The combo's three rows ARE the 0..2 spice scale the lines file uses:
                // 0 Innocent, 1 Suggestive, 2 Anything. No off-by-one translation.
                CmbSpice.SelectedIndex = Math.Max(0, Math.Min(2, s.EmiDeskSpice));

                RefreshHotkeyButton();
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

        // ------------------------------------------------------------------ toggles

        private static void Persist(Action write)
        {
            try
            {
                write();
                CoreSettings.Save();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[EmiDesk] settings write failed");
            }
        }

        private void OnEnabledChanged(object? sender, RoutedEventArgs e)
        {
            if (_loading) return;
            bool on = ChkEnabled.IsChecked == true;
            Persist(() => CoreSettings.Current.EmiDeskEnabled = on);
            // WPF :123: off takes her off the screen and frees the chord, not just the next summon.
            try
            {
                if (!on) EmiDeskService.Instance.Dismiss();
                EmiDeskService.Instance.ApplyHotkey();
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] enable toggle side effects failed"); }
            RefreshHotkeyButton();
        }

        private void OnMuteChanged(object? sender, RoutedEventArgs e)
        {
            if (_loading) return;
            bool on = ChkMuteAvatar.IsChecked == true;
            Persist(() =>
            {
                CoreSettings.Current.EmiDeskMuteAvatar = on;
                // Flipping the switch clears "do not ask again": the user has just changed their
                // mind about the whole arrangement, so the next summon asks again.
                CoreSettings.Current.EmiDeskMuteDontAsk = false;
            });
        }

        private void OnOffersChanged(object? sender, RoutedEventArgs e)
        {
            if (_loading) return;
            bool on = ChkOffers.IsChecked == true;
            Persist(() => CoreSettings.Current.EmiDeskOffers = on);
        }

        private void OnGlassChanged(object? sender, RoutedEventArgs e)
        {
            if (_loading) return;
            bool on = ChkGlass.IsChecked == true;
            Persist(() => CoreSettings.Current.EmiDeskGlass = on);
        }

        private void CmbSpice_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            int spice = Math.Max(0, Math.Min(2, CmbSpice.SelectedIndex));
            Persist(() => CoreSettings.Current.EmiDeskSpice = spice);
        }

        // ------------------------------------------------------------------ hotkey capture

        private void BtnHotkey_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (_capturing) { CancelCapture(); return; }
                _capturing = true;
                BtnHotkey.Content = Loc.Get("emi_desk_hotkey_capturing");
                BtnHotkey.Focus();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] hotkey capture start failed");
                CancelCapture();
            }
        }

        private void OnHotkeyPreviewKeyDown(object? sender, KeyEventArgs e)
        {
            if (!_capturing) return;
            e.Handled = true;
            if (e.Key == Key.Escape) { CancelCapture(); return; }
            // Wait for a real key: the modifiers alone are not a chord.
            switch (e.Key)
            {
                case Key.LeftCtrl: case Key.RightCtrl:
                case Key.LeftAlt: case Key.RightAlt:
                case Key.LeftShift: case Key.RightShift:
                case Key.LWin: case Key.RWin:
                case Key.System: case Key.None:
                    return;
            }
            try
            {
                var m = e.KeyModifiers;
                var mods = ((m & KeyModifiers.Control) != 0 ? ChordMods.Ctrl : 0)
                         | ((m & KeyModifiers.Alt) != 0 ? ChordMods.Alt : 0)
                         | ((m & KeyModifiers.Shift) != 0 ? ChordMods.Shift : 0)
                         | ((m & KeyModifiers.Meta) != 0 ? ChordMods.Win : 0);
                var key = e.Key.ToString();
                if (EmiDeskChord.Validate(mods, key, CoreSettings.Current) is { } why)
                {
                    // Stay in capture so the user can just press something else.
                    TxtHotkeyHint.Text = why;
                    return;
                }

                var chord = EmiDeskChord.Format(mods, key);
                _capturing = false;
                Persist(() => CoreSettings.Current.EmiDeskHotkey = chord);
                TxtHotkeyHint.Text = Loc.Get("set2_emi_desk_hotkey_hint");
                RefreshHotkeyButton();

                EmiDeskService.Instance.ApplyHotkey();
                // Registration can still fail: another client may already hold the combo.
                if (!EmiDeskService.Instance.HotkeyArmed)
                    TxtHotkeyHint.Text = Loc.GetF("emi_desk_hotkey_err_taken", chord);
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

        private void RefreshHotkeyButton()
        {
            try
            {
                var s = CoreSettings.Current;
                var chord = s.EmiDeskHotkey;
                BtnHotkey.Content = string.IsNullOrWhiteSpace(chord)
                    ? Loc.Get("emi_desk_hotkey_unbound")
                    : chord;
                BtnHotkey.IsEnabled = s.EmiDeskEnabled;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] hotkey button refresh failed");
            }
        }

        // ------------------------------------------------------------------ her ring

        /// <summary>
        /// The wall is <see cref="EmiRingPicker"/>, shared verbatim with her options panel. This
        /// section owns only the count line and the reset button, so they can sit in the row above
        /// in the section's own hue; both follow the picker's <c>StateChanged</c>.
        ///
        /// <para>The pin logic deliberately does NOT live here. There is one pin store, and the
        /// surest way to end up with two was to write the picker twice.</para>
        /// </summary>
        private void WireRingPicker()
        {
            // The picker counts in its own ctor, before this line runs, so seed the row once here
            // rather than waiting for a pin to move.
            RingPicker.StateChanged += (_, _) => RefreshRingRow();
            RefreshRingRow();
        }

        private void RefreshRingRow()
        {
            try
            {
                // Guarded exactly as WPF is: a blank count line would wipe the static hint.
                TxtRingHint.Text = string.IsNullOrEmpty(RingPicker.HintText)
                    ? Loc.Get("set2_emi_desk_ring_hint")
                    : RingPicker.HintText;
                BtnRingReset.IsEnabled = RingPicker.CanReset;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] ring row refresh failed");
            }
        }

        /// <summary>"Let her choose": the picker drops every pin through the suggester.</summary>
        private void BtnRingReset_Click(object? sender, RoutedEventArgs e)
        {
            try { RingPicker.ResetPins(); }
            catch (Exception ex) { Log.Warning(ex, "[EmiDesk] ring reset failed"); }
        }
    }
}
