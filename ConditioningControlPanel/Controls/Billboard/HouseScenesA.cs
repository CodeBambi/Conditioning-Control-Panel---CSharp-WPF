using System.Windows.Media;

namespace ConditioningControlPanel.Controls.Billboard
{
    /// <summary>House card Discord. STUB: the art lane replaces Paint.</summary>
    public sealed class DiscordHouseArt : BillboardVectorArt
    {
        protected override void Paint(DrawingContext dc, double w, double h, double t) =>
            CachedGround(dc, w, h, Deep(Accent));
    }

    /// <summary>House card Web App. STUB: the art lane replaces Paint.</summary>
    public sealed class WebAppHouseArt : BillboardVectorArt
    {
        protected override void Paint(DrawingContext dc, double w, double h, double t) =>
            CachedGround(dc, w, h, Deep(Accent));
    }

    /// <summary>House card Remix Room. STUB: the art lane replaces Paint.</summary>
    public sealed class RemixHouseArt : BillboardVectorArt
    {
        protected override void Paint(DrawingContext dc, double w, double h, double t) =>
            CachedGround(dc, w, h, Deep(Accent));
    }
}
