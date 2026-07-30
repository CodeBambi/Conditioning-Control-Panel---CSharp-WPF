using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// PR-3 of the FX overhaul: the *Enhancements* (skill tree) pass. Everything here decorates
    /// Views/Tabs/EnhancementsTabView.xaml and the nodes MainWindow.Enhancements.cs draws into it.
    ///
    /// The tab gets exactly ONE looping ambient element, per FX plan section 4:
    ///   • hero  - a sparse <see cref="AmbientFxCanvas"/> DustField behind the tree. The animated
    ///     gradient on SkillTreeOuterBorder stays as it is: that is background PAINT (a two-stop
    ///     drift on a brush), not a focal loop, and it was already tier-gated in PR-0.
    ///   • micro - owned nodes breathe their existing green glow, and node hover keeps the pop it
    ///     always had, now routed through the motion gate so Off means off.
    ///
    /// House rules this file obeys:
    ///   * every ambient clock asks <see cref="EnhancementsAmbientAllowed"/> (window focus + tab
    ///     visibility + reduced-motion + the performance tier) BEFORE it starts, and parks at a
    ///     static resting state otherwise;
    ///   * the owned-node breath is ONE <see cref="AnimationClock"/> shared by every owned node's
    ///     DropShadowEffect (see <see cref="ApplyOwnedNodeBreath"/>) - N nodes cost one clock, and
    ///     pausing the tab is one Pause() call rather than N animation teardowns;
    ///   * element.BeginAnimation / ApplyAnimationClock only - Storyboard.SetTargetName silently
    ///     no-ops across the tab UserControl namescope;
    ///   * every callback is wrapped. Decoration may never be why the tree throws.
    /// </summary>
    public partial class MainWindow
    {
        // ---- tuning ----------------------------------------------------------------

        /// <summary>Dust alpha multiplier. Low: the tree art is the subject, this is the air.</summary>
        private const double SkillTreeFxIntensity = 0.55;

        private const double OwnedNodeGlowMinOpacity = 0.38;
        private const double OwnedNodeGlowMaxOpacity = 0.72;
        private const double OwnedNodeGlowSeconds = 3.8;

        /// <summary>The node hover pop that shipped. Kept at 1.25 on purpose - the node grows so
        /// its art is readable, which is a different job from the shared 1.02 card lift.</summary>
        private const double SkillNodeHoverScale = 1.25;
        private const int SkillNodeHoverInMs = 250;
        private const int SkillNodeHoverOutMs = 200;

        // ---- state -----------------------------------------------------------------

        private bool _enhancementsFxInitialized;

        /// <summary>Every owned node's glow effect, rebuilt on each DrawSkillTree.</summary>
        private readonly List<DropShadowEffect> _ownedNodeGlows = new();

        /// <summary>The single clock driving all of them.</summary>
        private AnimationClock? _ownedNodeGlowClock;

        /// <summary>
        /// Reduced-motion + tier: whether the tab may hold an ambient clock AT ALL. False here
        /// means tear down and go back to static art, not "park it".
        /// </summary>
        private static bool EnhancementsAmbientAllowed =>
            MotionFx.AllowAmbientLoops && PerformanceProfile.AllowGlow(PerformanceProfile.CurrentTier);

        /// <summary>Whether anyone can currently SEE the tree: window focused, not minimised,
        /// tab on screen. False here means park the clock, not drop it.</summary>
        private bool EnhancementsFxOnScreen =>
            IsActive
            && WindowState != WindowState.Minimized
            && EnhancementsTab?.IsVisible == true;

        // ============================== lifecycle ==============================

        /// <summary>
        /// Wires the tab's FX up once. Called from <see cref="RefreshEnhancementsUI"/> rather than
        /// from a shared startup hook: the Enhancements tab is only ever refreshed when it is
        /// about to be shown, so this costs nothing for a user who never opens it.
        /// </summary>
        private void EnsureEnhancementsFx()
        {
            if (_enhancementsFxInitialized) return;
            _enhancementsFxInitialized = true;
            try
            {
                var tab = EnhancementsTab;
                if (tab?.SkillTreeFx != null)
                {
                    tab.SkillTreeFx.StartLayers(new AmbientFxConfig
                    {
                        Layers = AmbientFxLayers.DustField,
                        Intensity = SkillTreeFxIntensity,
                    });
                    // ShowTab parks it on the way out and resumes it on the way in, for free.
                    RegisterTabFx("enhancements", tab.SkillTreeFx);
                }

                // The canvas parks itself on deactivate/minimise; the owned-node clock is a plain
                // WPF animation and needs to be told. These are this file's own hooks so the
                // chrome funnel (MainWindow.ChromeFx.cs) stays untouched.
                Activated += OnEnhancementsFxWindowStateish;
                Deactivated += OnEnhancementsFxWindowStateish;
                StateChanged += OnEnhancementsFxWindowStateish;
                if (tab != null) tab.IsVisibleChanged += OnEnhancementsTabVisibilityChanged;
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "EnsureEnhancementsFx failed"); }
        }

        private void OnEnhancementsFxWindowStateish(object? sender, EventArgs e) => ApplyEnhancementsFxLoops();

        private void OnEnhancementsTabVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) =>
            ApplyEnhancementsFxLoops();

        /// <summary>Re-evaluates every Enhancements loop against the current gate.</summary>
        private void ApplyEnhancementsFxLoops()
        {
            if (!_enhancementsFxInitialized) return;
            try { ApplyOwnedNodeBreath(); }
            catch (Exception ex) { App.Logger?.Debug("ApplyEnhancementsFxLoops: {E}", ex.Message); }
        }

        // ============================== owned-node breath ==============================

        /// <summary>Forgets the previous draw's glows. Called at the top of every DrawSkillTree.</summary>
        private void ResetOwnedNodeGlows()
        {
            try
            {
                StopOwnedNodeGlowClock();
                _ownedNodeGlows.Clear();
            }
            catch (Exception ex) { App.Logger?.Debug("ResetOwnedNodeGlows: {E}", ex.Message); }
        }

        /// <summary>Enrols one owned node's glow in the shared breath.</summary>
        private void RegisterOwnedNodeGlow(DropShadowEffect? effect)
        {
            if (effect == null || effect.IsFrozen) return;
            _ownedNodeGlows.Add(effect);
        }

        /// <summary>
        /// One clock, N glows. <see cref="AnimationTimeline.CreateClock"/> gives a single root
        /// clock that every effect then attaches to with
        /// <see cref="Animatable.ApplyAnimationClock(DependencyProperty, AnimationClock)"/>, so a
        /// tree with 28 owned nodes still ticks exactly one timeline (and they breathe in phase,
        /// which is the point - a tree of independently-phased glows is a shimmer, not a pulse).
        /// Under Off/Reduced, the Performance tier or a hidden tab, no clock exists at all and the
        /// nodes keep the static glow they shipped with.
        /// </summary>
        private void ApplyOwnedNodeBreath()
        {
            try
            {
                if (_ownedNodeGlows.Count == 0) { StopOwnedNodeGlowClock(); return; }

                // Off / Reduced / the Performance tier: no clock at all, and the nodes go back to
                // the flat glow they shipped with rather than freezing mid-breath.
                if (!EnhancementsAmbientAllowed) { StopOwnedNodeGlowClock(); return; }

                if (!EnhancementsFxOnScreen)
                {
                    // Nobody can see it - park rather than tear down, so coming back to the tab
                    // re-arms in one call and picks the breath up where it left off.
                    if (_ownedNodeGlowClock?.Controller != null) _ownedNodeGlowClock.Controller.Pause();
                    else StopOwnedNodeGlowClock();
                    return;
                }

                if (_ownedNodeGlowClock != null)
                {
                    _ownedNodeGlowClock.Controller?.Resume();
                    return;
                }

                var anim = new DoubleAnimation(OwnedNodeGlowMinOpacity, OwnedNodeGlowMaxOpacity,
                                               TimeSpan.FromSeconds(OwnedNodeGlowSeconds))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(anim, AmbientFrameRate);
                anim.Freeze();

                var clock = anim.CreateClock();
                foreach (var effect in _ownedNodeGlows)
                {
                    if (effect.IsFrozen) continue;
                    effect.ApplyAnimationClock(DropShadowEffect.OpacityProperty, clock);
                }
                _ownedNodeGlowClock = clock;
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyOwnedNodeBreath: {E}", ex.Message); }
        }

        private void StopOwnedNodeGlowClock()
        {
            if (_ownedNodeGlowClock == null) return;
            try
            {
                foreach (var effect in _ownedNodeGlows)
                {
                    if (effect.IsFrozen) continue;
                    effect.ApplyAnimationClock(DropShadowEffect.OpacityProperty, null);
                }
            }
            catch (Exception ex) { App.Logger?.Debug("StopOwnedNodeGlowClock: {E}", ex.Message); }
            _ownedNodeGlowClock = null;
        }

        // ============================== node hover ==============================

        /// <summary>
        /// Node hover: the 1.25 pop the tree shipped with, plus the z-order lift that keeps the
        /// grown node above its neighbours - now routed through the motion gate, so MotionLevel.Off
        /// snaps instead of animating. RenderTransform only: the node is on a Canvas, so nothing
        /// it does can move the layout of anything else.
        /// </summary>
        private void ApplySkillNodeHover(Border? node, bool on)
        {
            if (node == null) return;
            try
            {
                if (node.RenderTransform is not ScaleTransform scale || scale.IsFrozen) return;
                Canvas.SetZIndex(node, on ? 10 : 0);

                double to = on ? SkillNodeHoverScale : 1.0;
                if (!MotionFx.AllowTransitions)
                {
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    scale.ScaleX = scale.ScaleY = to;
                    return;
                }

                var anim = on
                    ? new DoubleAnimation(to, TimeSpan.FromMilliseconds(SkillNodeHoverInMs))
                    {
                        EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 },
                    }
                    : new DoubleAnimation(to, TimeSpan.FromMilliseconds(SkillNodeHoverOutMs))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
                    };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
            }
            catch (Exception ex) { App.Logger?.Debug("ApplySkillNodeHover: {E}", ex.Message); }
        }
    }
}
