using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Views/Tabs/LockdownTabView.xaml.cs.
    ///
    /// The view-only half is carried over intact: the intensity pills still behave as radio
    /// buttons, the master switch still greys the Possession block rather than hiding it, and the
    /// Emergency Exit slab still sinks under the finger. Everything that reads or writes
    /// AppSettings, forwards to MainWindow, or opens the exit games is a stub - those live behind
    /// App.* in the WPF head and get wired when they move to Core.
    /// </summary>
    public partial class LockdownTabView : UserControl
    {
        /// <summary>
        /// True while LoadPossessionSettings is writing the controls, so the change handlers do not
        /// write the value they were just given straight back into settings (and, worse, re-enter
        /// through the master switch's grey-out pass).
        /// </summary>
        private bool _loadingPossession;

        /// <summary>Stands in for AppSettings.LockdownPossessionIntensity so a tab re-show does not
        /// throw the user's pick away. Delete with the stub in LoadPossessionSettings.</summary>
        private int _intensity = 1;

        // The compiled-XAML x:Name fields are only populated by the generated
        // InitializeComponent(); this head loads its views with AvaloniaXamlLoader.Load, so every
        // control the code touches is resolved by name here, as in AdornedAvatar and EmiRingPicker.
        private readonly StackPanel _possessionBlock;
        private readonly CheckBox _chkPossessionEnabled;
        private readonly ToggleButton _btnPossGentle;
        private readonly ToggleButton _btnPossEerie;
        private readonly ToggleButton _btnPossFullDoki;

        public LockdownTabView()
        {
            AvaloniaXamlLoader.Load(this);

            _possessionBlock = this.FindControl<StackPanel>("PossessionBlock")!;
            _chkPossessionEnabled = this.FindControl<CheckBox>("ChkPossessionEnabled")!;
            _btnPossGentle = this.FindControl<ToggleButton>("BtnPossGentle")!;
            _btnPossEerie = this.FindControl<ToggleButton>("BtnPossEerie")!;
            _btnPossFullDoki = this.FindControl<ToggleButton>("BtnPossFullDoki")!;

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

            // ponytail: placeholder for the render proof. On WPF the active panel is flipped by
            // MainWindow.Lab.cs when a lockdown starts; nothing on this head does that yet, so the
            // Emergency Exit slab would be unreachable and unproven. Delete this line and the
            // sample readout below the moment the host moves to Core.
            this.FindControl<StackPanel>("LockdownActivePanel")!.IsVisible = true;
            this.FindControl<TextBlock>("TxtPossessionRung")!.Text =
                Loc.GetF("lockdown_poss_readout_fmt", Loc.Get("lockdown_poss_rung_1"));
            var pips = this.FindControl<StackPanel>("PossessionPips")!;
            for (var i = 0; i < pips.Children.Count; i++)
                if (pips.Children[i] is Border pip)
                    pip.Background = new SolidColorBrush(Color.Parse(i <= 1 ? "#FF8A5C" : "#33FF8A5C"));
        }

        // ==== Possession + Safeties ======================================================

        /// <summary>Paints every control on the card from AppSettings. Never writes anything back.</summary>
        private void LoadPossessionSettings()
        {
            // ponytail: needs AppSettings, wired when it moves to Core. The WPF body reads
            // LockdownPossessionEnabled / TripwiresEnabled / WardenEnabled / Photosafe and the four
            // safeties, then calls the two Apply* helpers below - which ARE ported, so once the
            // settings object lands this is eight assignments and nothing else.
            try
            {
                _loadingPossession = true;
                ApplyIntensityPills(_intensity);
                ApplyPossessionEnabledLook(_chkPossessionEnabled.IsChecked == true);
            }
            finally
            {
                _loadingPossession = false;
            }
        }

        /// <summary>The three pills behave as radio buttons: exactly one is lit, and clicking the lit
        /// one cannot turn the setting off, because there is no "no intensity".</summary>
        private void ApplyIntensityPills(int intensity)
        {
            _btnPossGentle.IsChecked = intensity == 0;
            _btnPossEerie.IsChecked = intensity == 1;
            _btnPossFullDoki.IsChecked = intensity == 2;
        }

        /// <summary>Greys rather than hides: see the XAML comment on the Possession block.</summary>
        private void ApplyPossessionEnabledLook(bool on)
        {
            if (_possessionBlock == null) return;
            _possessionBlock.IsEnabled = on;
            _possessionBlock.Opacity = on ? 1.0 : 0.4;
        }

        private void ChkPossessionEnabled_Changed(object? sender, RoutedEventArgs e)
        {
            if (_loadingPossession) return;
            // ponytail: needs AppSettings (LockdownPossessionEnabled + Save), wired when it moves
            // to Core. The grey-out is view-only, so it works now.
            ApplyPossessionEnabledLook(_chkPossessionEnabled.IsChecked == true);
        }

        private void PossIntensity_Click(object? sender, RoutedEventArgs e)
        {
            if (_loadingPossession) return;

            // Tag carries the value so all three pills share one handler and the mapping lives
            // next to the label the user actually reads.
            if (sender is not ToggleButton tb || tb.Tag is not string tag || !int.TryParse(tag, out var value))
                return;

            // ponytail: needs AppSettings (LockdownPossessionIntensity + Save), wired when it moves
            // to Core. The radio behaviour is view-only, so it works now.
            _intensity = value;
            _loadingPossession = true;
            try { ApplyIntensityPills(_intensity); }
            finally { _loadingPossession = false; }
        }

        // ponytail: the four handlers below need AppSettings (LockdownTripwiresEnabled,
        // LockdownWardenEnabled, LockdownPhotosafe, and the four safeties read together), wired
        // when it moves to Core. Nothing about them is view-only, so the bodies are empty rather
        // than half-right.
        private void ChkPossTripwires_Changed(object? sender, RoutedEventArgs e) { }

        private void ChkPossWarden_Changed(object? sender, RoutedEventArgs e) { }

        private void ChkPossPhotosafe_Changed(object? sender, RoutedEventArgs e) { }

        /// <summary>
        /// All four safeties share one handler: they are read together on Activate and none of them
        /// does anything until then, so there is nothing per-toggle to react to.
        /// </summary>
        private void ChkLockdownSafety_Changed(object? sender, RoutedEventArgs e) { }

        // ==== forwarded to MainWindow ====================================================
        // ponytail: all four need the MainWindow lockdown partials (MainWindow.Lab.cs), wired when
        // they move to Core. On WPF each one is Window.GetWindow(this) is MainWindow mw -> mw.<same>.

        private void BtnActivateLockdown_Click(object? sender, RoutedEventArgs e) { }

        private void BtnGateUnlock_Click(object? sender, RoutedEventArgs e) { }

        private void TxtLockdownExit_KeyDown(object? sender, KeyEventArgs e) { }

        private void TxtLockdownTimer_Click(object? sender, PointerPressedEventArgs e) { }

        // ==== Emergency Exit =============================================================
        // The huge button's own motion. Deliberately NOT routed through Possession: this is the
        // one control on the page that must behave exactly the same every second of a lockdown,
        // so its animations live here, on the view, and answer only to the photosafe setting.

        /// <summary>
        /// Starts the slow ember breath under the slab.
        /// ponytail: needs AppSettings (LockdownPhotosafe) plus a named target for the glow, wired
        /// when settings move to Core. Avalonia cannot name an Effect (AVLN2000), so the WPF
        /// storyboard on EEGlow.Opacity/BlurRadius becomes either a keyframe Animation over a
        /// pseudo-class on the plate Border or a swapped DropShadowEffect instance - decide that
        /// when there is a setting to gate it with. POSSESSION.md: photosafe means no flicker, not
        /// no colour, so the resting glow in the XAML is already the correct photosafe state.
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
        /// ponytail: needs EmergencyExitHostService, wired when it moves to Core. The host owns
        /// everything after that line - the tripwire, the game pick, the verdict and whether the
        /// lockdown actually ends (Services/EmergencyExit/EMERGENCY_EXIT.md).
        /// </summary>
        private void BtnEmergencyExit_Click(object? sender, RoutedEventArgs e) { }
    }
}
