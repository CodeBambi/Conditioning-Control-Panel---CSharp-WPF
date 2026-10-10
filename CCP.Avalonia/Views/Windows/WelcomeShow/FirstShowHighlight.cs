using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Windows.WelcomeShow
{
    // PORTED from ConditioningControlPanel/Services/FirstShow/FirstShowHighlight.cs, numbers verbatim.
    // Strong, non-blocking guide marks. Escalation adds size and contrast, never rapid flashing.
    internal sealed class FirstShowHighlight : Control
    {
        internal Rect? Target { get; set; }
        internal Rect Viewport { get; set; }
        internal double Waiting { get; set; }
        internal double Time { get; set; }
        internal Func<MotionLevel> Motion = () => global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.Level;

        internal FirstShowHighlight() { IsHitTestVisible = false; }

        public override void Render(DrawingContext dc)
        {
            if (Target is not { } target || target.Width <= 0 || target.Height <= 0) return;
            double strength = Math.Clamp(Waiting / 10, 0, 1);
            var level = Motion();
            bool moving = level != MotionLevel.Off;
            double motion = level == MotionLevel.Reduced ? .4 : 1;
            var color = strength > .55 ? Colors.Gold : Colors.HotPink;
            var rect = target.Inflate(8);
            dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(40, color.R, color.G, color.B)), 18 + 12 * strength), rect, 10, 10);
            dc.DrawRectangle(null, new Pen(new SolidColorBrush(color), 4 + 3 * strength), rect, 10, 10);
            dc.DrawRectangle(null, new Pen(Brushes.White, 1.5), rect, 10, 10);
            if (moving)
            {
                for (int i = 0; i < 2; i++)
                {
                    double phase = (Time * .65 * motion + i * .5) % 1;
                    var ring = rect.Inflate(phase * (12 + 18 * strength));
                    dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb((byte)(120 * (1 - phase)), color.R, color.G, color.B)), 2), ring, 12, 12);
                }
            }
            // Four thick corner brackets keep large targets legible too.
            double length = Math.Min(30, Math.Min(rect.Width, rect.Height) / 2);
            var pen = new Pen(Brushes.White, 3 + strength * 2);
            foreach (int x in new[] { -1, 1 }) foreach (int y in new[] { -1, 1 })
            {
                var p = new Point(x < 0 ? rect.Left : rect.Right, y < 0 ? rect.Top : rect.Bottom);
                dc.DrawLine(pen, p, new Point(p.X - x * length, p.Y));
                dc.DrawLine(pen, p, new Point(p.X, p.Y - y * length));
            }
            // A large inward pointer appears immediately and grows while the player waits.
            bool fromRight = rect.Right + 85 < Viewport.Right;
            double direction = fromRight ? 1 : -1;
            double nudge = moving ? Math.Sin(Time * 3) * 5 * motion : 0;
            var tip = new Point((fromRight ? rect.Right : rect.Left) + direction * (14 + nudge), rect.Top + Math.Min(30, rect.Height / 2));
            double size = 18 + strength * 12;
            var arrow = new StreamGeometry();
            using (var ctx = arrow.Open())
            {
                ctx.BeginFigure(tip, true);
                ctx.LineTo(new Point(tip.X + direction * size, tip.Y - size * .7));
                ctx.LineTo(new Point(tip.X + direction * size, tip.Y + size * .7));
                ctx.EndFigure(true);
            }
            dc.DrawGeometry(new SolidColorBrush(color), new Pen(Brushes.White, 2), arrow);
        }
    }
}
