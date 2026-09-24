using System;
using System.Linq;
using System.Windows;

namespace ConditioningControlPanel.Services.FirstShow;

// All rectangles use stage DIPs. A monitor work area, never the virtual desktop, is the safe boundary.
internal static class FirstShowLayout
{
    internal static Rect Fit(Rect rect, Rect bounds) => new(
        Math.Clamp(rect.X, bounds.Left, Math.Max(bounds.Left, bounds.Right - rect.Width)),
        Math.Clamp(rect.Y, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - rect.Height)),
        Math.Min(rect.Width, bounds.Width), Math.Min(rect.Height, bounds.Height));

    private static double Overlap(Rect a, Rect b)
    {
        a.Intersect(b);
        return a.IsEmpty ? 0 : a.Width * a.Height;
    }

    internal static Rect Speech(Rect bounds, Size size, Rect body, Rect target, Point preferred)
    {
        double w = Math.Min(size.Width, bounds.Width), h = Math.Min(size.Height, bounds.Height);
        var candidates = new[]
        {
            new Rect(preferred, new Size(w,h)),
            new Rect(body.Right+18, body.Top, w,h), new Rect(body.Left-w-18, body.Top, w,h),
            new Rect(body.Left+(body.Width-w)/2, body.Bottom+18, w,h),
            new Rect(body.Left+(body.Width-w)/2, body.Top-h-18, w,h),
            new Rect(bounds.Left,bounds.Top,w,h), new Rect(bounds.Right-w,bounds.Top,w,h),
            new Rect(bounds.Left,bounds.Bottom-h,w,h), new Rect(bounds.Right-w,bounds.Bottom-h,w,h)
        };
        var anchor = body;
        body.Inflate(10,10);
        return candidates.Select(x => Fit(x,bounds)).OrderBy(x =>
            Overlap(x,body)*1000 + Overlap(x,target)*1000 +
            Distance(x,anchor)*8 + (x.TopLeft-preferred).Length).First();
    }

    private static double Distance(Rect a, Rect b)
    {
        double dx = Math.Max(0,Math.Max(a.Left-b.Right,b.Left-a.Right));
        double dy = Math.Max(0,Math.Max(a.Top-b.Bottom,b.Top-a.Bottom));
        return Math.Sqrt(dx*dx+dy*dy);
    }

    internal static Rect GuideBody(Rect bounds, Size body, Size speech, Rect target)
    {
        var focus = target.IsEmpty
            ? new Rect(bounds.Left+bounds.Width*.5,bounds.Top+bounds.Height*.56,1,1) : target;
        double cx = focus.Left+focus.Width/2, cy = focus.Top+focus.Height/2;
        var centers = new[]
        {
            new Point(focus.Right+24+body.Width/2,cy),
            new Point(focus.Left-24-body.Width/2,cy),
            new Point(cx,focus.Bottom+24+body.Height/2),
            new Point(cx,focus.Top-24-body.Height/2),
            new Point(focus.Right+24+body.Width/2,focus.Bottom+24+body.Height/2),
            new Point(focus.Left-24-body.Width/2,focus.Bottom+24+body.Height/2),
            new Point(bounds.Right-body.Width/2, bounds.Top+body.Height/2),
            new Point(bounds.Left+body.Width/2, bounds.Top+body.Height/2),
            new Point(bounds.Right-body.Width/2, bounds.Bottom-body.Height/2),
            new Point(bounds.Left+body.Width/2, bounds.Bottom-body.Height/2),
            new Point(bounds.Right-body.Width/2, bounds.Top+bounds.Height/2),
            new Point(bounds.Left+body.Width/2, bounds.Top+bounds.Height/2)
        };
        return centers.Select(p => Fit(new Rect(p.X-body.Width/2,p.Y-body.Height/2,body.Width,body.Height),bounds))
            .OrderBy(r =>
            {
                var card = Speech(bounds,speech,r,target,new Point(r.Left,r.Bottom+24));
                return (Overlap(r,target)+Overlap(card,target)+Overlap(card,r))*1000 +
                    Distance(r,focus)*8 + (card.TopLeft-r.TopLeft).Length*.5 +
                    (new Point(r.Left+r.Width/2,r.Top+r.Height/2)-new Point(cx,cy)).Length*.2;
            }).First();
    }
}
