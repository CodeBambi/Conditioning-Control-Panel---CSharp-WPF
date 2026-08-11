using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// PR-4a of the FX overhaul: <b>Takeover</b>, the shared <b>premium-gate</b> treatment, and the
    /// status pulses on <b>Blink Trainer</b>, <b>Haptics</b>, <b>She's Listening</b> and
    /// <b>Awareness</b>. (<b>Lab</b> was the fifth surface until Phase 6 of the UX restructure
    /// retired it — see the tombstone below.) Nothing here touches the shell (PR-1), the dashboard
    /// (PR-2) or PR-3's tabs.
    ///
    /// What each surface gets, per FX plan section 4:
    ///   * Takeover  - the orb, and only the orb. It is the tab's whole identity, so it is allowed
    ///     to be the most present FX in the app; it is also a Skia control that parks itself the
    ///     instant the tab is collapsed (see Controls/TakeoverOrb.cs). No second loop is added to
    ///     that tab.
    ///   * Lab       - RETIRED (UX restructure, Phase 6). The ember DustField behind the Rabbit
    ///     Hole launcher and the Bureau folder-stamp both moved to the Play door with the cards
    ///     they decorate: the canvas is composed and registered by Views\Tabs\PlayTabView.xaml.cs
    ///     (as RegisterTabFx("play", ...)) and the stamp is dispatched from RefreshPlayCards in
    ///     MainWindow.PlayTab.cs. Nothing in this file paints that surface any more; the four
    ///     status tabs and the Takeover orb below are all that is left of PR-4a.
    ///   * Gates     - Controls/PremiumGateFx.cs, attached from the existing RefreshPremiumGate
    ///     choke points. Gating LOGIC is untouched; only the paint moves.
    ///   * Blink / Haptics / She's Listening / Awareness - status pulses ONLY. The forms stay calm.
    ///
    /// The rule the four status tabs are held to is "zero idle loops": a pulse runs only while the
    /// underlying feature is genuinely running or connected, and is driven from the state-change
    /// method those tabs already call. That is why the Blink Trainer's pulse MOVED - it used to run
    /// in IdleReady, which is precisely the state where nothing is happening.
    ///
    /// House rules (FX plan section 2) this file obeys:
    ///   * every clock asks <see cref="Pr4aAmbientAllowed"/> (window focus + not minimised +
    ///     reduced-motion + the performance tier) AND its own element's visibility before starting,
    ///     and parks at a static resting state otherwise;
    ///   * <c>element.BeginAnimation</c> only - Storyboard.SetTargetName silently no-ops across the
    ///     tab UserControl namescopes these elements live in;
    ///   * ambient timelines are capped at <see cref="AmbientFrameRate"/>;
    ///   * colour only ever from <see cref="FxTheme"/> or a theme key, never a literal;
    ///   * every callback wrapped. Decoration may never be why a tab throws.
    /// </summary>
    public partial class MainWindow
    {
        // ---- tuning ----------------------------------------------------------------

        // RabbitHoleFxIntensity (0.62) left with the canvas it tuned: it is now a private const of
        // Views\Tabs\PlayTabView.xaml.cs, kept to the digit so the portal hero looks identical to
        // the Lab card it replaces. Do not re-declare it here.

        /// <summary>Glow-breath bracket and period for every status dot in this file. 2.8s is the
        /// slow end of "alive" and deliberately clear of the 0.8s blink the Blink Trainer used to
        /// run, which read as a warning light rather than as a heartbeat.</summary>
        private const double StatusPulseMinOpacity = 0.22;
        private const double StatusPulseMaxOpacity = 0.95;
        private const double StatusPulseSeconds = 2.8;

        /// <summary>Glow radius per dot size. The 8px Awareness dot and the 64px She's Listening
        /// disc cannot share one number without the small one turning into a smudge.</summary>
        private const double StatusPulseBlurSmall = 10;
        private const double StatusPulseBlurLarge = 26;

        // ---- state -----------------------------------------------------------------

        private bool _pr4aFxInitialized;

        /// <summary>Every status dot currently asking for a pulse, and how it wants to look. The
        /// dictionary is the single record of intent; <see cref="ApplyPr4aStatusPulses"/> is the
        /// single place that turns intent into (or out of) a clock.</summary>
        private readonly Dictionary<FrameworkElement, StatusPulseRequest> _statusPulses = new();

        private sealed class StatusPulseRequest
        {
            internal bool Wanted;
            internal double Blur;
            internal Color Tint;
            internal DropShadowEffect? Glow;
        }

        /// <summary>Window focus + reduced-motion + the performance tier: the one ambient gate every
        /// loop in this file passes through.</summary>
        private bool Pr4aAmbientAllowed =>
            IsActive
            && WindowState != WindowState.Minimized
            && MotionFx.AllowAmbientLoops;

        // ============================== lifecycle ==============================

        /// <summary>
        /// Wires this PR's hooks up once, from whichever of its five surfaces is refreshed first
        /// (Takeover + the four status tabs; the Lab surface was retired in Phase 6).
        /// Nothing is created that a user who never opens any of them has to pay for - the Skia
        /// surfaces themselves are inert until their tab is visible.
        /// </summary>
        private void EnsurePr4aFx()
        {
            if (_pr4aFxInitialized) return;
            _pr4aFxInitialized = true;
            try
            {
                // The canvases and the orb park themselves on deactivate/minimise; the plain WPF
                // status-pulse animations do not, so they need this window funnel. These are this
                // file's own handlers, so the chrome funnel (MainWindow.ChromeFx.cs) stays untouched.
                Activated += OnPr4aFxWindowStateish;
                Deactivated += OnPr4aFxWindowStateish;
                StateChanged += OnPr4aFxWindowStateish;

                // Tab visibility drives both the pulses and the gate treatments. Hooked from here
                // rather than from ShowTab, which this PR must not edit.
                HookPr4aTab(BlinkTrainerTab);
                HookPr4aTab(HapticsTab);
                HookPr4aTab(SheListeningTab);
                HookPr4aTab(AwarenessTab);
                // The Play door is deliberately NOT hooked here. Its one ambient loop is composed
                // and registered by the view itself (PlayTabView.xaml.cs, on its own
                // IsVisibleChanged), which is what lets the canvas live on the surface it decorates
                // instead of being driven from the window. Adding a hook here would give that
                // canvas two owners.

                if (App.Mods != null) App.Mods.ModChanged += OnPr4aModChanged;
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "EnsurePr4aFx failed"); }
        }

        private void HookPr4aTab(FrameworkElement? tab)
        {
            if (tab == null) return;
            tab.IsVisibleChanged -= OnPr4aTabVisibilityChanged;
            tab.IsVisibleChanged += OnPr4aTabVisibilityChanged;
        }

        private void OnPr4aFxWindowStateish(object? sender, EventArgs e) => ApplyPr4aFxLoops();

        private void OnPr4aTabVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            try
            {
                // Haptics is the one status tab with no refresh-on-show of its own - the dot is
                // painted from the Connect handler and nowhere else - so opening the tab with a
                // device already connected (auto-connect on startup) has to seed the pulse here.
                if (ReferenceEquals(sender, HapticsTab) && HapticsTab?.IsVisible == true)
                    SetHapticsStatusPulse(App.Haptics?.IsConnected == true);

                ApplyPr4aFxLoops();
            }
            catch (Exception ex) { App.Logger?.Debug("OnPr4aTabVisibilityChanged: {E}", ex.Message); }
        }

        /// <summary>A mod switch re-tints every status glow (the canvases and the orb re-read
        /// FxTheme themselves).</summary>
        private void OnPr4aModChanged(object? sender, ModPackage mod)
        {
            void Apply()
            {
                try
                {
                    foreach (var req in _statusPulses.Values)
                    {
                        if (req.Glow != null && !req.Glow.IsFrozen) req.Glow.Color = FxTheme.GlowColor;
                    }
                    PremiumGateFx.RefreshAll();
                }
                catch (Exception ex) { App.Logger?.Debug("OnPr4aModChanged: {E}", ex.Message); }
            }
            try
            {
                if (Dispatcher.CheckAccess()) Apply();
                else Dispatcher.BeginInvoke((Action)Apply);
            }
            catch { }
        }

        /// <summary>Re-evaluates every loop this PR owns against the current gate.</summary>
        private void ApplyPr4aFxLoops()
        {
            if (!_pr4aFxInitialized) return;
            try
            {
                ApplyPr4aStatusPulses();
                PremiumGateFx.RefreshAll();
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyPr4aFxLoops: {E}", ex.Message); }
        }

        // ============================== 1. Lab - RETIRED ==============================
        //
        // TOMBSTONE (UX restructure, Phase 6). OnLabTabShown lived here and did two things for the
        // Lab tab; both left with the cards they decorated when the Lab view was deleted:
        //
        //   * the ember DustField behind the Rabbit Hole launcher - composed and registered by
        //     Views\Tabs\PlayTabView.xaml.cs on its own first show, as RegisterTabFx("play", ...).
        //     The registry key MUST be the key ShowTab passes for that view: SwitchTabFx compares
        //     the incoming key against the registry, so registering the Play canvas under "lab"
        //     would park it on arrival and leave it running on the way out. ShowTab canonicalises
        //     the permanent "lab" alias to "play" before it calls SwitchTabFx for exactly that
        //     reason (MainWindow.TabNavigation.cs, CanonicalTabKey);
        //   * the one-shot Bureau folder stamp - now dispatched from RefreshPlayCards in
        //     MainWindow.PlayTab.cs, at DispatcherPriority.Normal (never Loaded; that queue is
        //     starved in this window).
        //
        // Do not restore either here. The window no longer hooks the Play view's visibility at all
        // (see EnsurePr4aFx above) - the view owns its own loop.

        // ============================== 2. status pulses ==============================

        /// <summary>
        /// Records whether a status element wants to pulse and re-evaluates it. This is what the
        /// four status tabs call from their existing state-change methods; it is the ONLY entry
        /// point, so "the feature stopped" and "the window lost focus" both land in the same place.
        /// </summary>
        private void SetStatusPulse(FrameworkElement? dot, bool on, bool large = false)
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
                req.Tint = FxTheme.GlowColor;
                ApplyStatusPulse(dot, req);
            }
            catch (Exception ex) { App.Logger?.Debug("SetStatusPulse: {E}", ex.Message); }
        }

        private void ApplyPr4aStatusPulses()
        {
            try
            {
                foreach (var kvp in _statusPulses) ApplyStatusPulse(kvp.Key, kvp.Value);
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyPr4aStatusPulses: {E}", ex.Message); }
        }

        /// <summary>
        /// One dot, one decision. The pulse is a breathing DropShadow glow BEHIND the dot rather
        /// than an opacity blink ON it: a status light that keeps disappearing reads as a fault,
        /// one that breathes reads as a pulse. Only the effect's Opacity is animated - an animated
        /// BlurRadius re-rasterises the kernel every frame, which is the expensive part.
        ///
        /// Off means the dot goes back to exactly what its own code painted: local Effect cleared,
        /// Opacity 1. Nothing here ever touches Fill, so the tabs keep full ownership of colour.
        /// </summary>
        private void ApplyStatusPulse(FrameworkElement dot, StatusPulseRequest req)
        {
            try
            {
                var tier = PerformanceProfile.CurrentTier;
                bool run = req.Wanted
                           && dot.IsVisible
                           && Pr4aAmbientAllowed
                           && PerformanceProfile.AllowGlow(tier);

                if (!run)
                {
                    if (req.Glow != null && !req.Glow.IsFrozen)
                        req.Glow.BeginAnimation(DropShadowEffect.OpacityProperty, null);
                    req.Glow = null;
                    dot.BeginAnimation(UIElement.OpacityProperty, null);
                    dot.Opacity = 1;
                    dot.ClearValue(UIElement.EffectProperty);
                    return;
                }

                if (req.Glow == null || !ReferenceEquals(dot.Effect, req.Glow))
                {
                    req.Glow = new DropShadowEffect
                    {
                        Color = req.Tint,
                        BlurRadius = Math.Min(req.Blur, PerformanceProfile.MaxGlowBlurRadius(tier)),
                        ShadowDepth = 0,
                        Opacity = StatusPulseMaxOpacity,
                    };
                    dot.Effect = req.Glow;
                }
                else
                {
                    return;                          // already breathing, leave the clock alone
                }

                var anim = new DoubleAnimation(StatusPulseMinOpacity, StatusPulseMaxOpacity,
                                               TimeSpan.FromSeconds(StatusPulseSeconds))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(anim, AmbientFrameRate);
                req.Glow.BeginAnimation(DropShadowEffect.OpacityProperty, anim);
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyStatusPulse: {E}", ex.Message); }
        }

        // ---- the four tabs' entry points -------------------------------------------

        /// <summary>Blink Trainer: the dot breathes while a session is actually RUNNING. It used to
        /// blink at 0.8s in IdleReady, which was an idle loop on a tab doing nothing.</summary>
        private void SetBlinkTrainerStatusPulse(bool running)
        {
            EnsurePr4aFx();
            SetStatusPulse(BlinkTrainerTab?.BlinkTrainerStatusDot, running);
        }

        /// <summary>Haptics: the connection dot breathes while a device is actually connected.</summary>
        private void SetHapticsStatusPulse(bool connected)
        {
            EnsurePr4aFx();
            SetStatusPulse(HapticsTab?.HapticStatusDot, connected);
        }

        /// <summary>She's Listening: the 64px mic disc breathes while the mic is armed.</summary>
        private void SetSheListeningStatusPulse(bool armed)
        {
            EnsurePr4aFx();
            SetStatusPulse(SheListeningTab?.SL_StatusDot, armed, large: true);
        }

        /// <summary>Awareness: the dot breathes while the engine is live.</summary>
        private void SetAwarenessStatusPulse(bool live)
        {
            EnsurePr4aFx();
            SetStatusPulse(AwarenessTab?.AwarenessStatusDot, live);
        }
    }
}
