// PORTED from ConditioningControlPanel/MainWindow/MainWindow.EventFx.cs:249 CelebrateLevelUp - the
// burst half (the flash and the chip pop are HeroFx's FlashLevelUp/PopLevelChip). WPF fires
// LevelUpBurstCount (110) particles from the XP bar track's right edge, gated on transitions.
// Lane k16: plus MainWindow.ProfileBubble.cs:602, the second 45-spark burst at the profile bubble.

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

        /// <summary>WPF MainWindow.ProfileBubble.cs:602 (OnBubbleLevelUp): the second, smaller burst.</summary>
        internal const int ProfileBubbleBurstCount = 45;

        // Its own layer: the XP bar's burst is still in the air when this one fires, and one
        // layer is one box that moves with its newest burst.
        private readonly EventBurstLayer _profileBubbleBurst = new();

        /// <summary>Bursts fired at the profile bubble (test hook).</summary>
        internal int ProfileBubbleBursts => _profileBubbleBurst.Count;

        /// <summary>
        /// WPF FireBurstAt(BtnProfileBubble, count: 45) on level up: 45 sparks from the bubble's
        /// centre in the theme particle colour. IN = the burst, OUT = the sparks' own fade (the
        /// canvas holds no clock once they are gone). Gated like WPF EventFxAllowed by the layer
        /// itself: particles allowed (motion level Full and a tier with a particle budget), the
        /// bubble on screen, the window active and not minimised. False when nothing was drawn.
        /// </summary>
        internal bool BurstProfileBubble()
        {
            if (Named<global::Avalonia.Controls.Button>("BtnProfileBubble") is not { } bubble) return false;
            try { return _profileBubbleBurst.Fire(bubble, ProfileBubbleBurstCount); }
            catch (System.Exception ex) { Serilog.Log.Debug("Bubble level-up burst: {E}", ex.Message); return false; }
        }
    }
}
