using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ConditioningControlPanel.Controls.Depth;
using ConditioningControlPanel.Controls.NavRail;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Views.Tabs
{
    public partial class QuestsTabView : UserControl
    {
        public QuestsTabView()
        {
            InitializeComponent();
            // FX lifecycle (PR-3a): the quest bars are filled from the tab's own show, because
            // RefreshQuestUI runs before the tracks have ever been measured.
            IsVisibleChanged += QuestsTabView_IsVisibleChanged;

            // The three daily seats each own a reroll button; the tab just forwards which seat was
            // pressed. MainWindow spends the reroll - no quest state is touched down here.
            DailyCard0.RerollRequested += OnDailyCardRerollRequested;
            DailyCard1.RerollRequested += OnDailyCardRerollRequested;
            DailyCard2.RerollRequested += OnDailyCardRerollRequested;

            InitDepth();
        }

        private void OnDailyCardRerollRequested(object? sender, EventArgs e)
        {
            if (sender is Controls.DailyQuestCard card && Window.GetWindow(this) is MainWindow mw)
                mw.RerollDailySlot(card.Slot);
        }

        private void QuestsTabView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.OnQuestsTabVisibilityChanged(IsVisible);
        }

        private void BtnFixStreak_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnFixStreak_Click(sender, e);
        }
        private void BtnQuestSubDaily_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnQuestSubDaily_Click(sender, e);
        }
        private void BtnQuestSubRoadmap_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnQuestSubRoadmap_Click(sender, e);
        }
        private void BtnRerollWeekly_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnRerollWeekly_Click(sender, e);
        }
        private void BtnTrack_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnTrack_Click(sender, e);
        }
        private void HorizontalScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.HorizontalScrollViewer_PreviewMouseWheel(sender, e);
        }
        private void StreakCalendarCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.StreakCalendarCanvas_SizeChanged(sender, e);
        }

        // ============================== Depth (nav polish wave 10) ==============================
        // One lamp, three heights. The seats and the weekly card FLOAT, the list and the bars are
        // SUNKEN, the counter badge, the reward chip and the two action buttons are RAISED and the
        // buttons press. A finished quest DROPS into its well (no float band, ActiveSinkPx down,
        // the pressed bevel) and the weekly takes the DONE stamp (static XAML art). Nothing here
        // moves at rest; Motion Off sets every value at once and looks the same as Full.

        /// <summary>The groove's inner top band. A 10 px groove cannot carry WellPx (9): this is
        /// the same well scaled to the groove (0.4 of its height).</summary>
        internal const double GrooveTopPx = 4.0;
        /// <summary>The tube bead's diameter: the groove height less one px of lip on each side.</summary>
        internal const double TubeBeadPx = 8.0;

        private void InitDepth()
        {
            // Quests lives under You, so its shadows lean Coral until the window paints the live
            // section hue (MainWindow.PaintSectionWash -> PaintDepthQuests).
            PaintDepthQuests(NavStripRules.Accent(NavSections.You));

            WirePlank(BtnRerollWeekly, WeeklyRerollFace, WeeklyRerollDrop, WeeklyRerollBevel);
            WirePlank(BtnFixStreak, FixStreakFace, FixStreakDrop, FixStreakBevel);

            WatchVisibility(WeeklyCompletedOverlay, ApplyWeeklyDone);
            WatchVisibility(DailyCard0.CompletedOverlay, () => ApplySeatDone(DailySeat0, DailySeatBand0, DailyCard0.CompletedOverlay));
            WatchVisibility(DailyCard1.CompletedOverlay, () => ApplySeatDone(DailySeat1, DailySeatBand1, DailyCard1.CompletedOverlay));
            WatchVisibility(DailyCard2.CompletedOverlay, () => ApplySeatDone(DailySeat2, DailySeatBand2, DailyCard2.CompletedOverlay));
            ApplyWeeklyDone();
        }

        /// <summary>
        /// Paints this tab's drop and float shadows with the section hue (DepthRules.ShadowColor),
        /// so the shadows blend with the page wash. The integrator wires it from
        /// MainWindow.PaintSectionWash; the constructor paints the You hue so the tab is right
        /// before that wiring exists.
        /// </summary>
        internal void PaintDepthQuests(Color hue)
        {
            var drop = ShadowBand(DepthRules.ShadowColor(hue));
            var floatBand = ShadowBand(DepthRules.ShadowColor(hue, DepthRules.FloatAlpha));
            foreach (var b in new[] { DailyCounterDrop, WeeklyXpDrop, WeeklyRerollDrop, FixStreakDrop })
                b.Background = drop;
            foreach (var b in new[] { DailySeatBand0, DailySeatBand1, DailySeatBand2, WeeklyFloatBand })
                b.Background = floatBand;
        }

        /// <summary>A shadow band: the colour at the contact edge, fading to nothing below.</summary>
        internal static LinearGradientBrush ShadowBand(Color contact)
        {
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            brush.GradientStops.Add(new GradientStop(contact, 0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, contact.R, contact.G, contact.B), 1));
            brush.Freeze();
            return brush;
        }

        private static void WatchVisibility(UIElement element, Action changed)
        {
            DependencyPropertyDescriptor.FromProperty(UIElement.VisibilityProperty, typeof(UIElement))
                ?.AddValueChanged(element, (_, _) => changed());
        }

        /// <summary>The weekly is done: the card drops into its seat and loses its float band.</summary>
        internal void ApplyWeeklyDone()
        {
            bool done = WeeklyCompletedOverlay.Visibility == Visibility.Visible;
            WeeklyFloatBand.Visibility = done ? Visibility.Hidden : Visibility.Visible;
            WeeklySeat.RenderTransform = new TranslateTransform(0, done ? DepthRules.ActiveSinkPx : 0);
            WeeklyQuestCard.BorderBrush = (Brush)FindResource(done ? "DepthPressedBevel" : "DepthFloatRim");
        }

        /// <summary>A finished daily seat drops the same way. The card stamps itself.</summary>
        private static void ApplySeatDone(FrameworkElement seat, UIElement band, UIElement completedOverlay)
        {
            bool done = completedOverlay.Visibility == Visibility.Visible;
            band.Visibility = done ? Visibility.Hidden : Visibility.Visible;
            seat.RenderTransform = new TranslateTransform(0, done ? DepthRules.ActiveSinkPx : 0);
        }

        /// <summary>The bead rides the leading edge only once the tube is wider than the bead.</summary>
        private void WeeklyProgressFill_SizeChanged(object sender, SizeChangedEventArgs e)
            => WeeklyTubeBead.Visibility = e.NewSize.Width >= TubeBeadPx + 2 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// A raised plank that presses: the face travels DepthRules.TravelFor (hover lifts, press
        /// drops PressTravelPx in PressMs, release springs back in ReleaseMs with the overshoot),
        /// the drop band follows ShadowFor, the bevel swaps to the pressed pair while it is down.
        /// </summary>
        private void WirePlank(ButtonBase button, FrameworkElement face, FrameworkElement drop, Border bevel)
        {
            var travel = new TranslateTransform();
            face.RenderTransform = travel;
            bool wasPressed = false;

            void Update()
            {
                bool enabled = button.IsEnabled, pressed = button.IsPressed, hovered = button.IsMouseOver;
                double to = DepthRules.TravelFor(enabled, pressed, active: false, hovered);
                double shadow = DepthRules.ShadowFor(enabled, pressed, active: false, hovered);

                drop.Margin = new Thickness(1, shadow, 1, -shadow);
                drop.Opacity = shadow > 0 ? 1 : 0;
                bevel.BorderBrush = (Brush)FindResource(pressed ? "DepthPressedBevel" : "DepthRaisedBevel");
                bevel.Background = (Brush)FindResource(pressed ? "DepthPressedShade" : "DepthRaisedSheen");
                bevel.Opacity = enabled ? 1 : 0.5;

                bool releasing = wasPressed && !pressed;
                int ms = DepthRules.Ms(pressed ? DepthRules.PressMs : releasing ? DepthRules.ReleaseMs : DepthRules.HoverMs,
                                       MotionFx.Level);
                wasPressed = pressed;
                if (ms <= 0)
                {
                    travel.BeginAnimation(TranslateTransform.YProperty, null);
                    travel.Y = to;
                    return;
                }
                if (releasing)
                {
                    var spring = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(ms) };
                    spring.KeyFrames.Add(new EasingDoubleKeyFrame(to - DepthRules.ReleaseOvershootPx,
                        KeyTime.FromPercent(0.6), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
                    spring.KeyFrames.Add(new EasingDoubleKeyFrame(to, KeyTime.FromPercent(1.0),
                        new QuadraticEase { EasingMode = EasingMode.EaseInOut }));
                    travel.BeginAnimation(TranslateTransform.YProperty, spring);
                    return;
                }
                travel.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                });
            }

            DependencyPropertyDescriptor.FromProperty(ButtonBase.IsPressedProperty, typeof(ButtonBase))
                ?.AddValueChanged(button, (_, _) => Update());
            button.MouseEnter += (_, _) => Update();
            button.MouseLeave += (_, _) => Update();
            button.IsEnabledChanged += (_, _) => Update();
            Update();
        }
    }
}
