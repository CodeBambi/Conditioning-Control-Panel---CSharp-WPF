// PORTED from ConditioningControlPanel/MainWindow/MainWindow.EventFx.cs:249 CelebrateLevelUp - the
// burst half (the flash and the chip pop are HeroFx's FlashLevelUp/PopLevelChip). WPF fires
// LevelUpBurstCount (110) particles from the XP bar track's right edge, gated on transitions.

using ConditioningControlPanel.Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>WPF EventFx.cs:70.</summary>
        internal const int LevelUpBurstCount = 110;

        private readonly EventBurstLayer _levelUpBurst = new();

        /// <summary>WPF FireBurstAt(XPBarTrack, FxBurstSpot.RightEdge, LevelUpBurstCount). False when
        /// nothing was drawn (motion off, shell hidden or inactive, bar not laid out).</summary>
        internal bool BurstLevelUp()
        {
            if (!AmbientFxCanvas.Env.AllowTransitions || Named<global::Avalonia.Controls.Border>("XPBarTrack") is not { } track) return false;
            try { return _levelUpBurst.Fire(track, LevelUpBurstCount, rightEdge: true); }
            catch (System.Exception ex) { Serilog.Log.Debug("CelebrateLevelUp: {E}", ex.Message); return false; }
        }
    }
}
