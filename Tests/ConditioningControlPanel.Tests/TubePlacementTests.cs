using System;
using ConditioningControlPanel.AvatarTubeLayout;
using Xunit;

namespace ConditioningControlPanel.Tests
{
    public class SpeechBubblePlacementTests
    {
        // The tube's design canvas: 780x1080, avatar box roughly where the attached layout puts her.
        private static readonly Box Canvas = new(0, 0, 780, 1080);
        private static readonly Box Avatar = new(245, 564, 198, 306);   // centre x 344, head at 564

        private static (double W, double H) Fixed(double w, double h, double outerMax) => (Math.Min(w, outerMax), h);

        private static void AssertInside(Box inner, Box outer)
        {
            Assert.True(inner.X >= outer.X - 0.01, $"left {inner.X} < {outer.X}");
            Assert.True(inner.Y >= outer.Y - 0.01, $"top {inner.Y} < {outer.Y}");
            Assert.True(inner.Right <= outer.Right + 0.01, $"right {inner.Right} > {outer.Right}");
            Assert.True(inner.Bottom <= outer.Bottom + 0.01, $"bottom {inner.Bottom} > {outer.Bottom}");
        }

        [Fact]
        public void AttachedLeftOfMain_StaysOffMainAndAboveHer()
        {
            // Main's left edge on the seam (canvas x 427), whole canvas on screen.
            var main = new Box(427, -200, 1400, 1500);
            var plan = SpeechBubblePlacement.Place(Canvas, new Box(-500, -500, 3000, 3000), main, Avatar,
                380, 36, m => Fixed(380, 120, m));

            Assert.True(plan.Bubble.Right <= main.X - SpeechBubblePlacement.Gap + 0.01);
            Assert.True(plan.GrowsLeft);
            Assert.Equal(TailEdge.Bottom, plan.Tail);
            Assert.True(plan.Bubble.Bottom <= Avatar.Y);
            Assert.InRange(plan.TailX, plan.Bubble.X, plan.Bubble.Right);
        }

        [Fact]
        public void DockedRightOfMain_GrowsRightAndStaysOffMain()
        {
            // Main on the LEFT of the avatar now (right dock): main ends at canvas x 239.
            var main = new Box(-1400, -200, 1639, 1500);
            var plan = SpeechBubblePlacement.Place(Canvas, new Box(-2000, -500, 4000, 3000), main, Avatar,
                380, 36, m => Fixed(380, 120, m));

            Assert.False(plan.GrowsLeft);
            Assert.True(plan.Bubble.X >= main.Right + SpeechBubblePlacement.Gap - 0.01);
            AssertInside(plan.Bubble, Canvas);
        }

        [Fact]
        public void DetachedAtLeftScreenEdge_FlipsRightAndStaysOnScreen()
        {
            // The monitor starts at canvas x 230: the canvas' left part hangs off-screen.
            var work = new Box(230, 0, 2000, 1040);
            var plan = SpeechBubblePlacement.Place(Canvas, work, null, Avatar, 380, 36, m => Fixed(380, 120, m));

            Assert.False(plan.GrowsLeft);
            AssertInside(plan.Bubble, work);
            AssertInside(plan.Bubble, Canvas);
        }

        [Fact]
        public void DetachedAtRightScreenEdge_GrowsLeftAndStaysOnScreen()
        {
            var work = new Box(-2000, 0, 2460, 1040);   // right edge at canvas x 460
            var plan = SpeechBubblePlacement.Place(Canvas, work, null, Avatar, 380, 36, m => Fixed(380, 120, m));

            Assert.True(plan.GrowsLeft);
            AssertInside(plan.Bubble, work);
        }

        [Fact]
        public void AtTopScreenEdge_FlipsBelowHer()
        {
            // Monitor top at canvas y 520: no room above her head (564).
            var work = new Box(-500, 520, 3000, 3000);
            var plan = SpeechBubblePlacement.Place(Canvas, work, null, Avatar, 380, 36, m => Fixed(380, 120, m));

            Assert.Equal(TailEdge.Top, plan.Tail);
            Assert.True(plan.Bubble.Y >= Avatar.Bottom);
            AssertInside(plan.Bubble, Canvas);
        }

        [Fact]
        public void AtBottomScreenEdge_StaysAbove()
        {
            var work = new Box(-500, -500, 3000, 1300);   // bottom at canvas y 800
            var plan = SpeechBubblePlacement.Place(Canvas, work, null, Avatar, 380, 36, m => Fixed(380, 120, m));

            Assert.Equal(TailEdge.Bottom, plan.Tail);
            AssertInside(plan.Bubble, work);
        }

