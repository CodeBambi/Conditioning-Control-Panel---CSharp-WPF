using System.Windows.Media;

namespace ConditioningControlPanel.Controls.Billboard
{
    /// <summary>House card The Loom. STUB: the art lane replaces Paint.</summary>
    public sealed class LoomHouseArt : BillboardVectorArt
    {
        protected override void Paint(DrawingContext dc, double w, double h, double t) =>
            CachedGround(dc, w, h, Deep(Accent));
    }

    /// <summary>House card Support. STUB: the art lane replaces Paint.</summary>
    public sealed class SupportHouseArt : BillboardVectorArt
    {
        protected override void Paint(DrawingContext dc, double w, double h, double t) =>
            CachedGround(dc, w, h, Deep(Accent));
    }
}
