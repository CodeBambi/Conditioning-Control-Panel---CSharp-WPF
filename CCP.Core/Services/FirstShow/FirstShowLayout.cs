using System;
using System.Linq;
using ConditioningControlPanel.Fx;

namespace ConditioningControlPanel.Services.FirstShow;

/// <summary>A neutral size (WPF Size, Avalonia Size).</summary>
internal readonly record struct ShowSize(double Width, double Height);

// PORTED from ConditioningControlPanel/Services/FirstShow/FirstShowLayout.cs, numbers verbatim; the WPF
// Rect / Size / Point are Core's RectD / ShowSize / Vec2 so both heads share one rule.
// All rectangles use stage DIPs. A monitor work area, never the virtual desktop, is the safe boundary.
internal static class FirstShowLayout
{
    /// <summary>WPF Rect.Empty: "no target".</summary>
    internal static readonly RectD Empty = default;
    internal static bool IsEmpty(RectD r) => r.Width <= 0 || r.Height <= 0;

    internal static RectD Fit(RectD rect, RectD bounds) => new(
        Math.Clamp(rect.Left, bounds.Left, Math.Max(bounds.Left, bounds.Right - rect.Width)),
        Math.Clamp(rect.Top, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - rect.Height)),
        Math.Min(rect.Width, bounds.Width), Math.Min(rect.Height, bounds.Height));

    internal static double Overlap(RectD a, RectD b)
    {
        if (IsEmpty(a) || IsEmpty(b)) return 0;
        double w = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
        double h = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
        // WPF Rect.Intersect keeps a touching edge as a zero-area rect: area 0 either way.
        return w < 0 || h < 0 ? 0 : w * h;
    }

    private static double Length(double dx, double dy) => Math.Sqrt(dx * dx + dy * dy);

    internal static RectD Speech(RectD bounds, ShowSize size, RectD body, RectD target, Vec2 preferred)
    {
        double w = Math.Min(size.Width, bounds.Width), h = Math.Min(size.Height, bounds.Height);
        var candidates = new[]
        {
            new RectD(preferred.X, preferred.Y, w, h),
            new RectD(body.Right + 18, body.Top, w, h), new RectD(body.Left - w - 18, body.Top, w, h),
            new RectD(body.Left + (body.Width - w) / 2, body.Bottom + 18, w, h),
            new RectD(body.Left + (body.Width - w) / 2, body.Top - h - 18, w, h),
            new RectD(bounds.Left, bounds.Top, w, h), new RectD(bounds.Right - w, bounds.Top, w, h),
            new RectD(bounds.Left, bounds.Bottom - h, w, h), new RectD(bounds.Right - w, bounds.Bottom - h, w, h)
        };
        var anchor = body;
        body = new RectD(body.Left - 10, body.Top - 10, body.Width + 20, body.Height + 20);
        return candidates.Select(x => Fit(x, bounds)).OrderBy(x =>
            Overlap(x, body) * 1000 + Overlap(x, target) * 1000 +
            Distance(x, anchor) * 8 + Length(x.Left - preferred.X, x.Top - preferred.Y)).First();
    }

    private static double Distance(RectD a, RectD b)
    {
        double dx = Math.Max(0, Math.Max(a.Left - b.Right, b.Left - a.Right));
        double dy = Math.Max(0, Math.Max(a.Top - b.Bottom, b.Top - a.Bottom));
        return Math.Sqrt(dx * dx + dy * dy);
    }

    internal static RectD GuideBody(RectD bounds, ShowSize body, ShowSize speech, RectD target)
    {
        var focus = IsEmpty(target)
            ? new RectD(bounds.Left + bounds.Width * .5, bounds.Top + bounds.Height * .56, 1, 1) : target;
        double cx = focus.Left + focus.Width / 2, cy = focus.Top + focus.Height / 2;
        var centers = new[]
        {
            new Vec2(focus.Right + 24 + body.Width / 2, cy),
            new Vec2(focus.Left - 24 - body.Width / 2, cy),
            new Vec2(cx, focus.Bottom + 24 + body.Height / 2),
            new Vec2(cx, focus.Top - 24 - body.Height / 2),
            new Vec2(focus.Right + 24 + body.Width / 2, focus.Bottom + 24 + body.Height / 2),
            new Vec2(focus.Left - 24 - body.Width / 2, focus.Bottom + 24 + body.Height / 2),
            new Vec2(bounds.Right - body.Width / 2, bounds.Top + body.Height / 2),
            new Vec2(bounds.Left + body.Width / 2, bounds.Top + body.Height / 2),
            new Vec2(bounds.Right - body.Width / 2, bounds.Bottom - body.Height / 2),
            new Vec2(bounds.Left + body.Width / 2, bounds.Bottom - body.Height / 2),
            new Vec2(bounds.Right - body.Width / 2, bounds.Top + bounds.Height / 2),
            new Vec2(bounds.Left + body.Width / 2, bounds.Top + bounds.Height / 2)
        };
        return centers.Select(p => Fit(new RectD(p.X - body.Width / 2, p.Y - body.Height / 2, body.Width, body.Height), bounds))
            .OrderBy(r =>
            {
                var card = Speech(bounds, speech, r, target, new Vec2(r.Left, r.Bottom + 24));
                return (Overlap(r, target) + Overlap(card, target) + Overlap(card, r)) * 1000 +
                    Distance(r, focus) * 8 + Length(card.Left - r.Left, card.Top - r.Top) * .5 +
                    Length(r.Left + r.Width / 2 - cx, r.Top + r.Height / 2 - cy) * .2;
            }).First();
    }
}
