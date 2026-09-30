using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The haptic seam: the one <see cref="HapticService"/> a head built, so Core engines and head
    /// overlays call it exactly where WPF calls <c>App.Haptics</c>. Null = no haptics on this head
    /// (tests, smoke): every call site uses <c>?.</c>, as WPF does. The premium gate and the master
    /// toggle live in <c>HapticMixer.IsGateOpen</c>, not at call sites.
    /// </summary>
    public static class CoreHaptics
    {
        public static volatile HapticService? Service;
    }
}
