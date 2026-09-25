using System;

namespace ConditioningControlPanel.AvatarTubeLayout
{
    /// <summary>A plain axis-aligned rectangle. WPF-free so the placement maths below is unit testable.</summary>
    public readonly record struct Box(double X, double Y, double W, double H)
    {
        public double Right => X + W;
        public double Bottom => Y + H;
        public bool IsEmpty => W <= 0 || H <= 0;

        public static Box FromEdges(double left, double top, double right, double bottom)
            => new(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));

        public Box Intersect(Box o) => FromEdges(
            Math.Max(X, o.X), Math.Max(Y, o.Y), Math.Min(Right, o.Right), Math.Min(Bottom, o.Bottom));

        public bool Contains(double x, double y) => x >= X && x <= Right && y >= Y && y <= Bottom;
    }

    /// <summary>Which edge of the bubble carries the tail (the tail points out of that edge at the avatar).</summary>
    public enum TailEdge { Bottom, Top }

    /// <summary>Result of <see cref="SpeechBubblePlacement.Place"/>, all in the caller's units.</summary>
    public readonly record struct BubblePlan(Box Bubble, double ContentMaxWidth, TailEdge Tail, double TailX, bool GrowsLeft);

    /// <summary>
    /// Where the avatar tube's speech bubble goes. Everything is in ONE coordinate space (the tube's
    /// design canvas in practice): the caller maps the monitor work area and the main window into it,
    /// so per-monitor DPI and the window's own scale fall out of that one linear map.
    ///
    /// <para>Rules: the bubble stays inside the visible area (canvas n work area), never covers the main
    /// window when the avatar is outside it (an opaque pixel over main swallows main's clicks), sits
    /// ABOVE her head and grows toward whichever side has more room, flips below her when there is no
    /// room above, and its tail stays aimed at her.</para>
    /// </summary>
    public static class SpeechBubblePlacement
    {
        public const double Gap = 12;          // daylight between bubble and main / screen edge
        public const double HeadGap = 16;      // between the bubble's tail edge and her head
        public const double TailInset = 42;    // tail distance from the bubble's near corner
        public const double TailHalfWidth = 9;
        public const double CornerRadius = 20;
        public const double MinWidth = 120;

        /// <param name="canvas">The area the bubble can physically paint in (the tube window).</param>
        /// <param name="workArea">The monitor work area the avatar is on, in the same units.</param>
        /// <param name="obstacle">The main window when the tube is docked to it, else null.</param>
        /// <param name="avatar">The avatar's box (her head is its top edge).</param>
        /// <param name="configuredMaxWidth">The bubble's own MaxWidth (380 normally, 600 for chat history).</param>
        /// <param name="chrome">Horizontal padding + border of the bubble (outer width minus content width).</param>
        /// <param name="edgeGap">Daylight kept from the work area's edges (defaults to <see cref="Gap"/>).</param>
        /// <param name="measure">Outer size of the bubble when laid out at a given outer max width.</param>
        public static BubblePlan Place(Box canvas, Box workArea, Box? obstacle, Box avatar,
            double configuredMaxWidth, double chrome, Func<double, (double W, double H)> measure,
            double edgeGap = Gap)
        {
            var allowed = canvas.Intersect(Box.FromEdges(
                workArea.X + edgeGap, workArea.Y + edgeGap, workArea.Right - edgeGap, workArea.Bottom - edgeGap));
            if (allowed.IsEmpty) allowed = canvas;   // nothing sensible to clamp to: keep old behaviour

            double anchorX = avatar.X + avatar.W / 2;
            double headY = avatar.Y;

            // Keep off the main window, on the side of it the avatar is on. When she is ON main
            // (tube floated over a maximised window) there is no clean side and the rule is dropped.
            if (obstacle is Box ob && !ob.IsEmpty && !ob.Contains(anchorX, headY + avatar.H / 2))
            {
                if (ob.X >= anchorX && ob.X - Gap < allowed.Right)
                    allowed = Box.FromEdges(allowed.X, allowed.Y, Math.Max(allowed.X, ob.X - Gap), allowed.Bottom);
                else if (ob.Right <= anchorX && ob.Right + Gap > allowed.X)
                    allowed = Box.FromEdges(Math.Min(allowed.Right, ob.Right + Gap), allowed.Y, allowed.Right, allowed.Bottom);
            }

            // The anchor itself may sit outside the allowed band (her art hangs off the monitor).
            double ax = Math.Clamp(anchorX, allowed.X, Math.Max(allowed.X, allowed.Right));

            double roomLeft = ax - allowed.X;
            double roomRight = allowed.Right - ax;
            bool growsLeft = roomLeft >= roomRight;

            double maxW = Math.Max(MinWidth, Math.Min(configuredMaxWidth, allowed.W));
            if (maxW > allowed.W && allowed.W > 0) maxW = allowed.W;
            var size = measure(maxW);
            double w = Math.Clamp(size.W, Math.Min(MinWidth, maxW), maxW);
            double h = Math.Max(0, size.H);

            // Horizontal: the tail sits TailInset in from the near corner, so her head is under it.
            double x = growsLeft ? ax + TailInset - w : ax - TailInset;
            x = Math.Clamp(x, allowed.X, Math.Max(allowed.X, allowed.Right - w));

            // Vertical: above her head, else below her, else pinned inside.
            double top = headY - HeadGap - h;
            var tail = TailEdge.Bottom;
            if (top < allowed.Y)
            {
                double below = avatar.Bottom + HeadGap;
                if (below + h <= allowed.Bottom && (allowed.Bottom - avatar.Bottom) > (headY - allowed.Y))
                {
                    top = below;
                    tail = TailEdge.Top;
                }
                else
                {
                    top = allowed.Y;
                }
            }
            if (top + h > allowed.Bottom) top = Math.Max(allowed.Y, allowed.Bottom - h);

            double tailMin = x + CornerRadius + TailHalfWidth, tailMax = x + w - CornerRadius - TailHalfWidth;
            double tailX = tailMax < tailMin ? x + w / 2 : Math.Clamp(anchorX, tailMin, tailMax);
            return new BubblePlan(new Box(x, top, w, h), Math.Max(0, maxW - chrome), tail, tailX, growsLeft);
        }
    }

    /// <summary>Which side of the main window the attached tube ended up on.</summary>
    public enum DockSide { Left, Right, Float }

    public readonly record struct DockPlan(int Left, int Top, DockSide Side);

    /// <summary>
    /// Where the ATTACHED tube window goes, in physical px (GetWindowRect space). The tube hangs off
    /// main's left edge by default. When that would push her painted art off the monitor it docks
    /// to main's right edge instead, and when neither side has room (main maximised or nearly) she
    /// floats with all her art on the monitor, on the side with more room. The window is never
    /// resized or transformed here, so her aspect cannot skew.
    /// </summary>
    public static class TubeDockPlacement
    {
        /// <param name="parent">Main window rect.</param>
        /// <param name="tubeW">Tube window width.</param>
        /// <param name="tubeH">Tube window height.</param>
        /// <param name="artLeftInset">Transparent px left of her painted art inside the tube window.</param>
        /// <param name="artRightInset">Transparent px right of her painted art.</param>
        /// <param name="verticalOffset">Nudge below main's vertical centre.</param>
        /// <param name="workArea">Work area of the monitor main is on.</param>
        public static DockPlan Place(Box parent, int tubeW, int tubeH, int artLeftInset, int artRightInset,
            int verticalOffset, Box workArea)
        {
            double painted = Math.Max(0, tubeW - artLeftInset - artRightInset);

            // Left dock: painted right edge on main's left edge.
            double leftDock = parent.X - tubeW + artRightInset;
            bool leftFits = leftDock + artLeftInset >= workArea.X;
            // Right dock: painted left edge on main's right edge.
            double rightDock = parent.Right - artLeftInset;
            bool rightFits = rightDock + artLeftInset + painted <= workArea.Right;

            double left;
            DockSide side;
            if (leftFits) { left = leftDock; side = DockSide.Left; }
            else if (rightFits) { left = rightDock; side = DockSide.Right; }
            else
            {
                // Float: keep the whole painted art on the monitor, nearest to the roomier side.
                double roomLeft = parent.X - workArea.X, roomRight = workArea.Right - parent.Right;
                double wanted = roomRight > roomLeft ? rightDock : leftDock;
                double minLeft = workArea.X - artLeftInset;
                double maxLeft = workArea.Right - artLeftInset - painted;
                left = maxLeft < minLeft ? minLeft : Math.Clamp(wanted, minLeft, maxLeft);
                side = DockSide.Float;
            }

            double top = parent.Y + (parent.H - tubeH) / 2 + verticalOffset;
            if (tubeH <= workArea.H) top = Math.Clamp(top, workArea.Y, workArea.Bottom - tubeH);
            else top = workArea.Y - (tubeH - workArea.H) / 2;

            return new DockPlan((int)Math.Round(left), (int)Math.Round(top), side);
        }
    }

    /// <summary>
    /// "Make room" (owner, Sep 25): when the attached tube fits on neither side of main, main
    /// moves and, if that is not enough, narrows (never below its MinWidth) just enough for the
    /// tube to dock fully on the monitor beside it: left preferred, else right. Physical px.
    /// </summary>
    public static class MakeRoomPlacement
    {
        /// <param name="parent">Main window rect now.</param>
        /// <param name="parentMinWidth">Main's MinWidth in px.</param>
        /// <param name="paintedWidth">Width of the tube's painted art (what must fit beside main).</param>
        /// <param name="workArea">Work area of main's monitor.</param>
        /// <returns>The new main rect, or null when no move is needed or none can make room.</returns>
        public static Box? Plan(Box parent, double parentMinWidth, double paintedWidth, Box workArea)
        {
            if (parent.IsEmpty || workArea.IsEmpty || paintedWidth <= 0) return null;
            double minW = Math.Max(1, parentMinWidth);

            bool leftFits = parent.X - paintedWidth >= workArea.X;
            bool rightFits = parent.Right + paintedWidth <= workArea.Right;
            if (leftFits || rightFits) return null;

            // Left dock: main's left edge moves to workLeft + art, its right edge stays on the monitor.
            double lx = workArea.X + paintedWidth;
            double lRight = Math.Min(lx + parent.W, workArea.Right);
            double lw = lRight - lx;
            if (lw >= minW) return new Box(lx, parent.Y, lw, parent.H);

            // Right dock: main's right edge moves to workRight - art, left edge stays on the monitor.
            double rRight = workArea.Right - paintedWidth;
            double rx = Math.Max(workArea.X, rRight - parent.W);
            double rw = rRight - rx;
            if (rw >= minW) return new Box(rRight - rw, parent.Y, rw, parent.H);

            return null;
        }
    }

    /// <summary>
    /// Bookkeeping for a make-room move: what main looked like before, what we moved it to, and
    /// whether the user has since taken over (then we forget and do not adjust again until the
    /// next attach or show).
    /// </summary>
    public sealed class MakeRoomState
    {
        public const double Tolerance = 3;

        public Box? Saved { get; private set; }
        public Box? Target { get; private set; }
        public bool Suppressed { get; private set; }

        public bool CanAdjust => !Suppressed && Saved == null;

        public void Applied(Box before, Box target)
        {
            Saved ??= before;   // the FIRST bounds are what a restore goes back to
            Target = target;
        }

        /// <summary>Main moved or resized. True when it was the user (not our own move): forget.</summary>
        public bool NoteParentRect(Box current)
        {
            if (Target is not Box t) return false;
            bool ours = Math.Abs(current.X - t.X) <= Tolerance && Math.Abs(current.Y - t.Y) <= Tolerance
                && Math.Abs(current.W - t.W) <= Tolerance && Math.Abs(current.H - t.H) <= Tolerance;
            if (ours) return false;
            Saved = null;
            Target = null;
            Suppressed = true;
            return true;
        }

        /// <summary>Tube detached, hidden or turned off: the rect to put main back to (once), or null.</summary>
        public Box? TakeRestore(bool parentMinimized)
        {
            if (parentMinimized || Saved is not Box s) return null;
            Saved = null;
            Target = null;
            return s;
        }

        /// <summary>A fresh attach or show re-arms the adjustment.</summary>
        public void Rearm() => Suppressed = false;
    }

    /// <summary>When the speech bubble's own window may be on screen.</summary>
    public static class SpeechBubbleVisibility
    {
        public static bool ShouldShow(bool bubbleWanted, bool tubeVisible, bool tubeMinimized,
            bool attached, bool parentVisible, bool parentMinimized)
        {
            if (!bubbleWanted || !tubeVisible || tubeMinimized) return false;
            if (attached && (!parentVisible || parentMinimized)) return false;
            return true;
        }
    }
}
