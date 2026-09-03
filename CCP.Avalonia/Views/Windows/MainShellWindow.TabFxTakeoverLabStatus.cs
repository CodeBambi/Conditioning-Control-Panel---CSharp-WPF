// PORTED-IN-PART from ConditioningControlPanel/MainWindow/MainWindow.TabFxTakeoverLabStatus.cs
// (336 lines). The status-pulse half is LIVE here; the Lab half was already a tombstone in WPF.
//
// What is real: the registry (_statusPulses), its single decision point (ApplyStatusPulse) and the
// four per-tab entry points. A dot breathes only while its feature genuinely runs, which is the
// whole "zero idle loops" rule the WPF file is held to. WPF's DoubleAnimation with AutoReverse +
// RepeatBehavior.Forever over DropShadowEffect.Opacity becomes one Avalonia Animation with
// PlaybackDirection.Alternate + IterationCount.Infinite over the same property - the same motion,
// not an approximation - cancelled through a CancellationTokenSource the way every other loop on
// this head is (see MainShellWindow.DeeperFx.cs).
//
// NOTHING CALLS THE FOUR ENTRY POINTS YET. On WPF each status tab calls its Set*StatusPulse from
// the state-change method it already has; on this head those live in MainShellWindow.BlinkTrainer.cs,
// .Haptics.cs, .SheListening.cs and Views/Tabs/AwarenessTabView.axaml.cs, none of which this layer
// owns. MainShellWindow.SheListening.cs already paints SL_StatusDot's Fill and names the missing
// pulse in its own header, so wiring it is one line there.
//
// Deviations, each because the symbol is not on this head:
//   * MotionFx.AllowAmbientLoops and PerformanceProfile.CurrentTier / AllowGlow / MaxGlowBlurRadius
//     (ConditioningControlPanel/Services/MotionFx.cs, /Services/PerformanceProfile.cs) are not in
//     Core. The gate keeps the two halves that ARE here - window focus and not-minimised - and the
//     blur is used unclamped. Restore both when they move: two `&&` and one Math.Min.
//   * FxTheme.GlowColor (ConditioningControlPanel/Services/FxTheme.cs) is the mod's glow. This head
//     resolves the same slot through the FxGlowColor theme key, which is what a mod switch
//     rewrites, so the retint on CoreMods.ModChanged still lands.
//   * PremiumGateFx.RefreshAll() - ConditioningControlPanel/Controls/PremiumGateFx.cs is not
//     ported; the gate borders on this head are static XAML. Dropped, not faked.
//   * AmbientFrameRate (Timeline.SetDesiredFrameRate) has no Avalonia twin - same reason as
//     MainShellWindow.AmbientFx.cs's note.
//   * Tab visibility: WPF's IsVisibleChanged becomes a PropertyChanged filter on IsVisibleProperty,
//     which is exactly the property ShowTab writes.
//
// One entry point cannot reach its dot yet: SetHapticsStatusPulse. Views/Tabs/HapticsTabView.axaml
// is ported but nothing hosts it - StudioTabView.axaml:249 carries a placard named PanelHaptics
// where <tabs:HapticsTabView x:Name="PanelHaptics"/> belongs. The lookup is deliberately TWO hops,
// StudioRack -> PanelHaptics -> HapticStatusDot, because the dot lives in HapticsTabView's own
// namescope and a single FindControl off the rack would search the rack's scope only - the
// cross-namescope trap WPF's own MainWindow.Animations.cs comments describe. It no-ops safely
// against today's placard and starts working the moment that Border becomes the page.

