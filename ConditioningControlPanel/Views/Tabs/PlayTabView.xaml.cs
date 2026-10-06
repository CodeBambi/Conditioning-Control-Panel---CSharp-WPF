using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Controls;

namespace ConditioningControlPanel.Views.Tabs
{
    /// <summary>
    /// The Play door (tab key <c>play</c>): a card wall over the game-shaped features that used to
    /// be scattered across the Lab tab, the Exclusives shelf and two orphaned windows.
    ///
    /// <para><b>This file is the host only.</b> It owns the page frame and the one ambient loop.
    /// It owns no card content, no launch, and no tier decision. Every card's markup goes in a
    /// <c>Slot*</c> Grid in PlayTabView.xaml, and every card's click shim goes in a partial file
    /// of its own — <c>Views\Tabs\PlayTabView.&lt;Area&gt;.cs</c>, holding nothing but
    /// <c>if (Window.GetWindow(this) is MainWindow mw) mw.&lt;ExistingHandler&gt;(sender, e);</c>
    /// passthroughs. Same convention as <see cref="AppSettingsTabView"/> (Phase 2) and
    /// <see cref="StudioTabView"/> (Phase 4), for the same reason: parallel agents must never
    /// share one file.</para>
    ///
    /// <para><b>Launch parity is the contract.</b> A shim means an entitled user's click runs the
    /// same handler object the old button ran — <c>BtnGazeMinigame_Click</c>,
    /// <c>ChkFocusGaze_Changed</c>, <c>BtnStartBureau_Click</c>, <c>BtnStartIntake_Click</c>, and
    /// <c>ShowTab</c> for everything that is a page rather than a window. Nothing here
    /// re-implements a launch, and nothing here decides a tier: the lockbands are decoration and
    /// <c>TierGate</c> does the refusing inside the handler.</para>
    ///
    /// <para><b>No ambient loop.</b> The Rabbit Hole hero carried this surface's one focal
    /// canvas until 2026-09-18, when the games moved to the CC Labs launcher; the wall has no
    /// registered canvas now, and <c>SwitchTabFx</c> simply finds nothing under "play".</para>
    /// </summary>
    public partial class PlayTabView : UserControl
    {
        public PlayTabView()
        {
            InitializeComponent();

            // Nothing composed here since the games left the wall (2026-09-18): the only
            // ambient canvas this view ever owned sat behind the Rabbit Hole hero.
        }

        /// <summary>Zone keys the Play section strip reaches (nav rework contract 2).</summary>
        public static readonly string[] ZoneKeys = { "games", "sessions", "eyes" };

        /// <summary>
        /// Brings a zone header to the top of the wall: "games" | "sessions" | "eyes". An unknown
        /// key does nothing. The header glows once for 2 s so the eye lands on it; the glow is
        /// skipped under reduced or no motion (MotionFx), the scroll is not.
        /// </summary>
        public void ScrollToZone(string zone)
        {
            var header = ZoneHeader(zone);
            if (header == null) return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    // Top-align, not just "into view": BringIntoView settles for the nearest edge.
                    if (WallScroll.Content is Visual content && header.IsDescendantOf(content))
                    {
                        var y = header.TransformToAncestor(content).Transform(new Point(0, 0)).Y;
                        WallScroll.ScrollToVerticalOffset(Math.Max(0, y - 8));
                    }
                    else header.BringIntoView();
                    GlowZoneHeader(header);
                }
                catch (Exception ex) { App.Logger?.Debug("Play ScrollToZone({Zone}): {E}", zone, ex.Message); }
            }), System.Windows.Threading.DispatcherPriority.Normal);
        }

        /// <summary>The header element a zone key names, or null.</summary>
        internal FrameworkElement? ZoneHeader(string? zone) => (zone ?? "").Trim().ToLowerInvariant() switch
        {
            "games" => ZoneGames,
            "sessions" => ZoneSessions,
            "eyes" => ZoneEyes,
            _ => null,
        };

        private static void GlowZoneHeader(FrameworkElement header)
        {
            if (!Services.MotionFx.AllowAmbientLoops) return;
            var glow = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Color.FromRgb(0xFF, 0x69, 0xB4),
                BlurRadius = 18,
                ShadowDepth = 0,
                Opacity = 0,
            };
            header.Effect = glow;
            var anim = new System.Windows.Media.Animation.DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(2) };
            anim.KeyFrames.Add(new System.Windows.Media.Animation.EasingDoubleKeyFrame(0.9,
                System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(250))));
            anim.KeyFrames.Add(new System.Windows.Media.Animation.EasingDoubleKeyFrame(0.9,
                System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1400))));
            anim.KeyFrames.Add(new System.Windows.Media.Animation.EasingDoubleKeyFrame(0,
                System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2))));
            anim.Completed += (_, _) => { if (ReferenceEquals(header.Effect, glow)) header.Effect = null; };
            glow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, anim);
        }
    }
}
