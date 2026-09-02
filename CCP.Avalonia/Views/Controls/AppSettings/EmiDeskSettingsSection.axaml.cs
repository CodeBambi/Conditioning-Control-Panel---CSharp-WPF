using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls.AppSettings
{
    /// <summary>
    /// SETTINGS - EMI DESK, ported from the WPF head.
    ///
    /// On WPF this section is self-contained: it reads <c>App.Settings.Current</c> on Loaded,
    /// writes it back on every change, re-arms the summon chord through <c>App.EmiDesk</c> and
    /// validates chords with <c>EmiDeskService.ValidateChord</c>. None of that is reachable from
    /// this head, so the toggles render but persist nothing, and the hotkey button only enters and
    /// leaves capture. What IS ported: the spice combo's three localized rows, and the capture
    /// state machine's shape (click to start, Escape or focus loss to cancel).
    /// </summary>
    public partial class EmiDeskSettingsSection : UserControl
    {
        private bool _loading;
        private bool _capturing;

        private readonly ComboBox _cmbSpice;
        private readonly Button _btnHotkey;

        public EmiDeskSettingsSection()
        {
            AvaloniaXamlLoader.Load(this);
            _cmbSpice = this.FindControl<ComboBox>("CmbSpice")!;
            _btnHotkey = this.FindControl<Button>("BtnHotkey")!;

            Loaded += OnLoaded;
            Unloaded += (_, _) => CancelCapture();

            _btnHotkey.AddHandler(KeyDownEvent, OnHotkeyPreviewKeyDown, RoutingStrategies.Tunnel);
            _btnHotkey.LostFocus += (_, _) => CancelCapture();
        }

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            try
            {
                _loading = true;
                if (_cmbSpice.Items.Count == 0)
                {
                    _cmbSpice.Items.Add(Loc.Get("emi_desk_spice_innocent"));
                    _cmbSpice.Items.Add(Loc.Get("emi_desk_spice_suggestive"));
                    _cmbSpice.Items.Add(Loc.Get("emi_desk_spice_anything"));
                }
                // ponytail: needs App.Settings.Current.EmiDesk*, wired when SettingsService moves to Core
                _cmbSpice.SelectedIndex = 0;
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

        // ponytail: needs App.Settings.Save, wired when SettingsService moves to Core
        private void CmbSpice_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
        }

        // ------------------------------------------------------------------ hotkey capture

        private void BtnHotkey_Click(object? sender, RoutedEventArgs e)
        {
            if (_capturing) { CancelCapture(); return; }
            _capturing = true;
            _btnHotkey.Content = Loc.Get("emi_desk_hotkey_capturing");
            _btnHotkey.Focus();
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
            // ponytail: needs EmiDeskService.ValidateChord/FormatChord + App.EmiDesk.ApplyHotkey; until
            // then a completed chord ends capture without rebinding
            CancelCapture();
        }

        private void CancelCapture()
        {
            if (!_capturing) return;
            _capturing = false;
            RefreshHotkeyButton();
        }

        private void RefreshHotkeyButton()
        {
            // ponytail: needs App.Settings.Current.EmiDeskHotkey; the markup default stands in
            _btnHotkey.Content = "Ctrl+Alt+E";
        }

        // ponytail: needs EmiRingPicker.ResetPins, wired when that control is ported
        private void BtnRingReset_Click(object? sender, RoutedEventArgs e) { }
    }
}
