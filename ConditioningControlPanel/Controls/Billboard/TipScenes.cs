using System.Windows.Media;
using ConditioningControlPanel.Services.Billboard;

namespace ConditioningControlPanel.Controls.Billboard
{
    /// <summary>
    /// One picture per tip (owner, 2026-10-07: every tip had the same placeholder). The tip card
    /// carries its tip id as art data; an unknown id keeps the generic <see cref="TipArtView"/>.
    /// </summary>
    public static class TipScenes
    {
        public static System.Windows.FrameworkElement Create(string? id) => id switch
        {
            "quests" => new QuestsTipArt(),
            "awareness" => new AwarenessTipArt(),
            "programs" => new ProgramsTipArt(),
            "deeper" => new DeeperTipArt(),
            "blink" => new BlinkTipArt(),
            "haptics" => new HapticsTipArt(),
            "folders" => new FoldersTipArt(),
            "lockdown" => new LockdownTipArt(),
            "remote" => new RemoteTipArt(),
            _ => new TipArtView(),
        };
    }
}
