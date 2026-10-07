using System.Windows.Media;

namespace ConditioningControlPanel.Controls.Billboard
{
    /// <summary>Tip "effects can reach a toy". STUB: the art lane replaces Paint.</summary>
    public sealed class HapticsTipArt : BillboardVectorArt
    {
        protected override void Paint(DrawingContext dc, double w, double h, double t) =>
            CachedGround(dc, w, h, Deep(Accent));
    }

    /// <summary>Tip "leave a whole folder out". STUB: the art lane replaces Paint.</summary>
    public sealed class FoldersTipArt : BillboardVectorArt
    {
        protected override void Paint(DrawingContext dc, double w, double h, double t) =>
            CachedGround(dc, w, h, Deep(Accent));
    }

    /// <summary>Tip "Lockdown holds a session". STUB: the art lane replaces Paint.</summary>
    public sealed class LockdownTipArt : BillboardVectorArt
    {
        protected override void Paint(DrawingContext dc, double w, double h, double t) =>
            CachedGround(dc, w, h, Deep(Accent));
    }

    /// <summary>Tip "someone else can drive". STUB: the art lane replaces Paint.</summary>
    public sealed class RemoteTipArt : BillboardVectorArt
    {
        protected override void Paint(DrawingContext dc, double w, double h, double t) =>
            CachedGround(dc, w, h, Deep(Accent));
    }
}