        [Fact]
        public void NarrowRoom_ShrinksTheBubbleInsteadOfClipping()
        {
            // Chat history asks for 600; only ~300 of screen is visible.
            var work = new Box(200, 0, 300, 1080);
            double askedInner = -1;
            var plan = SpeechBubblePlacement.Place(Canvas, work, null, Avatar, 600, 36, m =>
            {
                askedInner = m - 36;
                return (m, 200);
            });

            Assert.True(plan.Bubble.W <= work.W - 2 * SpeechBubblePlacement.Gap + 0.01);
            Assert.Equal(plan.Bubble.W - 36, plan.ContentMaxWidth, 3);
            Assert.Equal(plan.ContentMaxWidth, askedInner, 3);
            AssertInside(plan.Bubble, work);
        }

        [Fact]
        public void ShortLine_KeepsTailOnTheAvatar()
        {
            var plan = SpeechBubblePlacement.Place(Canvas, new Box(-500, -500, 3000, 3000), null, Avatar,
                380, 36, m => Fixed(140, 60, m));

            Assert.Equal(140, plan.Bubble.W, 3);
            double anchor = Avatar.X + Avatar.W / 2;
            Assert.Equal(anchor, plan.TailX, 3);
        }

        [Fact]
        public void HighDpiMonitor_SameAnswerInCanvasUnits()
        {
            // The caller divides screen px by the canvas scale; a 150% monitor at the left edge
            // maps to the same canvas work area as a 100% one, so the plan does not depend on DPI.
            Box Map(double scale, double originPx, double workLeftPx) =>
                new((workLeftPx - originPx) / scale, 0, 4000, 1040);
            var a = SpeechBubblePlacement.Place(Canvas, Map(1.0, -230, 0), null, Avatar, 380, 36, m => Fixed(380, 120, m));
            var b = SpeechBubblePlacement.Place(Canvas, Map(1.5, -345, 0), null, Avatar, 380, 36, m => Fixed(380, 120, m));
            Assert.Equal(a.Bubble, b.Bubble);
        }

        [Fact]
        public void TinyVisibleArea_NeverThrows()
        {
            var plan = SpeechBubblePlacement.Place(Canvas, new Box(700, 1000, 20, 20), null, Avatar, 380, 36, m => (m, 50));
            Assert.True(plan.Bubble.W > 0);
        }
    }

    public class TubeDockPlacementTests
    {
        // A 1920x1040 work area; tube 520x680 px with 159 px transparent left, 236 right (art 125 wide).
        private static readonly Box Work = new(0, 0, 1920, 1040);
        private const int TubeW = 520, TubeH = 680, LeftInset = 159, RightInset = 236;

        private static DockPlan Dock(Box main, Box work)
            => TubeDockPlacement.Place(main, TubeW, TubeH, LeftInset, RightInset, 13, work);

        private static void AssertArtOnScreen(DockPlan p, Box work)
        {
            double artLeft = p.Left + LeftInset, artRight = p.Left + TubeW - RightInset;
            Assert.True(artLeft >= work.X, $"art left {artLeft} < {work.X}");
            Assert.True(artRight <= work.Right, $"art right {artRight} > {work.Right}");
            Assert.True(p.Top >= work.Y && p.Top + TubeH <= work.Bottom);
        }

        [Fact]
        public void MainCentred_DocksLeftFlushOnTheSeam()
        {
            var main = new Box(600, 100, 1000, 800);
            var p = Dock(main, Work);
            Assert.Equal(DockSide.Left, p.Side);
            Assert.Equal(main.X, p.Left + TubeW - RightInset);
            AssertArtOnScreen(p, Work);
        }

        [Fact]
        public void MainAtLeftEdge_DocksRightInsteadOfCoveringTheRail()
        {
            var main = new Box(0, 100, 1000, 800);
            var p = Dock(main, Work);
            Assert.Equal(DockSide.Right, p.Side);
            Assert.Equal(main.Right, p.Left + LeftInset);   // art starts where main ends
            AssertArtOnScreen(p, Work);
        }

        [Fact]
        public void MainAtRightEdge_StaysLeft()
        {
            var main = new Box(920, 100, 1000, 800);
            var p = Dock(main, Work);
            Assert.Equal(DockSide.Left, p.Side);
            AssertArtOnScreen(p, Work);
        }

