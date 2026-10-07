using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.UI;
using ConditioningControlPanel.Features;

namespace ConditioningControlPanel
{
    // ============================================================================================
    // THE PREMIUM PAGE'S FLAIR (polish 12 round 2, owner desk pass 2026-10-07: "add more particles
    // and flair to the Premium section ... there aren't enough particles").
    //
    //   - the room's canvas runs VaultMotes: gold glitter off the Basic cards and signs, cyan
    //     diamonds off the Prime ones, loose sparkles anywhere (AmbientFxCanvas.Vault.cs);
    //   - a shimmer crosses each group's tier sign;
    //   - every card that is yours wears a breathing rim glow in its tier colour and an occasional
    //     glint (VaultCardAura); locked cards keep the slow faint glass sweep they already had;
    //   - a click throws a burst in the card's colour; the cards rise in, staggered, as the page opens.
    //
    // Motion: Full = all of it; Reduced = a few slow motes, a slow shallow breath, a slow sign
    // shimmer, a short fade-in, no glints and no burst; Off = static (rims lit, no clocks). Every
    // clock parks with the page (StopVaultFlair from StopVaultMotion). Depth law: glows are pens and
    // gradient shapes, never an Effect. One shared canvas, motes capped (VaultMoteMath).
    // ============================================================================================
    public partial class MainWindow
    {
        /// <summary>A group sign's shimmer band, kept so the page can start and park it.</summary>
        private readonly List<TranslateTransform> _vaultSignSheens = new();

        /// <summary>The group signs, with the colour and shape their motes take.</summary>
        private readonly List<(FrameworkElement Sign, Color Hue, bool Diamond)> _vaultSigns = new();

        private bool _vaultZonesQueued;
        private bool _vaultZonesHooked;

        /// <summary>Entrance stagger step and cap (Full).</summary>
        internal const int VaultEntranceStepMs = 38;
        internal const int VaultEntranceCap = 16;

        /// <summary>A shelf group's colour: Basic gold, Prime cyan, the rest lilac.</summary>
        private static Color VaultGroupHue(PremiumGroup g) => g switch
        {
            PremiumGroup.Basic => VaultBasicGold,
            PremiumGroup.Prime => VaultPrimeCyan,
            _ => VaultFreeLilac,
        };

        private static PremiumGroup VaultGroupOf(ExclusiveFeature f) => PremiumShelfOrder.GroupOf(f.Key, f.Tier);

        /// <summary>The card's own transforms, made once: HoverLift finds the scale, the entrance the
        /// translate, and neither ever replaces the other.</summary>
        private static void GiveVaultCardTransforms(FrameworkElement card)
        {
            card.RenderTransformOrigin = new Point(0.5, 0.5);
            card.RenderTransform = new TransformGroup { Children = { new ScaleTransform(1, 1), new TranslateTransform() } };
        }

