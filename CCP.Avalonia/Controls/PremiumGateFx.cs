using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Controls
{
    /// <summary>
    /// WPF Controls/PremiumGateFx.cs: the one animated treatment every premium gate wears - a very
    /// slow mod-tinted fog drift across the scrim, a breathing glow behind the padlock, and a
    /// <see cref="CardSheenAdorner"/> crossing the CTA every twelve seconds. A DECORATOR:
    /// <see cref="Attach"/> takes the Border a tab calls its gate, reparents its child under a Grid
    /// so an <see cref="AmbientFxCanvas"/> sits behind it, and finds the padlock and CTA by walking
    /// the LOGICAL tree. Visibility, hit-testing and entitlement are never touched.
    /// <para>House rules kept: one cached attachment per gate (safe from every refresh); every
    /// clock gated on AllowAmbientLoops plus the gate actually being visible; colour re-read on
    /// ModChanged; every entry point wrapped - a decoration may never be why a gate fails.</para>
    /// <para>Avalonia: this head draws the padlock as an emoji TextBlock (Avalonia renders colour
    /// emoji natively), so the padlock is the first Image OR the first "🔒" TextBlock.</para>
    /// </summary>
    internal sealed class PremiumGateFx(Border gate, AmbientFxCanvas fog, Control? padlock, Button? cta)
    {
        private const double ScrimFogIntensity = 0.34;
        private const int ScrimFogPuffs = 3;
        private const double LockGlowMinOpacity = 0.30;
        private const double LockGlowMaxOpacity = 0.85;
        private const double LockGlowSeconds = 3.6;
        private const double LockGlowBlurRadius = 22;
        private const double CtaCornerRadius = 8;

        // Weak: a gate whose tab is torn down must not be kept alive by its decoration.
        private static readonly ConditionalWeakTable<Border, PremiumGateFx> Attached = new();

        private DispatcherTimer? _lockClock;
        private long _lockStart;
        private CardSheenAdorner? _ctaSheen;
        private bool _running;
        private IDisposable? _watch;

        /// <summary>The attachment a gate already wears, or null. Test seam.</summary>
        internal static PremiumGateFx? For(Border gate) => Attached.TryGetValue(gate, out var fx) ? fx : null;

        internal AmbientFxCanvas Fog => fog;
        internal bool IsRunning => _running;
        internal bool HasLockGlow => padlock?.Effect is DropShadowEffect;
        internal CardSheenAdorner? CtaSheen => _ctaSheen;
        internal bool GlowTicking => _lockClock?.IsEnabled == true;

        /// <summary>Decorates a gate once and re-evaluates its clocks. Idempotent, never throws;
        /// null when the gate is not a shape this can decorate.</summary>
        internal static PremiumGateFx? Attach(Border? gate)
        {
            if (gate == null) return null;
            try
            {
                if (Attached.TryGetValue(gate, out var existing)) { existing.Refresh(); return existing; }
                if (gate.Child is not Control content) return null;

                var fog = new AmbientFxCanvas();
                var host = new Grid();
                gate.Child = null;              // must leave the old parent before joining the new one
                host.Children.Add(fog);         // paints first, so behind the content
                host.Children.Add(content);
                gate.Child = host;

                var cta = Find<Button>(content, _ => true);
                var fx = new PremiumGateFx(gate, fog,
                    Find<Control>(content, IsPadlock), cta);
                Attached.Add(gate, fx);
                // The CTA is unmeasured when the gate flips visible (WPF queued a retry); its first
                // arrange re-runs the attach instead.
                if (cta != null) cta.PropertyChanged += (_, e) => { if (e.Property == Visual.BoundsProperty) fx.AttachCtaSheen(); };

                // WPF IsVisibleChanged covers ancestors (the shell hides the TAB, not the gate).
                gate.AttachedToVisualTree += (_, _) => fx.WatchVisibility();
                gate.DetachedFromVisualTree += (_, _) =>
                {
                    fx._watch?.Dispose();
                    fx._watch = null;
                    CoreMods.ModChanged -= fx.OnModChanged;   // a static event must not pin a gone tab
                    Dispatcher.UIThread.Post(fx.Refresh);    // the tree still says "attached" mid-event
                };
                if (gate.IsAttachedToVisualTree()) fx.WatchVisibility();
                fx.Refresh();
                return fx;
            }
            catch (Exception ex) { Log.Debug("PremiumGateFx.Attach: {E}", ex.Message); return null; }
        }

        private void WatchVisibility()
        {
            _watch?.Dispose();
            _watch = EffectiveVisibility.Watch(gate, Refresh);
            CoreMods.ModChanged -= OnModChanged;
            CoreMods.ModChanged += OnModChanged;
            Refresh();
        }

        /// <summary>Re-evaluates every attached gate - after a tab/window change.</summary>
        internal static void RefreshAll()
        {
            try { foreach (var pair in Attached.ToArray()) pair.Value.Refresh(); }
            catch (Exception ex) { Log.Debug("PremiumGateFx.RefreshAll: {E}", ex.Message); }
        }

        private void OnModChanged(object? sender, ModPackage mod)
        {
            void Apply()
            {
                try
                {
                    // The sheen and the glow sample the glow colour when built: a re-tint is a rebuild.
                    CardSheenAdorner.Detach(_ctaSheen);
                    (_ctaSheen, _running) = (null, false);
                    ApplyLockGlow(false);
                    Refresh();
                }
                catch (Exception ex) { Log.Debug("PremiumGateFx.OnModChanged: {E}", ex.Message); }
            }
            if (Dispatcher.UIThread.CheckAccess()) Apply();
            else Dispatcher.UIThread.Post(Apply);
        }

        /// <summary>Starts the three clocks when the gate is on screen and motion is allowed; parks
        /// all of them otherwise. The only place any of them start or stop.</summary>
        internal void Refresh()
        {
            try
            {
                bool want = gate.IsVisible && gate.IsEffectivelyVisible && gate.IsAttachedToVisualTree()
                            && AmbientFxCanvas.Env.AllowAmbientLoops;
                if (!want)
                {
                    if (!_running) return;
                    _running = false;
                    fog.Stop();
                    ApplyLockGlow(false);
                    CardSheenAdorner.Detach(_ctaSheen);
                    _ctaSheen = null;
                    return;
                }
                if (!_running)
                {
                    _running = true;
                    fog.StartLayers(new AmbientFxConfig
                    { Layers = AmbientFxLayers.FogDrift, Intensity = ScrimFogIntensity, FogPuffs = ScrimFogPuffs });
                    ApplyLockGlow(true);
                }
                AttachCtaSheen();
            }
            catch (Exception ex) { Log.Debug("PremiumGateFx.Refresh: {E}", ex.Message); }
        }

        /// <summary>A breathing glow BEHIND the padlock, not an opacity pulse ON it - only the
        /// effect's Opacity animates (an animated blur re-rasterises every frame).</summary>
        private void ApplyLockGlow(bool on)
        {
            if (padlock == null) return;
            _lockClock?.Stop();
            _lockClock = null;
            var tier = AmbientFxCanvas.Env.CurrentTier;
            if (!on || !AmbientFxCanvas.Env.AllowGlow(tier)) { padlock.ClearValue(Visual.EffectProperty); return; }
            var glow = new DropShadowEffect
            {
                Color = AmbientFxCanvas.Env.GlowColor,
                BlurRadius = Math.Min(LockGlowBlurRadius, AmbientFxCanvas.Env.MaxGlowBlurRadius(tier)),
                OffsetX = 0,
                OffsetY = 0,
                Opacity = LockGlowMaxOpacity,
            };
            padlock.Effect = glow;
            // WPF: a 3.6s SineEase in/out, auto-reversed, capped at 24fps (SetDesiredFrameRate).
            _lockStart = FxAdorner.Time.GetTimestamp();
            _lockClock = new DispatcherTimer(TimeSpan.FromMilliseconds(1000.0 / 24), DispatcherPriority.Background, (_, _) =>
            {
                double t = FxAdorner.Time.GetElapsedTime(_lockStart).TotalSeconds % (2 * LockGlowSeconds);
                double u = t < LockGlowSeconds ? t / LockGlowSeconds : 2 - (t / LockGlowSeconds);
                glow.Opacity = LockGlowMinOpacity + ((LockGlowMaxOpacity - LockGlowMinOpacity) * (1 - Math.Cos(Math.PI * u)) / 2);
            });   // this constructor starts the timer
        }

        /// <summary>The Presets-style travelling band on the CTA, in the adorner layer so the
        /// button's shared theme is never touched.</summary>
        private void AttachCtaSheen()
        {
            if (cta != null && _ctaSheen == null && _running && cta.IsEffectivelyVisible && cta.Bounds.Width > 1)
                _ctaSheen = CardSheenAdorner.Attach(cta, CtaCornerRadius);
        }

        /// <summary>The gate's padlock: its art Image, a 🔒 TextBlock, or (Fluent icons) an
        /// IconGlyph LockClosed - a converted gate must keep its glow.</summary>
        internal static bool IsPadlock(Control c) =>
            c is Image || (c is TextBlock t && t.Text == "🔒") || (c is IconGlyph g && g.Kind == IconKind.LockClosed);

        /// <summary>First matching descendant in the LOGICAL tree (populated before first render).</summary>
        private static T? Find<T>(Control root, Func<T, bool> match) where T : class
        {
            if (root is T hit && match(hit)) return hit;
            return root.GetLogicalDescendants().OfType<T>().FirstOrDefault(match);
        }
    }
}
