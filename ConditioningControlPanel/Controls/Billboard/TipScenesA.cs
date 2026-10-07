using System.Windows.Media;

namespace ConditioningControlPanel.Controls.Billboard
{
    /// <summary>Tip "a quest can be swapped". STUB: the art lane replaces Paint.</summary>
    public sealed class QuestsTipArt : BillboardVectorArt
    {
        protected override void Paint(DrawingContext dc, double w, double h, double t) =>
            CachedGround(dc, w, h, Deep(Accent));
    }

    /// <summary>Tip "the app can read the room". STUB: the art lane replaces Paint.</summary>
    public sealed class AwarenessTipArt : BillboardVectorArt
    {
        protected override void Paint(DrawingContext dc, double w, double h, double t) =>
            CachedGround(dc, w, h, Deep(Accent));
    }

    /// <summary>Tip "programs plan the days". STUB: the art lane replaces Paint.</summary>
    public sealed class ProgramsTipArt : BillboardVectorArt
    {
        protected override void Paint(DrawingContext dc, double w, double h, double t) =>
            CachedGround(dc, w, h, Deep(Accent));
    }

    /// <summary>Tip "Deeper times effects to a video". STUB: the art lane replaces Paint.</summary>
    public sealed class DeeperTipArt : BillboardVectorArt
    {
        protected override void Paint(DrawingContext dc, double w, double h, double t) =>
            CachedGround(dc, w, h, Deep(Accent));
    }

    /// <summary>Tip "Blink Trainer keeps score". STUB: the art lane replaces Paint.</summary>
    public sealed class BlinkTipArt : BillboardVectorArt
    {
        protected override void Paint(DrawingContext dc, double w, double h, double t) =>
            CachedGround(dc, w, h, Deep(Accent));
    }
}
