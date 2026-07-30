using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Controls
{
    /// <summary>
    /// The one animated treatment every premium gate in the app wears: a very slow mod-tinted fog
    /// drift across the scrim, a breathing glow behind the padlock, and a sheen that crosses the CTA
    /// every twelve seconds. Eight surfaces, one implementation.
    ///
    /// It is a DECORATOR, not a replacement control, and that is deliberate. The gates come in two
    /// unrelated shapes - the six older "scrim + centred stack" overlays (Takeover, Awareness,
    /// Haptics, Remote, Lockdown, She's Listening) and the two newer "scrim + gradient-bordered
    /// card" ones (Blink Trainer, Graded Intake) - and the newer pair swap their copy at runtime and
    /// carry per-tab localisation keys. Re-authoring all eight into a single UserControl would mean
    /// rewriting eight XAML blocks and moving gating LOGIC, which this pass must not touch. Instead
    /// <see cref="Attach"/> takes whatever Border a tab already calls its gate, reparents its child
    /// under a Grid so an <see cref="AmbientFxCanvas"/> can sit behind it, and finds the padlock and
    /// the CTA by walking the LOGICAL tree (which is populated before anything has rendered).
    /// Nothing about the gate's visibility, hit-testing or entitlement changes.
    ///
    /// House rules (FX plan section 2):
    ///   * one attachment per gate, cached - <see cref="Attach"/> is safe to call from every
    ///     RefreshPremiumGate, which is what actually happens;
    ///   * every clock is gated on <see cref="MotionFx.AllowAmbientLoops"/> plus the gate actually
    ///     being visible, and <see cref="Refresh"/> parks all three the moment either stops holding.
    ///     A collapsed gate on a tab nobody has opened costs nothing at all;
    ///   * colour only ever from <see cref="FxTheme"/>, re-read on <c>ModChanged</c>;
    ///   * BeginAnimation only - Storyboard.SetTargetName silently no-ops across the tab UserControl
    ///     namescopes these gates live in;
    ///   * every entry point wrapped. A decoration may never be why a gate fails to appear.
    /// </summary>
    internal sealed class PremiumGateFx
    {
        /// <summary>Deliberately faint: this is air behind a modal scrim, not weather.</summary>
        private const double ScrimFogIntensity = 0.34;
        private const int ScrimFogPuffs = 3;

        private const double LockGlowMinOpacity = 0.30;
        private const double LockGlowMaxOpacity = 0.85;
        private const double LockGlowSeconds = 3.6;
        private const double LockGlowBlurRadius = 22;
        private const int AmbientFrameRate = 24;

        /// <summary>The CTA's corner radius. Both gate families use pill-ish buttons; 8 sits inside
        /// the smaller of the two so the sheen never bleeds past a corner.</summary>
        private const double CtaCornerRadius = 8;

        private static readonly Dictionary<Border, PremiumGateFx> _attached = new();

        private readonly Border _gate;
        private readonly AmbientFxCanvas _fog;
        private readonly Image? _lock;
        private readonly ButtonBase? _cta;

        private DropShadowEffect? _lockGlow;
        private CardSheenAdorner? _ctaSheen;
        private AdornerLayer? _ctaSheenLayer;
        private bool _ctaSheenRetryQueued;
        private bool _running;

        private PremiumGateFx(Border gate, AmbientFxCanvas fog, Image? padlock, ButtonBase? cta)
        {
            _gate = gate;
            _fog = fog;
            _lock = padlock;
            _cta = cta;
        }

        // ============================== attach ==============================

        /// <summary>
        /// Decorates a gate once and re-evaluates its clocks. Call it from wherever the gate's
        /// Visibility is set - it is idempotent, cheap on the second call, and never throws.
        /// Returns null when the gate is not a shape this can decorate.
        /// </summary>
        internal static PremiumGateFx? Attach(Border? gate)
        {
            if (gate == null) return null;
            try
            {
                if (_attached.TryGetValue(gate, out var existing))
                {
                    existing.Refresh();
                    return existing;
                }

                // The gate's own content moves under a Grid so the canvas can sit behind it. Grid
                // children paint in declaration order, so the canvas goes in first.
                if (gate.Child is not UIElement content) return null;
                if (content is Grid g && g.Children.Count > 0 && g.Children[0] is AmbientFxCanvas) return null;

                var fog = new AmbientFxCanvas();
                var host = new Grid();
                gate.Child = null;              // must leave the old parent before joining the new one
                host.Children.Add(fog);
                host.Children.Add(content);
                gate.Child = host;

                var padlock = FindLogical<Image>(content);
                var cta = FindLogical<ButtonBase>(content);

                var fx = new PremiumGateFx(gate, fog, padlock, cta);
                _attached[gate] = fx;

                gate.IsVisibleChanged += fx.OnGateVisibilityChanged;
                if (App.Mods != null) App.Mods.ModChanged += fx.OnModChanged;

                fx.Refresh();
                return fx;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("PremiumGateFx.Attach: {E}", ex.Message);
                return null;
            }
        }

        /// <summary>Re-evaluates every attached gate - used after a mod switch or a settings change.</summary>
        internal static void RefreshAll()
        {
            try
            {
                foreach (var fx in new List<PremiumGateFx>(_attached.Values)) fx.Refresh();
            }
            catch (Exception ex) { App.Logger?.Debug("PremiumGateFx.RefreshAll: {E}", ex.Message); }
        }

        // ============================== the gate ==============================

        private void OnGateVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => Refresh();

        private void OnModChanged(object? sender, ModPackage mod)
        {
            void Apply()
            {
                try
                {
                    // The sheen samples FxTheme at construction, so a re-tint is a rebuild.
                    DetachCtaSheen();
                    _lockGlow = null;
                    _lock?.ClearValue(UIElement.EffectProperty);
                    Refresh();
                }
                catch (Exception ex) { App.Logger?.Debug("PremiumGateFx.OnModChanged: {E}", ex.Message); }
            }
            try
            {
                var d = _gate.Dispatcher;
                if (d.CheckAccess()) Apply();
                else d.BeginInvoke((Action)Apply);
            }
            catch { }
        }

        /// <summary>
        /// Starts the three clocks when the gate is on screen and motion is allowed; parks all of
        /// them otherwise. This is the only place any of them are started or stopped.
        /// </summary>
        internal void Refresh()
        {
            try
            {
                bool want = _gate.Visibility == Visibility.Visible
                            && _gate.IsVisible
                            && MotionFx.AllowAmbientLoops;

                if (!want)
                {
                    if (!_running) return;
                    _running = false;
                    _fog.Stop();
                    ApplyLockGlow(false);
                    DetachCtaSheen();
                    return;
                }

                if (!_running)
                {
                    _running = true;
                    _fog.StartLayers(new AmbientFxConfig
                    {
                        Layers = AmbientFxLayers.FogDrift,
                        Intensity = ScrimFogIntensity,
                        FogPuffs = ScrimFogPuffs,
                    });
                    ApplyLockGlow(true);
                }
                // Retried on every refresh: the adorner layer does not exist until the gate has
                // actually been rendered, which is usually a tab show or two after Attach.
                AttachCtaSheen();
                if (_ctaSheen == null) QueueCtaSheenRetry();
            }
            catch (Exception ex) { App.Logger?.Debug("PremiumGateFx.Refresh: {E}", ex.Message); }
        }

        // ============================== padlock glow ==============================

        /// <summary>
        /// A breathing glow BEHIND the padlock rather than an opacity pulse ON it - a lock that
        /// fades in and out reads as a rendering bug, a lock that glows reads as a promise. The glow
        /// is a DropShadowEffect at zero depth (the app's standard glow), tinted from FxTheme, with
        /// only its Opacity animated: an animated BlurRadius re-rasterises the kernel every frame,
        /// which is the expensive part.
        /// </summary>
        private void ApplyLockGlow(bool on)
        {
            var padlock = _lock;
            if (padlock == null) return;
            try
            {
                var tier = PerformanceProfile.CurrentTier;
                if (!on || !PerformanceProfile.AllowGlow(tier))
                {
                    if (_lockGlow != null) _lockGlow.BeginAnimation(DropShadowEffect.OpacityProperty, null);
                    _lockGlow = null;
                    padlock.ClearValue(UIElement.EffectProperty);
                    return;
                }

                if (_lockGlow == null || !ReferenceEquals(padlock.Effect, _lockGlow))
                {
                    _lockGlow = new DropShadowEffect
                    {
                        Color = FxTheme.GlowColor,
                        BlurRadius = Math.Min(LockGlowBlurRadius, PerformanceProfile.MaxGlowBlurRadius(tier)),
                        ShadowDepth = 0,
                        Opacity = LockGlowMaxOpacity,
                    };
                    padlock.Effect = _lockGlow;
                }

                var anim = new DoubleAnimation(LockGlowMinOpacity, LockGlowMaxOpacity,
                                               TimeSpan.FromSeconds(LockGlowSeconds))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(anim, AmbientFrameRate);
                _lockGlow.BeginAnimation(DropShadowEffect.OpacityProperty, anim);
            }
            catch (Exception ex) { App.Logger?.Debug("PremiumGateFx.ApplyLockGlow: {E}", ex.Message); }
        }

        // ============================== CTA sheen ==============================

        /// <summary>
        /// The same travelling band the Presets tab puts on a selected card, in the adorner layer so
        /// it costs the button no element and no layout pass. Two of the eight CTAs wear a shared
        /// Style whose template we must not touch, which is exactly why this is an adorner.
        /// </summary>
        private void AttachCtaSheen()
        {
            if (_cta == null || _ctaSheen != null) return;
            try
            {
                if (!_cta.IsVisible || _cta.ActualWidth <= 1) return;
                var layer = AdornerLayer.GetAdornerLayer(_cta);
                if (layer == null)
                {
                    // Not rendered yet. Harmless - the next Refresh (tab show, mod switch, gate
                    // repaint) tries again, and there is nothing to see in the meantime anyway.
                    App.Logger?.Debug("PremiumGateFx: no adorner layer for the gate CTA yet");
                    return;
                }
                var sheen = new CardSheenAdorner(_cta, CtaCornerRadius);
                layer.Add(sheen);
                sheen.Start();
                _ctaSheen = sheen;
                _ctaSheenLayer = layer;
            }
            catch (Exception ex) { App.Logger?.Debug("PremiumGateFx.AttachCtaSheen: {E}", ex.Message); }
        }

        /// <summary>
        /// One deferred retry per attempt. The gate is shown by flipping a Visibility, and at that
        /// instant the CTA has not been measured yet - ActualWidth is 0 and there is no adorner
        /// layer - so the first AttachCtaSheen always misses on a first tab visit. Normal priority
        /// on purpose: DispatcherPriority.Loaded is starved in this window and would never run.
        /// </summary>
        private void QueueCtaSheenRetry()
        {
            if (_cta == null || _ctaSheenRetryQueued) return;
            _ctaSheenRetryQueued = true;
            try
            {
                _gate.Dispatcher.BeginInvoke(new Action(() =>
                {
                    _ctaSheenRetryQueued = false;
                    try { if (_running) AttachCtaSheen(); }
                    catch (Exception ex) { App.Logger?.Debug("PremiumGateFx retry: {E}", ex.Message); }
                }), System.Windows.Threading.DispatcherPriority.Normal);
            }
            catch { _ctaSheenRetryQueued = false; }
        }

        private void DetachCtaSheen()
        {
            try
            {
                var sheen = _ctaSheen;
                var layer = _ctaSheenLayer;
                _ctaSheen = null;
                _ctaSheenLayer = null;
                if (sheen == null) return;
                sheen.Stop();
                layer?.Remove(sheen);
            }
            catch (Exception ex) { App.Logger?.Debug("PremiumGateFx.DetachCtaSheen: {E}", ex.Message); }
        }

        // ============================== tree walk ==============================

        /// <summary>
        /// First matching descendant in the LOGICAL tree. The visual tree is the wrong one here:
        /// these gates are decorated the moment their Visibility is first set, which for most of
        /// them is before the tab has ever been shown, so there is no visual tree to walk yet - but
        /// the logical children exist from InitializeComponent onwards.
        /// </summary>
        private static T? FindLogical<T>(object? root) where T : class
        {
            if (root is T hit) return hit;
            if (root is not DependencyObject d) return null;
            try
            {
                foreach (var child in LogicalTreeHelper.GetChildren(d))
                {
                    if (FindLogical<T>(child) is { } found) return found;
                }
            }
            catch { }
            return null;
        }
    }
}
