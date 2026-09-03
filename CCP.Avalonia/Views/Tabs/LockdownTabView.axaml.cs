using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Views/Tabs/LockdownTabView.xaml.cs.
    ///
    /// <para>The settings half is restored against <see cref="CoreSettings"/>: the Possession
    /// master switch, the three intensity pills, tripwires / warden / photosafe and the four
    /// Safeties all read and write <c>AppSettings</c> for real, one for one with the WPF bodies,
    /// and the master switch still greys the Possession block rather than hiding it.</para>
    ///
    /// <para><b>Nothing here enforces a lockdown.</b> Activate, the gate unlock, the secret exit
    /// and the timer taps are forwarders into <c>MainWindow.Lab.cs</c> / <c>LockdownService</c>,
    /// which are Win32 and head-side; the Emergency Exit slab needs
    /// <c>EmergencyExitHostService</c>. Those stay stubs and each says so at its own body.</para>
    /// </summary>
    public partial class LockdownTabView : UserControl
    {
        /// <summary>
        /// True while LoadPossessionSettings is writing the controls, so the change handlers do not
        /// write the value they were just given straight back into settings (and, worse, re-enter
        /// through the master switch's grey-out pass).
        ///
        /// Starts true: the XAML wires <c>IsCheckedChanged</c> itself, so a handler can fire from
        /// inside InitializeComponent, before any field is assigned and before the seed has run.
        /// Cleared by the first LoadPossessionSettings.
        /// </summary>
        private bool _loadingPossession = true;

        public LockdownTabView()
        {
            // InitializeComponent, not AvaloniaXamlLoader.Load: only the generated one assigns the
            // x:Name fields this code-behind reads.
            InitializeComponent();

            LoadPossessionSettings();

            // Tabs are shown and hidden rather than rebuilt, so the first attach fires once.
            // Re-read on every show for the same reason BambiTakeoverTabView does: something else
            // can move these behind our back (a settings import replaces the whole object, a
            // future safety panic clears a flag) and a stale toggle here is a toggle that lies.
            // WPF's Loaded + IsVisibleChanged pair maps to Avalonia's AttachedToVisualTree +
            // the IsVisible property changing.
            AttachedToVisualTree += (_, _) => LoadPossessionSettings();
            PropertyChanged += (_, e) =>
            {
                if (e.Property == IsVisibleProperty && IsVisible) LoadPossessionSettings();
            };

            // ponytail: placeholder for the render proof, NOT a settings-driven state. On WPF the
            // Setup/Active swap is driven by a RUNNING lockdown (MainWindow.Lab.cs:760/835, off
            // App.Lockdown), and the rung readout is hooked only for the duration of one
            // (HookPossessionReadout). Neither has a Core seam, so without this line the Emergency
            // Exit slab would be unreachable and unproven. Delete it when the host lands.
            LockdownActivePanel.IsVisible = true;
            TxtPossessionRung.Text =
                Loc.GetF("lockdown_poss_readout_fmt", Loc.Get("lockdown_poss_rung_1"));
            for (var i = 0; i < PossessionPips.Children.Count; i++)
                if (PossessionPips.Children[i] is Border pip)
                    pip.Background = new SolidColorBrush(Color.Parse(i <= 1 ? "#FF8A5C" : "#33FF8A5C"));
        }

        // ==== Possession + Safeties ======================================================

        /// <summary>Paints every control on the card from AppSettings. Never writes anything back.</summary>
        private void LoadPossessionSettings()
        {
            try
            {
                var s = CoreSettings.Current;

                _loadingPossession = true;

                Set(ChkPossessionEnabled, s.LockdownPossessionEnabled);
                Set(ChkPossTripwires, s.LockdownTripwiresEnabled);
                Set(ChkPossWarden, s.LockdownWardenEnabled);
                Set(ChkPossPhotosafe, s.LockdownPhotosafe);

                Set(ChkLockdownStrict, s.LockdownForceStrictLock);
                Set(ChkLockdownNoPanic, s.LockdownDisablePanicKey);
                Set(ChkLockdownSysKeys, s.LockdownBlockSystemKeys);
                Set(ChkLockdownDose, s.LockdownDoseKeeperEnabled);

                ApplyIntensityPills(s.LockdownPossessionIntensity);
                ApplyPossessionEnabledLook(s.LockdownPossessionEnabled);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Lockdown card: failed to load possession settings");
            }
            finally
            {
                _loadingPossession = false;
            }

            // Assign only on a real difference: Avalonia raises IsCheckedChanged on a programmatic
            // set too, and every handler below is a live editor.
            static void Set(CheckBox box, bool value)
            {
                if ((box.IsChecked ?? false) != value) box.IsChecked = value;
            }
        }

        /// <summary>The three pills behave as radio buttons: exactly one is lit, and clicking the lit
        /// one cannot turn the setting off, because there is no "no intensity".</summary>
        private void ApplyIntensityPills(int intensity)
        {
            BtnPossGentle.IsChecked = intensity == 0;
            BtnPossEerie.IsChecked = intensity == 1;
            BtnPossFullDoki.IsChecked = intensity == 2;
        }

        /// <summary>Greys rather than hides: see the XAML comment on the Possession block.</summary>
        private void ApplyPossessionEnabledLook(bool on)
        {
            if (PossessionBlock == null) return;
            PossessionBlock.IsEnabled = on;
            PossessionBlock.Opacity = on ? 1.0 : 0.4;
        }

        private void ChkPossessionEnabled_Changed(object? sender, RoutedEventArgs e)
        {
            if (_loadingPossession) return;
            try
            {
                var s = CoreSettings.Current;
                s.LockdownPossessionEnabled = ChkPossessionEnabled.IsChecked == true;
                ApplyPossessionEnabledLook(s.LockdownPossessionEnabled);
                CoreSettings.Save();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Lockdown card: failed to write LockdownPossessionEnabled");
            }
        }

        private void PossIntensity_Click(object? sender, RoutedEventArgs e)
        {
            if (_loadingPossession) return;
            try
            {
                // Tag carries the value so all three pills share one handler and the mapping lives
                // next to the label the user actually reads.
                if (sender is not ToggleButton tb || tb.Tag is not string tag || !int.TryParse(tag, out var value))
                    return;

                var s = CoreSettings.Current;
                s.LockdownPossessionIntensity = value;

                _loadingPossession = true;
                try { ApplyIntensityPills(s.LockdownPossessionIntensity); }
                finally { _loadingPossession = false; }

                CoreSettings.Save();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Lockdown card: failed to write LockdownPossessionIntensity");
            }
        }

        private void ChkPossTripwires_Changed(object? sender, RoutedEventArgs e)
            => WriteFlag(v => CoreSettings.Current.LockdownTripwiresEnabled = v,
                         ChkPossTripwires, "LockdownTripwiresEnabled");

        private void ChkPossWarden_Changed(object? sender, RoutedEventArgs e)
            => WriteFlag(v => CoreSettings.Current.LockdownWardenEnabled = v,
                         ChkPossWarden, "LockdownWardenEnabled");

        private void ChkPossPhotosafe_Changed(object? sender, RoutedEventArgs e)
            => WriteFlag(v => CoreSettings.Current.LockdownPhotosafe = v,
                         ChkPossPhotosafe, "LockdownPhotosafe");

        /// <summary>One box, one flag, one save - the shape three of the toggles share.</summary>
        private void WriteFlag(Action<bool> write, CheckBox box, string name)
        {
            if (_loadingPossession) return;
            try
            {
                write(box.IsChecked == true);
                CoreSettings.Save();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Lockdown card: failed to write {Setting}", name);
            }
        }

        /// <summary>
        /// All four safeties share one handler: they are read together on Activate and none of them
        /// does anything until then, so there is nothing per-toggle to react to.
        /// </summary>
        private void ChkLockdownSafety_Changed(object? sender, RoutedEventArgs e)
        {
            if (_loadingPossession) return;
            try
            {
                var s = CoreSettings.Current;
                s.LockdownForceStrictLock = ChkLockdownStrict.IsChecked == true;
                s.LockdownDisablePanicKey = ChkLockdownNoPanic.IsChecked == true;
                s.LockdownBlockSystemKeys = ChkLockdownSysKeys.IsChecked == true;
                s.LockdownDoseKeeperEnabled = ChkLockdownDose.IsChecked == true;
                CoreSettings.Save();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Lockdown card: failed to write the lockdown safeties");
            }
        }

        // ==== forwarded to MainWindow ====================================================
        // All four need LockdownService and the MainWindow lockdown partials (MainWindow.Lab.cs).
        // Starting a lockdown is Win32 - strict lock, the panic-key and system-key hooks, the
        // always-on-top cage - so it is head-side by construction and no Core seam is planned.
        // On WPF each body is `Window.GetWindow(this) is MainWindow mw -> mw.<same handler>`.
        // NOTHING on this head enforces a lockdown; these are inert on purpose.

        private void BtnActivateLockdown_Click(object? sender, RoutedEventArgs e) { }

        private void BtnGateUnlock_Click(object? sender, RoutedEventArgs e) { }

        private void TxtLockdownExit_KeyDown(object? sender, KeyEventArgs e) { }

        private void TxtLockdownTimer_Click(object? sender, PointerPressedEventArgs e) { }

        // ==== Emergency Exit =============================================================
        // The huge button's own motion. Deliberately NOT routed through Possession: this is the
        // one control on the page that must behave exactly the same every second of a lockdown,
        // so its animations live here, on the view, and answer only to the photosafe setting.

        /// <summary>
        /// Starts the slow ember breath under the slab. Called by the host when the active panel
        /// is shown.
        /// ponytail: the gate is readable now (<c>CoreSettings.Current.LockdownPhotosafe</c>), but
        /// the target is not: Avalonia cannot name an Effect (AVLN2000), so the WPF storyboard on
        /// EEGlow.Opacity/BlurRadius has to become a keyframe Animation over a pseudo-class on the
        /// plate Border, or a swapped DropShadowEffect instance - an XAML change, and the XAML is
        /// not this layer's. POSSESSION.md: photosafe means no flicker, not no colour, so the
        /// resting glow already in the XAML is the correct photosafe state, which is why an
        /// unstarted pulse is a safe stub rather than a missing one.
        /// </summary>
        internal void StartEmergencyExitPulse() { }

        /// <summary>Stops the breath and puts the glow back where the XAML left it. Nothing to stop
        /// yet - see StartEmergencyExitPulse.</summary>
        internal void StopEmergencyExitPulse() { }

        // The slab still sinks under the finger and comes back, but with no code: the WPF pair of
        // DoubleAnimations on a named ScaleTransform (60 ms down / 140 ms up) is a :pressed style
        // plus one transition in the XAML. Two reasons, both hard: Avalonia cannot name a transform
        // (AVLN2000), and Button marks PointerPressed/Released handled in its class handler, so the
        // ported handlers would have been dead code that renders and reviews as if it worked.

        /// <summary>
        /// Opens the Emergency Exit games.
        /// ponytail: needs EmergencyExitHostService, which owns windows and so stays head-side. The
        /// host owns everything after that line - the tripwire, the game pick, the verdict and
        /// whether the lockdown actually ends (Services/EmergencyExit/EMERGENCY_EXIT.md).
        /// </summary>
        private void BtnEmergencyExit_Click(object? sender, RoutedEventArgs e) { }
    }
}
