// PORTED from WPF 7.1.5 Views/Tabs/QuestsTabView.xaml.cs:95-225 (depth wave 10) and the depth layer of
// QuestsTabView.xaml. One lamp top-left, three heights: the counter and the XP chip are RAISED coins,
// Reroll and Fix day are RAISED planks that press, the three daily seats and the weekly card FLOAT over
// a SUNKEN well and DROP into it when done (ON IS PRESSED IN). No Effect anywhere: bevels are 1 px
// gradient borders, shadows are gradient bands under the element. Shadows are pulled toward the section
// hue (DepthRules.ShadowColor through DepthPaint.ShadowBand): the shell calls PaintDepthQuests(hue) from
// PaintSectionWash, and the constructor paints the You hue so the tab is right before that. Every number
// is DepthRules'; no shade is hand-picked.
// not ported from the WPF depth layer: the weekly progress groove and its tube gloss / bead (:402-429)
// and the mint stamp plate over a finished weekly (:318-340).
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls.Depth;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Depth;
using ConditioningControlPanel.Nav;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class QuestsTabView
    {
        /// <summary>The hue the bands wear right now, for the tests.</summary>
        internal uint DepthHue { get; private set; }

        private void DepthInit()
        {
            try
            {
                PaintDepthQuests(NavStripRules.Accent(NavSections.You));

                WirePlank(BtnRerollWeekly, WeeklyRerollFace, WeeklyRerollDrop, WeeklyRerollBevel);
                WirePlank(BtnFixStreak, FixStreakFace, FixStreakDrop, FixStreakBevel);

                WatchVisibility(WeeklyCompletedOverlay, ApplyWeeklyDone);
                var seats = new Control[] { DailySeat0, DailySeat1, DailySeat2 };
                var bands = new Control[] { DailySeatBand0, DailySeatBand1, DailySeatBand2 };
                for (int i = 0; i < seats.Length; i++)
                {
                    if (_dailyCards[i].FindControl<Border>("CompletedOverlay") is not { } done) continue;
                    Control seat = seats[i], band = bands[i];
                    WatchVisibility(done, () => ApplySeatDone(seat, band, done));
                    ApplySeatDone(seat, band, done);
                }
                ApplyWeeklyDone();
            }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[Quests] depth init"); }
        }

        /// <summary>Paints this tab's drop and float shadows with the section hue (DepthRules.ShadowColor),
        /// so the shadows blend with the page wash.</summary>
        internal void PaintDepthQuests(uint hue)
        {
            DepthHue = hue;
            var drop = DepthPaint.ShadowBand(hue);
            var floatBand = DepthPaint.ShadowBand(hue, DepthRules.FloatAlpha);
            foreach (var b in new[] { DailyCounterDrop, WeeklyXpDrop, WeeklyRerollDrop, FixStreakDrop }) b.Background = drop;
            foreach (var b in new[] { DailySeatBand0, DailySeatBand1, DailySeatBand2, WeeklyFloatBand }) b.Background = floatBand;
        }

        private static void WatchVisibility(Control element, Action changed) =>
            element.PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) changed(); };

        /// <summary>The weekly is done: the card drops into its seat and loses its float band.</summary>
        internal void ApplyWeeklyDone()
        {
            bool done = WeeklyCompletedOverlay.IsVisible;
            WeeklyFloatBand.Opacity = done ? 0 : 1;   // WPF Visibility.Hidden: gone, its room kept
            WeeklySeat.RenderTransform = new TranslateTransform(0, done ? DepthRules.ActiveSinkPx : 0);
            if (DepthPaint.Brush(done ? "DepthPressedBevel" : "DepthFloatRim") is { } rim) WeeklyQuestCard.BorderBrush = rim;
        }

        /// <summary>A finished daily seat drops the same way. The card stamps itself.</summary>
        private static void ApplySeatDone(Control seat, Control band, Control completedOverlay)
        {
            bool done = completedOverlay.IsVisible;
            band.Opacity = done ? 0 : 1;
            seat.RenderTransform = new TranslateTransform(0, done ? DepthRules.ActiveSinkPx : 0);
        }

        /// <summary>
        /// A raised plank that presses: the face travels DepthRules.TravelFor (hover lifts, press drops
        /// PressTravelPx in PressMs, release springs back in ReleaseMs with the 1 px overshoot), the drop
        /// band follows ShadowFor, the bevel swaps to the pressed pair while it is down. Motion Off sets
        /// the values with no clock.
        /// </summary>
        private void WirePlank(Button button, Control face, Control drop, Border bevel)
        {
            var travel = new TranslateTransform();
            face.RenderTransform = travel;
            bool wasPressed = false;
            FxTrack.Run? run = null;

            void Update()
            {
                bool enabled = button.IsEffectivelyEnabled, pressed = button.IsPressed, hovered = button.IsPointerOver;
                double to = DepthRules.TravelFor(enabled, pressed, active: false, hovered);
                double shadow = DepthRules.ShadowFor(enabled, pressed, active: false, hovered);

                drop.Margin = new Thickness(1, shadow, 1, -shadow);
                drop.Opacity = shadow > 0 ? 1 : 0;
                if (DepthPaint.Brush(pressed ? "DepthPressedBevel" : "DepthRaisedBevel") is { } rim) bevel.BorderBrush = rim;
                if (DepthPaint.Brush(pressed ? "DepthPressedShade" : "DepthRaisedSheen") is { } sheen) bevel.Background = sheen;
                bevel.Opacity = enabled ? 1 : 0.5;

                bool releasing = wasPressed && !pressed;
                int ms = DepthRules.Ms(pressed ? DepthRules.PressMs : releasing ? DepthRules.ReleaseMs : DepthRules.HoverMs, Env.Level);
                wasPressed = pressed;
                var old = run;
                run = null;
                double from = travel.Y;
                old?.Finish();
                if (ms <= 0 || Math.Abs(to - from) < 0.01) { travel.Y = to; return; }
                travel.Y = from;
                run = releasing
                    // back past rest by the overshoot at 60 %, then home
                    ? FxTrack.Play(ms, at => travel.Y = FxTrack.Keys(at / ms, (0, from, null),
                        (0.6, to - DepthRules.ReleaseOvershootPx, FxTrack.QuadOut), (1, to, FxTrack.QuadInOut)), () => travel.Y = to)
                    : FxTrack.Play(ms, at => travel.Y = from + ((to - from) * FxTrack.QuadOut(at / ms)), () => travel.Y = to);
            }

            button.PropertyChanged += (_, e) =>
            {
                if (e.Property == Button.IsPressedProperty || e.Property == IsPointerOverProperty || e.Property == IsEffectivelyEnabledProperty) Update();
            };
            Update();
        }

        /// <summary>The plank faces' travel, for the tests.</summary>
        internal double RerollFaceY => (WeeklyRerollFace.RenderTransform as TranslateTransform)?.Y ?? double.NaN;
    }
}
