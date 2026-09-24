using System;
using System.Windows;
using System.Windows.Media;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.FirstShow;

// Strong, non-blocking guide marks. Escalation adds size and contrast, never rapid flashing.
internal sealed class FirstShowHighlight : FrameworkElement
{
    internal Rect Target { get; set; } = Rect.Empty;
    internal Rect Viewport { get; set; }
    internal double Waiting { get; set; }
    internal double Time { get; set; }
    internal FirstShowHighlight() { IsHitTestVisible = false; }

    protected override void OnRender(DrawingContext dc)
    {
        if (Target.IsEmpty) return;
        double strength = Math.Clamp(Waiting/10,0,1);
        bool moving = MotionFx.Level != MotionLevel.Off;
        double motion = MotionFx.Level == MotionLevel.Reduced ? .4 : 1;
        var color = strength > .55 ? Colors.Gold : Colors.HotPink;
        var rect = Target; rect.Inflate(8,8);
        dc.DrawRoundedRectangle(null,new Pen(new SolidColorBrush(Color.FromArgb(40,color.R,color.G,color.B)),18+12*strength),rect,10,10);
        dc.DrawRoundedRectangle(null,new Pen(new SolidColorBrush(color),4+3*strength),rect,10,10);
        dc.DrawRoundedRectangle(null,new Pen(Brushes.White,1.5),rect,10,10);
        if (moving)
        {
            for (int i=0;i<2;i++)
            {
                double phase = (Time*.65*motion+i*.5)%1;
                var ring = rect; ring.Inflate(phase*(12+18*strength),phase*(12+18*strength));
                dc.DrawRoundedRectangle(null,new Pen(new SolidColorBrush(Color.FromArgb((byte)(120*(1-phase)),color.R,color.G,color.B)),2),ring,12,12);
            }
        }
        // Four thick corner brackets keep large targets legible too.
        double length = Math.Min(30,Math.Min(rect.Width,rect.Height)/2);
        var pen = new Pen(Brushes.White,3+strength*2);
        foreach (int x in new[] {-1,1}) foreach (int y in new[] {-1,1})
        {
            var p = new Point(x<0?rect.Left:rect.Right,y<0?rect.Top:rect.Bottom);
            dc.DrawLine(pen,p,new Point(p.X-x*length,p.Y));
            dc.DrawLine(pen,p,new Point(p.X,p.Y-y*length));
        }
        // A large inward pointer appears immediately and grows while the player waits.
        bool fromRight = rect.Right+85 < Viewport.Right;
        double direction = fromRight ? 1 : -1;
        double nudge = moving ? Math.Sin(Time*3)*5*motion : 0;
        var tip = new Point((fromRight?rect.Right:rect.Left)+direction*(14+nudge),rect.Top+Math.Min(30,rect.Height/2));
        double size = 18+strength*12;
        var arrow = new StreamGeometry();
        using (var ctx = arrow.Open())
        {
            ctx.BeginFigure(tip,true,true);
            ctx.LineTo(new Point(tip.X+direction*size,tip.Y-size*.7),true,false);
            ctx.LineTo(new Point(tip.X+direction*size,tip.Y+size*.7),true,false);
        }
        dc.DrawGeometry(new SolidColorBrush(color),new Pen(Brushes.White,2),arrow);
    }
}