using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>Glow-breath bracket and period for every status dot. 2.8s is the slow end of
        /// "alive" and deliberately clear of the 0.8s blink the Blink Trainer used to run, which
        /// read as a warning light rather than as a heartbeat.</summary>
        private const double StatusPulseMinOpacity = 0.22;
        private const double StatusPulseMaxOpacity = 0.95;
        private const double StatusPulseSeconds = 2.8;

        /// <summary>Glow radius per dot size. The 8px Awareness dot and the 64px She's Listening
        /// disc cannot share one number without the small one turning into a smudge.</summary>
        private const double StatusPulseBlurSmall = 10;
        private const double StatusPulseBlurLarge = 26;

        private bool _pr4aFxInitialized;

        /// <summary>Every status dot currently asking for a pulse, and how it wants to look. The
        /// dictionary is the single record of intent; <see cref="ApplyStatusPulse"/> is the single
        /// place that turns intent into (or out of) a clock.</summary>
        private readonly Dictionary<Control, StatusPulseRequest> _statusPulses = new();

        private sealed class StatusPulseRequest
        {
            internal bool Wanted;
            internal double Blur;
            internal Color Tint;
            internal DropShadowEffect? Glow;
            internal CancellationTokenSource? Clock;
        }

        /// <summary>Window focus + not minimised: the ambient gate every loop in this file passes
        /// through. See the header for the two halves that are missing.</summary>
        private bool Pr4aAmbientAllowed => IsActive && WindowState != WindowState.Minimized;

        /// <summary>The mod's glow colour, from the one theme key a mod switch rewrites. The
        /// fallback is the shipped FxGlowColor, so a missing key tints rather than throwing.</summary>
        private Color Pr4aGlowColor =>
            this.TryFindResource("FxGlowColor", out var found) && found is Color c
                ? c : Color.FromRgb(0xFF, 0x69, 0xB4);

        // ============================== lifecycle ==============================

        /// <summary>
        /// Wires this file's hooks once, from whichever status surface is refreshed first. Nothing
        /// a user who never opens one of those tabs has to pay for.
        /// </summary>
        private void EnsurePr4aFx()
        {
            if (_pr4aFxInitialized) return;
            _pr4aFxInitialized = true;
            try
            {
                // A plain Animation does not park itself on deactivate/minimise the way
                // AmbientFxCanvas does, so the pulses need this window funnel.
                Activated += OnPr4aFxWindowStateish;
                Deactivated += OnPr4aFxWindowStateish;
                PropertyChanged += OnPr4aFxWindowProperty;

                HookPr4aTab(Named<Control>("BlinkTrainerTab"));
                HookPr4aTab(Named<Control>("SheListeningTab"));
                HookPr4aTab(Named<Control>("AwarenessTab"));

                CoreMods.ModChanged += OnPr4aModChanged;
                // ModChanged is a STATIC event and this is an instance handler, so the
                // subscription would root the window for the life of the process.
                Closed += (_, _) => CoreMods.ModChanged -= OnPr4aModChanged;
            }
            catch (Exception ex) { Log.Warning(ex, "EnsurePr4aFx failed"); }
        }

        private void HookPr4aTab(Control? tab)
        {
            if (tab == null) return;
            tab.PropertyChanged -= OnPr4aTabPropertyChanged;
            tab.PropertyChanged += OnPr4aTabPropertyChanged;
        }

        private void OnPr4aFxWindowStateish(object? sender, EventArgs e) => ApplyPr4aFxLoops();

        private void OnPr4aFxWindowProperty(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == WindowStateProperty) ApplyPr4aFxLoops();
        }

        /// <summary>ShowTab writes IsVisible on the tab panel; that is the arrival and departure
        /// this reacts to.</summary>
        private void OnPr4aTabPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property != Visual.IsVisibleProperty) return;
            try { ApplyPr4aFxLoops(); }
            catch (Exception ex) { Log.Debug("OnPr4aTabPropertyChanged: {E}", ex.Message); }
        }

        /// <summary>A mod switch re-tints every status glow.</summary>
        private void OnPr4aModChanged(object? sender, ModPackage mod)
        {
            void Apply()
            {
                try
                {
                    var tint = Pr4aGlowColor;
                    foreach (var req in _statusPulses.Values)
                    {
                        req.Tint = tint;
                        if (req.Glow != null) req.Glow.Color = tint;
                    }
                }
                catch (Exception ex) { Log.Debug("OnPr4aModChanged: {E}", ex.Message); }
            }

            try
            {
                if (Dispatcher.UIThread.CheckAccess()) Apply();
                else Dispatcher.UIThread.Post(Apply);
            }
            catch { /* a retint is never worth a throw */ }
        }

        /// <summary>Re-evaluates every loop this file owns against the current gate.</summary>
        private void ApplyPr4aFxLoops()
        {
            if (!_pr4aFxInitialized) return;
            try { ApplyPr4aStatusPulses(); }
            catch (Exception ex) { Log.Debug("ApplyPr4aFxLoops: {E}", ex.Message); }
        }

        // ============================== status pulses ==============================

        /// <summary>
        /// Records whether a status element wants to pulse and re-evaluates it. The ONLY entry
        /// point, so "the feature stopped" and "the window lost focus" land in the same place.
        /// </summary>
        private void SetStatusPulse(Control? dot, bool on, bool large = false)
        {
            if (dot == null) return;
            try
            {
                if (!_statusPulses.TryGetValue(dot, out var req))
                {
                    if (!on) return;                 // nothing to remember about a dot that is off
                    _statusPulses[dot] = req = new StatusPulseRequest();
                }
                req.Wanted = on;
                req.Blur = large ? StatusPulseBlurLarge : StatusPulseBlurSmall;
                req.Tint = Pr4aGlowColor;
                ApplyStatusPulse(dot, req);
            }
            catch (Exception ex) { Log.Debug("SetStatusPulse: {E}", ex.Message); }
        }

        private void ApplyPr4aStatusPulses()
        {
            try
            {
                foreach (var kvp in _statusPulses) ApplyStatusPulse(kvp.Key, kvp.Value);
            }
            catch (Exception ex) { Log.Debug("ApplyPr4aStatusPulses: {E}", ex.Message); }
        }

        /// <summary>
        /// One dot, one decision. The pulse is a breathing glow BEHIND the dot rather than an
        /// opacity blink ON it: a status light that keeps disappearing reads as a fault, one that
        /// breathes reads as a pulse. Only the effect's Opacity moves - an animated BlurRadius
        /// re-rasterises the kernel every frame, which is the expensive part.
        ///
        /// Off means the dot goes back to exactly what its own code painted: the local Effect is
        /// cleared and Opacity is 1. Nothing here ever touches Fill, so the tabs keep full
        /// ownership of colour.
        /// </summary>
        private void ApplyStatusPulse(Control dot, StatusPulseRequest req)
        {
            try
            {
                bool run = req.Wanted && dot.IsVisible && Pr4aAmbientAllowed;

                if (!run)
                {
                    StopStatusPulseClock(req);
                    req.Glow = null;
                    dot.Opacity = 1;
                    dot.ClearValue(Visual.EffectProperty);
                    return;
                }

                if (req.Glow != null && ReferenceEquals(dot.Effect, req.Glow))
                    return;                          // already breathing, leave the clock alone

                StopStatusPulseClock(req);
                req.Glow = new DropShadowEffect
                {
                    Color = req.Tint,
                    BlurRadius = req.Blur,
                    OffsetX = 0,
                    OffsetY = 0,
                    Opacity = StatusPulseMaxOpacity,
                };
                dot.Effect = req.Glow;

                req.Clock = new CancellationTokenSource();
                var anim = new Animation
                {
                    Duration = TimeSpan.FromSeconds(StatusPulseSeconds),
                    IterationCount = IterationCount.Infinite,
                    PlaybackDirection = PlaybackDirection.Alternate,
                    Easing = new SineEaseInOut(),
                    Children =
                    {
                        new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(DropShadowEffect.OpacityProperty, StatusPulseMinOpacity) } },
                        new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(DropShadowEffect.OpacityProperty, StatusPulseMaxOpacity) } },
                    },
                };
                _ = anim.RunAsync(req.Glow, req.Clock.Token);
            }
            catch (Exception ex) { Log.Debug("ApplyStatusPulse: {E}", ex.Message); }
        }

        private static void StopStatusPulseClock(StatusPulseRequest req)
        {
            var clock = req.Clock;
            if (clock == null) return;
            req.Clock = null;
            try { clock.Cancel(); clock.Dispose(); } catch { }
        }

        // ---- the four tabs' entry points -------------------------------------------

        /// <summary>Blink Trainer: the dot breathes while a session is actually RUNNING - never in
        /// IdleReady, which is precisely the state where nothing is happening.</summary>
        internal void SetBlinkTrainerStatusPulse(bool running)
        {
            EnsurePr4aFx();
            SetStatusPulse(Named<Control>("BlinkTrainerTab")?.FindControl<Control>("BlinkTrainerStatusDot"), running);
        }

        /// <summary>Haptics: the connection dot breathes while a device is actually connected. See
        /// the header - the view exists but nothing hosts it, so this no-ops today.</summary>
        internal void SetHapticsStatusPulse(bool connected)
        {
            EnsurePr4aFx();
            SetStatusPulse(
                StudioRack?.FindControl<Control>("PanelHaptics")?.FindControl<Control>("HapticStatusDot"),
                connected);
        }

        /// <summary>She's Listening: the 64px mic disc breathes while the mic is armed.</summary>
        internal void SetSheListeningStatusPulse(bool armed)
        {
            EnsurePr4aFx();
            SetStatusPulse(Named<Control>("SheListeningTab")?.FindControl<Control>("SL_StatusDot"), armed, large: true);
        }

        /// <summary>Awareness: the dot breathes while the engine is live.</summary>
        internal void SetAwarenessStatusPulse(bool live)
        {
            EnsurePr4aFx();
            SetStatusPulse(Named<Control>("AwarenessTab")?.FindControl<Control>("AwarenessStatusDot"), live);
        }
    }
}