        /// <summary>The tier sign of a group header, with a shimmer band clipped to the sign's own
        /// pixels (an opacity mask of the same art), so the light crosses the neon and nothing else.</summary>
        private FrameworkElement VaultSignWithSheen(Image sign, Color hue, bool diamond)
        {
            var host = new Grid { Margin = sign.Margin, VerticalAlignment = VerticalAlignment.Center };
            sign.Margin = new Thickness(0);
            host.Children.Add(sign);

            var slide = new TranslateTransform(-1.2, 0);
            var band = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0.35),
                RelativeTransform = slide,
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.30),
                    new GradientStop(Color.FromArgb(0xD8, 255, 255, 255), 0.47),
                    new GradientStop(Color.FromArgb(0xF0, 255, 255, 255), 0.50),
                    new GradientStop(Color.FromArgb(0xD8, 255, 255, 255), 0.53),
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.70),
                },
            };
            var sheen = new System.Windows.Shapes.Rectangle
            {
                Fill = band,
                IsHitTestVisible = false,
                OpacityMask = new ImageBrush(sign.Source) { Stretch = Stretch.Uniform },
            };
            host.Children.Add(sheen);
            _vaultSignSheens.Add(slide);
            _vaultSigns.Add((sign, hue, diamond));
            return host;
        }

        /// <summary>Starts the flair on the Premium page (the view in hand). No-op on Account &amp; Plans.</summary>
        private void StartVaultFlair()
        {
            if (!VaultBuilt || VaultView.PlansMode) return;
            try
            {
                var level = MotionFx.Level;
                StartVaultSignSheens(level);
                AttachVaultAuras(level, retries: 5);
                HookVaultZones();
                QueueVaultZones();
            }
            catch (Exception ex) { App.Logger?.Debug("StartVaultFlair: {E}", ex.Message); }
        }

        /// <summary>Parks every flair clock (the page is hidden or the panel went to the tray).</summary>
        private void StopVaultFlair()
        {
            try
            {
                foreach (var s in _vaultSignSheens) s.BeginAnimation(TranslateTransform.XProperty, null);
                foreach (var ui in _exclusiveCards) ui.Aura?.Stop();
            }
            catch (Exception ex) { App.Logger?.Debug("StopVaultFlair: {E}", ex.Message); }
        }

        private void StartVaultSignSheens(MotionLevel level)
        {
            foreach (var s in _vaultSignSheens)
            {
                s.BeginAnimation(TranslateTransform.XProperty, null);
                s.X = -1.2;
                if (level == MotionLevel.Off) continue;
                double cross = level == MotionLevel.Full ? 1.1 : 2.2;
                double period = level == MotionLevel.Full ? 4.2 : 9.0;
                var sweep = new DoubleAnimationUsingKeyFrames
                {
                    Duration = TimeSpan.FromSeconds(period),
                    RepeatBehavior = RepeatBehavior.Forever,
                    BeginTime = TimeSpan.FromSeconds(0.5 + 0.7 * _vaultSignSheens.IndexOf(s)),
                };
                sweep.KeyFrames.Add(new DiscreteDoubleKeyFrame(-1.2, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                sweep.KeyFrames.Add(new EasingDoubleKeyFrame(1.2, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(cross)),
                    new SineEase { EasingMode = EasingMode.EaseInOut }));
                Timeline.SetDesiredFrameRate(sweep, 30);
                s.BeginAnimation(TranslateTransform.XProperty, sweep);
            }
        }

        /// <summary>Rim glow + glint on the cards that are yours; none on the rest. Adorner layers
        /// exist only after the shelf has rendered once, so this retries at Background priority (the
        /// same rule the sheens follow: never Normal, which starves the first render).</summary>
        private void AttachVaultAuras(MotionLevel level, int retries)
        {
            bool missing = false;
            int seed = 0;
            foreach (var ui in _exclusiveCards)
            {
                seed++;
                bool mine = ui.Feature.Shown() && IsVaultDoorOpen(ui.Feature);
                var layer = AdornerLayer.GetAdornerLayer(ui.Card);
                if (!mine)
                {
                    if (ui.Aura != null)
                    {
                        ui.Aura.Stop();
                        try { layer?.Remove(ui.Aura); } catch { }
                        ui.Aura = null;
                    }
                    continue;
                }
                if (layer == null) { missing = true; continue; }
                var hue = VaultGroupHue(VaultGroupOf(ui.Feature));
                if (ui.Aura == null || ui.Aura.Hue != hue)
                {
                    if (ui.Aura != null) { ui.Aura.Stop(); try { layer.Remove(ui.Aura); } catch { } }
                    ui.Aura = new VaultCardAura(ui.Card, hue, 12);
                }
                var on = layer.GetAdorners(ui.Card);
                if (on == null || Array.IndexOf(on, ui.Aura) < 0) layer.Add(ui.Aura);
                ui.Aura.Start(level, seed * 7 + ui.Feature.Key.Length);
            }

            if (!missing || retries <= 0) return;
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (_premiumView?.IsVisible == true)
                        PaintVault(_premiumView, () => AttachVaultAuras(MotionFx.Level, retries - 1));
                }
                catch (Exception ex) { App.Logger?.Debug("Vault aura retry: {E}", ex.Message); }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>The cards rise into place one after another as the page opens.</summary>
        private void PlayVaultEntrance()
        {
            if (!VaultBuilt || VaultView.PlansMode) return;
            var level = MotionFx.Level;
            int i = 0;
            foreach (var child in VaultView.ExclusivesShelf.Children.OfType<FrameworkElement>())
            {
                if (child.Visibility != Visibility.Visible) continue;
                var slide = (child.RenderTransform as TransformGroup)?.Children.OfType<TranslateTransform>().FirstOrDefault()
                            ?? child.RenderTransform as TranslateTransform;
                child.BeginAnimation(UIElement.OpacityProperty, null);
                slide?.BeginAnimation(TranslateTransform.YProperty, null);
                if (slide != null) slide.Y = 0;
                child.Opacity = 1;
                if (level == MotionLevel.Off) continue;

                bool full = level == MotionLevel.Full;
                var delay = TimeSpan.FromMilliseconds((full ? VaultEntranceStepMs : 20) * Math.Min(i, full ? VaultEntranceCap : 8));
                // Hidden until its turn, then in: one clock from t=0, so a card never shows
                // early (a plain BeginTime would leave it at full opacity while it waits).
                var fade = new DoubleAnimationUsingKeyFrames();
                fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(delay)));
                fade.KeyFrames.Add(new EasingDoubleKeyFrame(1,
                    KeyTime.FromTimeSpan(delay + TimeSpan.FromMilliseconds(full ? 320 : 200)),
                    new QuadraticEase { EasingMode = EasingMode.EaseOut }));
                child.Opacity = 1;
                child.BeginAnimation(UIElement.OpacityProperty, fade);
                if (full && slide != null)
                {
                    var rise = new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(420))
                    {
                        BeginTime = delay,
                        EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 },
                    };
                    slide.Y = 18;
                    slide.BeginAnimation(TranslateTransform.YProperty, rise);
                }
                i++;
            }
        }

        /// <summary>A click on a card: a burst in its colour, a press, then the door. The door waits
        /// 140 ms only when there is a burst to see (Full motion); otherwise it opens at once.</summary>
        private void OnVaultCardClicked(Border card, ExclusiveFeature feature)
        {
            bool burst = false;
            try
            {
                var view = _premiumView;
                if (view != null && view.IsVisible && MotionFx.AllowParticles && card.ActualWidth > 0)
                {
                    var fx = view.ExclusivesAmbientFx;
                    var at = card.TranslatePoint(new Point(card.ActualWidth / 2, card.ActualHeight / 2), fx);
                    fx.Burst(at.X, at.Y, VaultGroupHue(VaultGroupOf(feature)), 90);
                    MotionFx.PressSquish(card, true);
                    burst = true;
                }
            }
            catch (Exception ex) { App.Logger?.Debug("Vault card burst: {E}", ex.Message); }

            if (!burst) { OpenExclusiveFeature(feature); return; }
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                try { MotionFx.PressSquish(card, false); } catch { }
                OpenExclusiveFeature(feature);
            };
            t.Start();
        }

        // ---- the motes' zones ----

        private void HookVaultZones()
        {
            if (_vaultZonesHooked || _premiumView == null) return;
            _vaultZonesHooked = true;
            _premiumView.ContentScroll.ScrollChanged += (_, _) => QueueVaultZones();
            _premiumView.ExclusivesShelf.SizeChanged += (_, _) => QueueVaultZones();
            _premiumView.ExclusivesAmbientFx.SizeChanged += (_, _) => QueueVaultZones();
        }

        /// <summary>Coalesced: a burst of scroll steps recomputes the zones once, after layout.</summary>
        private void QueueVaultZones()
        {
            if (_vaultZonesQueued) return;
            _vaultZonesQueued = true;
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
            {
                _vaultZonesQueued = false;
                try { UpdateVaultZones(); }
                catch (Exception ex) { App.Logger?.Debug("Vault zones: {E}", ex.Message); }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>Every card and sign on screen becomes a place motes rise from. A card that is
        /// yours counts twice, so the glitter gathers where the doors are open.</summary>
        internal void UpdateVaultZones()
        {
            var view = _premiumView;
            if (view == null || !view.IsVisible) return;
            var fx = view.ExclusivesAmbientFx;
            double h = fx.ActualHeight;
            if (fx.ActualWidth <= 0 || h <= 0) return;

            var zones = new List<VaultZone>();
            void Add(FrameworkElement e, Color hue, bool diamond, int weight)
            {
                if (!e.IsVisible || e.ActualWidth <= 0) return;
                var tl = e.TranslatePoint(new Point(0, 0), fx);
                var r = new Rect(tl, new Size(e.ActualWidth, e.ActualHeight));
                if (r.Bottom < 0 || r.Top > h) return;
                for (int k = 0; k < weight; k++) zones.Add(new VaultZone(r, hue, diamond));
            }

            foreach (var ui in _exclusiveCards)
            {
                if (!ui.Feature.Shown()) continue;
                var g = VaultGroupOf(ui.Feature);
                Add(ui.Card, VaultGroupHue(g), g == PremiumGroup.Prime, IsVaultDoorOpen(ui.Feature) ? 2 : 1);
            }
            foreach (var (sign, hue, diamond) in _vaultSigns) Add(sign, hue, diamond, 2);
            fx.SetVaultZones(zones);
        }
    }
}
