using System;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using ConditioningControlPanel.Avalonia.Controls;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// The teaser card behind a nameless tease tile (see <c>FeatureCard.TeaseTier</c>).
    ///
    /// <para>It exists to say ONE thing - something is coming, and it is a Tier N thing - and it
    /// must never say more than that. The three strings come from the language files
    /// (<c>tease_popup_title</c> / <c>_body</c> / <c>_dismiss</c>) so the tease reads in nine
    /// languages the day it ships.</para>
    ///
    /// PORTED from ConditioningControlPanel/Windows/TeaseRevealPopup.xaml.cs. Deviations:
    ///  - <c>TierLivery</c> lives in the WPF head's Features namespace and may not be referenced
    ///    from here, so <see cref="LiveryBrush"/> / <see cref="AccentColor"/> below resolve the
    ///    SAME Tier1Gold/Tier2Diamond keys from Theme/Brushes.xaml with the same flat fallbacks.
    ///  - The WPF keyframe timeline becomes one Avalonia <see cref="Animation"/> run against the
    ///    sheen bar's TranslateTransform. Avalonia has no DesiredFrameRate knob; it is dropped.
    ///  - <c>DragMove()</c> -> <c>BeginMoveDrag(e)</c>; <c>App.Logger</c> -> Serilog's static Log.
    /// </summary>
    public partial class TeaseRevealPopup : Window
    {
        /// <summary>One sweep, then the bar sits off-frame until the next one. The dwell is
        /// what makes it read as a glint rather than a barber pole.</summary>
        private static readonly TimeSpan ShimmerSweep = TimeSpan.FromSeconds(1.6);
        private static readonly TimeSpan ShimmerCycle = TimeSpan.FromSeconds(4.4);

        /// <summary>Travel bounds for the sheen bar, in the card's own coordinates: fully off the
        /// left edge to fully off the right. The window is a fixed 430 wide, so these are literals
        /// rather than a measure pass.</summary>
        private const double ShimmerFromX = -180;
        private const double ShimmerToX = 470;

        /// <summary>Gold #F0C24B - the Tier 1 base tone. Copied from Features/TierLivery.cs.</summary>
        private static readonly Color GoldAccent = Color.FromRgb(0xF0, 0xC2, 0x4B);

        /// <summary>Diamond #8FD4EF - the Tier 2 base tone.</summary>
        private static readonly Color DiamondAccent = Color.FromRgb(0x8F, 0xD4, 0xEF);

        private readonly Border _shimmerHost;
        private readonly Rectangle _shimmerBar;

        /// <summary>Render/design constructor: a gold tease, so --render-view can draw the card
        /// without an owner. Internal, so no production caller can ship the sample.</summary>
        internal TeaseRevealPopup() : this(1) { }

        internal TeaseRevealPopup(int teaseTier)
        {
            AvaloniaXamlLoader.Load(this);

            _shimmerHost = this.FindControl<Border>("ShimmerHost")!;
            _shimmerBar = this.FindControl<Rectangle>("ShimmerBar")!;

            ApplyLivery(teaseTier);

            // Handlers live here rather than in markup, per the porting convention.
            this.FindControl<Button>("BtnDismiss")!.Click += (_, _) => { try { Close(); } catch { } };

            KeyDown += (_, e) =>
            {
                if (e.Key != Key.Escape) return;
                e.Handled = true;
                try { Close(); } catch { }
            };

            // Chromeless window, so dragging the card is the only way to move it.
            PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
                try { BeginMoveDrag(e); } catch { }
            };

            Loaded += (_, _) => StartShimmer();
        }

        /// <summary>
        /// Opens the teaser for <paramref name="teaseTier"/> (1 = gold, 2 = diamond). Guarded end
        /// to end: a teaser failing to open must never be the reason a dashboard click looks
        /// broken, so the worst case is a logged warning and nothing on screen.
        /// </summary>
        internal static void ShowFor(int teaseTier, Window? owner)
        {
            try
            {
                var popup = new TeaseRevealPopup(teaseTier);
                // IsVisible, not IsLoaded: Avalonia's ShowDialog throws on an owner that is not
                // visible, and a shell minimised to tray is loaded-and-not-visible. With IsLoaded
                // the throw lands in the catch below and the teaser silently never opens; with
                // IsVisible it falls through to the modeless Show(), which is the intended
                // worst case.
                if (owner is { IsVisible: true }) popup.ShowDialog(owner);
                else popup.Show();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Tease teaser popup failed to open");
            }
        }

        /// <summary>The persistent border gradient for <paramref name="tier"/>. Tier 2 and up wear
        /// diamond; everything else wears gold. A missing brush degrades to the flat accent, never
        /// to a throw inside card construction.</summary>
        private IBrush LiveryBrush(int tier)
        {
            var key = tier >= 2 ? "Tier2DiamondBorderBrush" : "Tier1GoldBorderBrush";
            if (this.TryFindResource(key, out var found) && found is IBrush brush) return brush;
            return new SolidColorBrush(AccentColor(tier));
        }

        /// <summary>The single flat tone behind the livery, for the drop shadow.</summary>
        private static Color AccentColor(int tier) => tier >= 2 ? DiamondAccent : GoldAccent;

        /// <summary>Rim, glow, glyph and button in the tease's metal - the one thing the card is
        /// allowed to disclose is the price band.</summary>
        private void ApplyLivery(int teaseTier)
        {
            try
            {
                var livery = LiveryBrush(teaseTier);
                var accent = AccentColor(teaseTier);

                var card = this.FindControl<Border>("CardBorder")!;
                card.BorderBrush = livery;
                if (card.Effect is DropShadowEffect shadow) shadow.Color = accent;

                this.FindControl<TextBlock>("TxtGlyph")!.Foreground = livery;
                this.FindControl<TextBlock>("TxtTitle")!.Foreground = livery;
                this.FindControl<Button>("BtnDismiss")!.Background = livery;
            }
            catch (Exception ex)
            {
                // The pink defaults from XAML stand; the card is still readable.
                Log.Debug("TeaseRevealPopup.ApplyLivery: {E}", ex.Message);
            }
        }

        /// <summary>
        /// Arms the sheen sweep, gated exactly as WPF gates it: the performance tier has to allow
        /// glow AND the motion setting has to allow ambient loops. When either says no the card
        /// keeps a STATIC accent (the livery rim and its drop shadow), which is the resting look -
        /// never "the same thing, slower".
        ///
        /// <para>WPF asks two services; this head asks <see cref="AmbientFxCanvas.Env"/>, which
        /// carries both verbatim over the same <c>CoreSettings</c> fields. The conjunction collapses:
        /// <c>AllowGlow(tier)</c> is <c>tier != Performance</c>, which is <c>Env.AllowAmbientMotion</c>,
        /// and <c>Env.AllowAmbientLoops</c> is <c>Level == Full &amp;&amp; AllowAmbientMotion(tier)</c> -
        /// so <c>AllowAmbientLoops</c> alone IS the WPF pair, not a weakened stand-in.</para>
        /// </summary>
        private void StartShimmer()
        {
            try
            {
                if (!AmbientFxCanvas.Env.AllowAmbientLoops)
                {
                    _shimmerHost.IsVisible = false;
                    return;
                }

                if (_shimmerBar.RenderTransform is not TransformGroup group) return;
                var slide = group.Children.OfType<TranslateTransform>().FirstOrDefault();
                if (slide is null) return;

                _shimmerHost.IsVisible = true;

                var sweep = ShimmerSweep.TotalSeconds / ShimmerCycle.TotalSeconds;
                var anim = new Animation
                {
                    Duration = ShimmerCycle,
                    IterationCount = IterationCount.Infinite,
                    Easing = new SineEaseInOut(),
                    Children =
                    {
                        Frame(0.0, ShimmerFromX),
                        Frame(sweep, ShimmerToX),
                        // Hold off-frame for the rest of the cycle: the dwell IS the effect.
                        Frame(1.0, ShimmerToX),
                    },
                };
                _ = anim.RunAsync(slide);
            }
            catch (Exception ex)
            {
                try { _shimmerHost.IsVisible = false; } catch { }
                Log.Debug("TeaseRevealPopup.StartShimmer: {E}", ex.Message);
            }
        }

        private static KeyFrame Frame(double cue, double x) => new()
        {
            Cue = new Cue(cue),
            Setters = { new Setter(TranslateTransform.XProperty, x) },
        };
    }
}
