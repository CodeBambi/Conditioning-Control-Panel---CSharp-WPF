using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// The motion level the Down the Rabbit Hole page is told at init (<c>init.motionLevel</c>,
/// read by <c>Resources/web/dtrh/shared/motion.js</c>): "full", "reduced" or "off".
///
/// <para>It carries the player's own in-app Motion setting, never <c>MotionFx.Level</c>: that
/// one caps to Reduced when Windows' animation-effects flag is off, and the hosted-page rule
/// (ccp-bugs #980, <c>ChaosWebViewHost.PrefersReducedMotionArgument</c>) is that the OS flag
/// never reaches a page's content.</para>
/// </summary>
public static class DtrhMotionWire
{
    public const string Full = "full";
    public const string Reduced = "reduced";
    public const string Off = "off";

    /// <summary>Pure mapping from the setting to the wire word. Unknown values read as full.</summary>
    public static string For(MotionLevel setting) => setting switch
    {
        MotionLevel.Off => Off,
        MotionLevel.Reduced => Reduced,
        _ => Full,
    };

    /// <summary>The word for the current settings; "full" when settings are not loaded yet.</summary>
    public static string Current()
    {
        try { return For(App.Settings?.Current?.MotionLevel ?? MotionLevel.Full); }
        catch { return Full; }
    }
}