        [Fact]
        public void MainMaximised_FloatsWithAllArtOnScreen()
        {
            var p = Dock(Work, Work);
            Assert.Equal(DockSide.Float, p.Side);
            AssertArtOnScreen(p, Work);
        }

        [Fact]
        public void SecondMonitorLeftOfPrimary_UsesItsOwnWorkArea()
        {
            // Monitor at negative x; main hugs its left edge.
            var work = new Box(-2560, 0, 2560, 1400);
            var main = new Box(-2560, 200, 1200, 900);
            var p = Dock(main, work);
            Assert.Equal(DockSide.Right, p.Side);
            AssertArtOnScreen(p, work);
        }

        [Fact]
        public void TallMain_TopIsClampedIntoTheWorkArea()
        {
            var main = new Box(600, -300, 1000, 1000);
            var p = Dock(main, Work);
            Assert.True(p.Top >= Work.Y);
        }
    }

    public class MakeRoomPlacementTests
    {
        private static readonly Box Work = new(0, 0, 1680, 1002);

        [Fact]
        public void OwnersDesk_PanelMovesRightAndNarrowsForALeftDock()
        {
            // Owner's desk (Sep 25): panel 59..1681 on a 1680 screen, art 154 wide, no side fits.
            var main = Box.FromEdges(59, 30, 1681, 973);
            var t = MakeRoomPlacement.Plan(main, 900, 154, Work);
            Assert.NotNull(t);
            Assert.Equal(154, t!.Value.X);
            Assert.Equal(1680, t.Value.Right);
            Assert.Equal(main.Y, t.Value.Y);
            Assert.Equal(main.H, t.Value.H);
        }

        [Fact]
        public void MoveAlone_WhenTheScreenHasRoom()
        {
            var main = new Box(40, 0, 1200, 900);
            var t = MakeRoomPlacement.Plan(main, 900, 154, new Box(0, 0, 1350, 1000));
            Assert.NotNull(t);
            Assert.Equal(154, t!.Value.X);
            Assert.Equal(1196, t.Value.W);   // right edge capped to the monitor, narrowed by the rest
        }

        [Fact]
        public void NoMove_WhenASideAlreadyFits()
        {
            Assert.Null(MakeRoomPlacement.Plan(new Box(400, 0, 1000, 900), 900, 154, Work));
            Assert.Null(MakeRoomPlacement.Plan(new Box(0, 0, 1000, 900), 900, 154, Work));
        }

        [Fact]
        public void NeverBelowMinWidth()
        {
            var t = MakeRoomPlacement.Plan(new Box(0, 0, 1680, 1000), 1600, 154, Work);
            Assert.Null(t);
        }

        [Fact]
        public void State_RestoresOnce_AndForgetsWhenTheUserTakesOver()
        {
            var st = new MakeRoomState();
            var before = new Box(59, 30, 1622, 943);
            var target = new Box(154, 30, 1526, 943);
            Assert.True(st.CanAdjust);
            st.Applied(before, target);
            Assert.False(st.CanAdjust);
            Assert.False(st.NoteParentRect(target));             // our own move echoing back
            Assert.Null(st.TakeRestore(parentMinimized: true));  // never restore a minimised main
            Assert.Equal(before, st.TakeRestore(parentMinimized: false));
            Assert.Null(st.TakeRestore(parentMinimized: false));

            st.Applied(before, target);
            Assert.True(st.NoteParentRect(new Box(300, 30, 1200, 943)));   // the user moved it
            Assert.Null(st.TakeRestore(false));
            Assert.False(st.CanAdjust);
            st.Rearm();
            Assert.True(st.CanAdjust);
        }
    }

    public class SpeechBubbleVisibilityTests
    {
        [Theory]
        [InlineData(true, true, false, true, true, false, true)]
        [InlineData(false, true, false, true, true, false, false)]   // nothing to say
        [InlineData(true, false, false, true, true, false, false)]   // tube hidden
        [InlineData(true, true, true, true, true, false, false)]     // tube minimised
        [InlineData(true, true, false, true, false, false, false)]   // attached, main hidden
        [InlineData(true, true, false, true, true, true, false)]     // attached, main minimised
        [InlineData(true, true, false, false, true, true, true)]     // detached ignores main
        public void Rule(bool wanted, bool tubeVisible, bool tubeMin, bool attached, bool mainVisible, bool mainMin, bool expected)
            => Assert.Equal(expected, SpeechBubbleVisibility.ShouldShow(wanted, tubeVisible, tubeMin, attached, mainVisible, mainMin));
    }
}
