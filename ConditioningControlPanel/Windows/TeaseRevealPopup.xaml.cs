using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ConditioningControlPanel.Features;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The teaser card behind a nameless tease tile (see <c>FeatureCard.TeaseTier</c> and the
    /// switch block at the top of <c>MainWindow.TeaseCard.cs</c>).
    ///
    /// <para>It exists to say ONE thing - something is coming, and it is a Tier N thing - and it
    /// must never say more than that. There is no feature name, no glyph that identifies the
    /// feature, and no copy in this file: the three strings come from the language files
    /// (<c>tease_popup_title</c> / <c>_body</c> / <c>_dismiss</c>) so the tease reads in nine
    /// languages the day it ships, unlike the one-shot intro cards which are hardcoded English.</para>
    ///
    /// <para>Unlike <see cref="FeatureIntroPopup"/> this is NOT once-per-install: it is the tile's
    /// click destination, so it opens every time. It therefore keeps no seen-flag, spends no
    /// pacing budget, and is deliberately small - one glyph, one line, one button.</para>
    /// </summary>
    public partial class TeaseRevealPopup : Window
    {
        /// <summary>One sweep, then the bar sits off-frame until the next one. The dwell is
        /// what makes it read as a glint rather than a barber pole.</summary>
        private static readonly TimeSpan ShimmerSweep = TimeSpan.FromSeconds(1.6);
        private static readonly TimeSpan ShimmerCycle = TimeSpan.FromSeconds(4.4);

        /// <summary>Travel bounds for the sheen bar, in the card's own coordinates: fully off the
        /// left edge to fully off the right. The window is a fixed 430 wide, so these are literals
        /// rather than a measure pass - a shimmer that has to wait for layout is a shimmer that
        /// stutters on its first cycle.</summary>
        private const double ShimmerFromX = -180;
        private const double ShimmerToX = 470;

        /// <summary>Internal rather than private so the render suite can build the card without
        /// showing it - a chromeless AllowsTransparency window is exactly the kind of thing that
        /// compiles and then dies on its first BAML load.</summary>
        internal TeaseRevealPopup(int teaseTier)
        {
            InitializeComponent();
            ApplyLivery(teaseTier);
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
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;

                var popup = new TeaseRevealPopup(teaseTier);
                if (owner is { IsLoaded: true }) popup.Owner = owner;
                popup.ShowDialog();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Tease teaser popup failed to open");
            }
        }

        /// <summary>Rim, glow, glyph and button in the tease's metal - the one thing the card is
        /// allowed to disclose is the price band.</summary>
        private void ApplyLivery(int teaseTier)
        {
            try
            {
                var livery = TierLivery.BorderBrush(teaseTier);
                var accent = TierLivery.AccentColor(teaseTier);

                CardBorder.BorderBrush = livery;
                CardShadow.Color = accent;
                TxtGlyph.Foreground = livery;
                TxtTitle.Foreground = livery;
                BtnDismiss.Background = livery;
            }
            catch (Exception ex)
            {
                // The pink defaults from XAML stand; the card is still readable.
                App.Logger?.Debug("TeaseRevealPopup.ApplyLivery: {E}", ex.Message);
            }
        }

        /// <summary>
        /// Arms the sheen sweep, or leaves it collapsed. Same gate every ambient loop in this app
        /// answers to: the performance tier has to allow glow AND the motion setting has to allow
        /// ambient loops. When either says no the card keeps a STATIC accent (the livery rim and
        /// its drop shadow), which is the resting look - never "the same thing, slower".
        /// </summary>
        private void StartShimmer()
        {
            try
            {
                if (!PerformanceProfile.AllowGlow(PerformanceProfile.CurrentTier)
                    || !MotionFx.AllowAmbientLoops)
                {
                    ShimmerHost.Visibility = Visibility.Collapsed;
                    return;
                }

                ShimmerHost.Visibility = Visibility.Visible;

                var slide = new DoubleAnimationUsingKeyFrames
                {
                    Duration = new Duration(ShimmerCycle),
                    RepeatBehavior = RepeatBehavior.Forever,
                };
                slide.KeyFrames.Add(new LinearDoubleKeyFrame(ShimmerFromX, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                slide.KeyFrames.Add(new EasingDoubleKeyFrame(ShimmerToX, KeyTime.FromTimeSpan(ShimmerSweep))
                {
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                });
                // Hold off-frame for the rest of the cycle: the dwell IS the effect.
                slide.KeyFrames.Add(new DiscreteDoubleKeyFrame(ShimmerToX, KeyTime.FromTimeSpan(ShimmerCycle)));

                Timeline.SetDesiredFrameRate(slide, 30);
                ShimmerSlide.BeginAnimation(TranslateTransform.XProperty, slide);
            }
            catch (Exception ex)
            {
                try { ShimmerHost.Visibility = Visibility.Collapsed; } catch { }
                App.Logger?.Debug("TeaseRevealPopup.StartShimmer: {E}", ex.Message);
            }
        }

        private void BtnDismiss_Click(object sender, RoutedEventArgs e)
        {
            try { Close(); } catch { }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            try { Close(); } catch { }
        }

        /// <summary>Chromeless window, so dragging the card is the only way to move it.</summary>
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try { DragMove(); } catch { }
        }
    }
}
